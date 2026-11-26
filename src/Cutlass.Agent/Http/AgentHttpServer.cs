using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using Cutlass.Agent.Api;
using Cutlass.Agent.Tools.Dispatch;

namespace Cutlass.Agent.Http;

/// <summary>
/// The loopback HTTP host of the agent API (AGENT_INTEGRATION 4.1) on <see cref="HttpListener"/>. One accept loop on
/// the thread pool and one task per request; it never blocks the UI thread. Every request passes
/// <see cref="AuthGuard"/> and the <see cref="RateLimiter"/>, then <see cref="Router"/> maps it to a tool call for
/// the <see cref="ToolDispatcher"/>. On Windows the prefix is <c>http://localhost:&lt;port&gt;/</c> (a normal user may
/// register it), falling back to <c>127.0.0.1</c>; elsewhere it is <c>http://127.0.0.1:&lt;port&gt;/</c>.
/// </summary>
public sealed class AgentHttpServer : IAsyncDisposable
{
    /// <summary>The default port.</summary>
    public const int DefaultPort = 47390;

    /// <summary>How many ports are tried from the preferred one when it is not fixed.</summary>
    public const int PortAttempts = 10;

    private readonly ToolDispatcher _dispatcher;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _requests = [];
    private readonly object _gate = new();
    private HttpListener? _listener;
    private Task? _loop;
    private volatile string _token;

    /// <summary>Creates the server (not started).</summary>
    /// <param name="dispatcher">Runs the tools.</param>
    /// <param name="token">The bearer token.</param>
    /// <param name="limiter">The rate limiter, or null for 30 per second with a burst of 60.</param>
    public AgentHttpServer(ToolDispatcher dispatcher, string token, RateLimiter? limiter = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _dispatcher = dispatcher;
        _token = token;
        Limiter = limiter ?? new RateLimiter();
    }

    /// <summary>The bearer token; replace it after "Regenerate token".</summary>
    public string Token
    {
        get => _token;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _token = value;
        }
    }

    /// <summary>The rate limiter.</summary>
    public RateLimiter Limiter { get; }

    /// <summary>The port, or 0 before start.</summary>
    public int Port { get; private set; }

    /// <summary>The base URL clients use verbatim, for example <c>http://localhost:47390/</c>; null before start.</summary>
    public string? BaseUrl { get; private set; }

    /// <summary>True while listening.</summary>
    public bool IsRunning => _listener?.IsListening == true;

    /// <summary>
    /// Starts listening on <paramref name="port"/>, or with <paramref name="fixedPort"/> false on the first free port
    /// of the next <see cref="PortAttempts"/>.
    /// </summary>
    /// <param name="port">The preferred port.</param>
    /// <param name="fixedPort">True to use only that port.</param>
    /// <returns>A task that completes when listening.</returns>
    /// <exception cref="HttpListenerException">When no port could be bound; the message names the last error.</exception>
    public Task StartAsync(int port = DefaultPort, bool fixedPort = false)
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("The server is already started.");
        }

        HttpListenerException? last = null;
        var attempts = fixedPort ? 1 : PortAttempts;
        for (var i = 0; i < attempts; i++)
        {
            var candidate = port + i;
            foreach (var host in OperatingSystem.IsWindows() ? new[] { "localhost", "127.0.0.1" } : ["127.0.0.1"])
            {
                var prefix = string.Create(CultureInfo.InvariantCulture, $"http://{host}:{candidate}/");
                var listener = new HttpListener { IgnoreWriteExceptions = true };
                listener.Prefixes.Add(prefix);
                try
                {
                    listener.Start();
                }
                catch (HttpListenerException ex)
                {
                    last = ex;
                    listener.Close();

                    // Access denied: try the other host name; a port in use: try the next port.
                    if (ex.ErrorCode == 5)
                    {
                        continue;
                    }

                    break;
                }

                _listener = listener;
                Port = candidate;
                BaseUrl = prefix;
                _loop = Task.Run(AcceptLoopAsync);
                return Task.CompletedTask;
            }
        }

        var hint = OperatingSystem.IsWindows()
            ? string.Create(CultureInfo.InvariantCulture, $" If access is denied, run: netsh http add urlacl url=http://localhost:{port}/ user=%USERNAME%")
            : string.Empty;
        throw new HttpListenerException(last?.ErrorCode ?? 0, (fixedPort
            ? string.Create(CultureInfo.InvariantCulture, $"Port {port} is in use or not allowed")
            : string.Create(CultureInfo.InvariantCulture, $"No port from {port} to {port + attempts - 1} could be used")) + ": " + (last?.Message ?? "unknown error") + "." + hint);
    }

    /// <summary>Stops listening and waits for the running requests.</summary>
    /// <returns>A task that completes when stopped.</returns>
    public async Task StopAsync()
    {
        if (_listener is not { } listener)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            listener.Stop();
            listener.Close();
        }
        catch (ObjectDisposedException)
        {
            // Already closed.
        }

        if (_loop is { } loop)
        {
            await loop.ConfigureAwait(false);
        }

        Task[] running;
        lock (_gate)
        {
            running = [.. _requests];
        }

        await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _listener = null;
    }

    /// <summary>Stops the server.</summary>
    /// <returns>A task that completes when stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        var listener = _listener!;
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            var task = Task.Run(() => HandleAsync(context));
            lock (_gate)
            {
                _requests.RemoveAll(t => t.IsCompleted);
                _requests.Add(task);
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        ApiResult result;
        try
        {
            var refused = AuthGuard.Check(
                request.RemoteEndPoint?.Address,
                request.Headers["Host"],
                request.Headers["Origin"],
                request.Headers["Authorization"],
                Port,
                _token);
            if (refused is not null)
            {
                result = ApiResult.Fail(_dispatcher.Revision.Current, refused);
            }
            else if (!Limiter.TryTake(out var retry))
            {
                result = ApiResult.Fail(_dispatcher.Revision.Current, new ApiError(ErrorCode.RateLimited, "Too many requests. Slow down and retry after the suggested delay.", null, retry));
            }
            else
            {
                var body = await BodyReader.ReadAsync(request.InputStream, request.ContentLength64, _stop.Token).ConfigureAwait(false);
                var call = Router.Route(
                    request.HttpMethod,
                    request.Url?.AbsolutePath ?? "/",
                    Query(request.QueryString),
                    body,
                    Header(request, ApiVersion.LockHeader),
                    Header(request, ApiVersion.AgentHeader));
                result = await _dispatcher.DispatchAsync(call, _stop.Token).ConfigureAwait(false);
                if (Router.IsCallRoute(request.Url?.AbsolutePath ?? "/"))
                {
                    // The generic contract (AGENT_INTEGRATION 9.1): /v1/call always answers with the envelope.
                    result = result.AsCallEnvelope();
                }
            }
        }
        catch (ApiException ex)
        {
            result = ApiResult.Fail(_dispatcher.Revision.Current, ex.Error);
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException or OperationCanceledException or ObjectDisposedException)
        {
            try
            {
                context.Response.Abort();
            }
            catch (Exception abort) when (abort is ObjectDisposedException or HttpListenerException)
            {
                // Already gone.
            }

            return;
        }

        await ResponseWriter.WriteAsync(context.Response, result, CancellationToken.None).ConfigureAwait(false);
    }

    private static string? Header(HttpListenerRequest request, string name) =>
        request.Headers[name] is { Length: > 0 } value ? value.Trim() : null;

    private static List<KeyValuePair<string, string>> Query(NameValueCollection query)
    {
        var list = new List<KeyValuePair<string, string>>();
        foreach (var key in query.AllKeys)
        {
            if (key is not null && query[key] is { } value)
            {
                list.Add(new(key, value));
            }
        }

        return list;
    }
}
