using System.Diagnostics;
using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Agent.Http;
using Microsoft.Extensions.Logging;

namespace Cutlass.Agent.Mcp.Backend;

/// <summary>
/// The proxy backend (docs/AGENT_INTEGRATION.md 5.1): sends each tool call to the running app's HTTP API through
/// <see cref="AgentClient"/>, which maps catalog names to routes. The endpoint is located lazily and again after any
/// connection failure (the call is then retried once). With no app serving, every call fails with the 5.1 message;
/// with <c>--launch</c> the app is started with <c>--agent-server</c> first and given 30 s to come up.
/// </summary>
public sealed class HttpBackend : AgentBackendBase
{
    /// <summary>The 5.1 message when no app serves agents.</summary>
    public const string NotRunningMessage =
        "Cutlass is not running with agent connections enabled. Ask the user to start Cutlass and turn on Settings > Agents > Allow agent connections, or to start it with --agent-server.";

    private static readonly TimeSpan LaunchWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LaunchPoll = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(150);

    private readonly EndpointLocator _locator;
    private readonly string? _launchPath;
    private readonly SemaphoreSlim _connect = new(1, 1);
    private AgentClient? _client;
    private string? _clientBaseUrl;
    private bool _launched;
    private Process? _launchedApp;

    /// <summary>Creates the backend.</summary>
    /// <param name="locator">Finds the app's endpoint and token.</param>
    /// <param name="launchPath">The app to start with <c>--agent-server</c> when none serves, or null.</param>
    /// <param name="logger">The logger (stderr).</param>
    public HttpBackend(EndpointLocator locator, string? launchPath, ILogger<HttpBackend> logger)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _locator = locator;
        _launchPath = launchPath;
    }

    /// <inheritdoc />
    public override string Kind => "proxy";

    /// <inheritdoc />
    protected override async Task<ApiResult> SendAsync(string tool, JsonElement args, string? lockId, string? agentName, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var client = await ConnectAsync(forceReload: attempt > 0, ct).ConfigureAwait(false);
            if (client is null)
            {
                return ApiResult.Fail(0, new ApiError(ErrorCode.Unavailable, NotRunningMessage, new Dictionary<string, string> { ["reason"] = "not_running" }));
            }

            // Only this backend uses the client, and every call of a session carries the same lock id and name.
            client.LockId = lockId;
            client.AgentName = agentName;
            try
            {
                return await client.CallAsync(tool, args, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt == 0)
            {
                Logger.LogInformation("The Cutlass agent API did not answer ({Message}); looking for it again.", ex.Message);
            }
            catch (HttpRequestException ex)
            {
                Logger.LogWarning("The Cutlass agent API did not answer: {Message}", ex.Message);
                return ApiResult.Fail(0, new ApiError(ErrorCode.Unavailable, NotRunningMessage, new Dictionary<string, string> { ["reason"] = "connection_failed" }));
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return ApiResult.Fail(0, new ApiError(ErrorCode.Busy, "Cutlass did not answer within the request timeout. Check the app, then retry.", null, 2000));
            }
        }
    }

    /// <inheritdoc />
    protected override ValueTask DisposeCoreAsync()
    {
        _client?.Dispose();
        _client = null;
        _connect.Dispose();

        // Disposing the handle leaves the launched app running; it belongs to the user now.
        _launchedApp?.Dispose();
        _launchedApp = null;
        return ValueTask.CompletedTask;
    }

    private async Task<AgentClient?> ConnectAsync(bool forceReload, CancellationToken ct)
    {
        await _connect.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_client is not null && !forceReload)
            {
                return _client;
            }

            var endpoint = _locator.Locate();
            if (endpoint is null && _launchPath is not null && !_launched)
            {
                endpoint = await LaunchAsync(_launchPath, ct).ConfigureAwait(false);
            }

            if (endpoint is null)
            {
                _client?.Dispose();
                _client = null;
                _clientBaseUrl = null;
                return null;
            }

            if (_client is null || !string.Equals(_clientBaseUrl, endpoint.Info.BaseUrl, StringComparison.Ordinal) || forceReload)
            {
                _client?.Dispose();
                _client = new AgentClient(endpoint.Info.BaseUrl, endpoint.Token)
                {
                    // An acquire may wait 120 s for a busy user (3.2); leave room above it.
                    Timeout = RequestTimeout,
                };
                _clientBaseUrl = endpoint.Info.BaseUrl;
                Logger.LogInformation("Connected to the Cutlass agent API at {Endpoint}.", endpoint.ToString());
            }

            return _client;
        }
        finally
        {
            _connect.Release();
        }
    }

    private async Task<LocatedEndpoint?> LaunchAsync(string appPath, CancellationToken ct)
    {
        _launched = true;
        if (!File.Exists(appPath))
        {
            Logger.LogWarning("--launch: {Path} does not exist.", appPath);
            return null;
        }

        // Not an ffmpeg process, so ProcessRunner does not apply (CLAUDE.md "Processes"): the app is an interactive
        // program that outlives this server. It must never inherit this process's stdout, which is the MCP protocol:
        // on Windows the shell start inherits no handles; elsewhere its streams are redirected and drained.
        var shell = OperatingSystem.IsWindows();
        var start = new ProcessStartInfo(appPath)
        {
            UseShellExecute = shell,
            RedirectStandardInput = !shell,
            RedirectStandardOutput = !shell,
            RedirectStandardError = !shell,
            WorkingDirectory = Path.GetDirectoryName(appPath) ?? Environment.CurrentDirectory,
        };
        start.ArgumentList.Add("--agent-server");
        Logger.LogInformation("Starting {Path} --agent-server.", appPath);
        var app = Process.Start(start);
        _launchedApp = app;
        if (app is not null && !shell)
        {
            app.OutputDataReceived += (_, _) => { };
            app.ErrorDataReceived += (_, _) => { };
            app.BeginOutputReadLine();
            app.BeginErrorReadLine();
            app.StandardInput.Close();
        }

        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < LaunchWait)
        {
            await Task.Delay(LaunchPoll, ct).ConfigureAwait(false);
            if (_locator.Locate() is { } endpoint)
            {
                return endpoint;
            }
        }

        Logger.LogWarning("Cutlass did not start serving agents within {Seconds} s.", (int)LaunchWait.TotalSeconds);
        return null;
    }
}
