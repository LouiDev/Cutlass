using System.Text.Json;
using Cutlass.Agent.Api;
using Microsoft.Extensions.Logging;

namespace Cutlass.Agent.Mcp.Backend;

/// <summary>
/// The lock bookkeeping both backends share (docs/AGENT_INTEGRATION.md 5.1): it remembers the lock id a successful
/// <c>lock.acquire</c> returns and the agent name it was acquired with, sends both with every later call, forgets them
/// after a successful <c>lock.release</c>, and releases a lock still held when the session ends. A lost lock (revoked
/// or expired) keeps its id, so later calls keep reporting <c>lock_lost</c> with its reason instead of a vaguer
/// <c>lock_required</c>. It never renews on a timer (3.3): a dead or stuck agent must lose the lock.
/// </summary>
public abstract class AgentBackendBase : IAgentBackend
{
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(5);
    private readonly object _gate = new();
    private string? _lockId;
    private string? _agentName;
    private int _disposed;

    /// <summary>Creates the base.</summary>
    /// <param name="logger">The logger (stderr).</param>
    protected AgentBackendBase(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract string Kind { get; }

    /// <inheritdoc />
    public string? LockId
    {
        get
        {
            lock (_gate)
            {
                return _lockId;
            }
        }
    }

    /// <summary>The agent name of the last acquire, sent as <c>X-Cutlass-Agent</c>, or null.</summary>
    protected string? AgentName
    {
        get
        {
            lock (_gate)
            {
                return _agentName;
            }
        }
    }

    /// <summary>The logger.</summary>
    protected ILogger Logger { get; }

    /// <inheritdoc />
    public async Task<ApiResult> CallAsync(string tool, JsonElement args, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        string? lockId;
        string? agentName;
        lock (_gate)
        {
            lockId = _lockId;
            agentName = _agentName;
        }

        if (tool == "lock.acquire" && args.ValueKind == JsonValueKind.Object &&
            args.TryGetProperty("agentName", out var name) && name.ValueKind == JsonValueKind.String)
        {
            agentName = name.GetString();
        }

        var result = await SendAsync(tool, args, lockId, agentName, ct).ConfigureAwait(false);
        if (!result.IsError)
        {
            if (tool == "lock.acquire" && result.Result is { ValueKind: JsonValueKind.Object } grant &&
                grant.TryGetProperty("lockId", out var id) && id.ValueKind == JsonValueKind.String)
            {
                lock (_gate)
                {
                    _lockId = id.GetString();
                    _agentName = agentName;
                }
            }
            else if (tool == "lock.release")
            {
                lock (_gate)
                {
                    _lockId = null;
                }
            }
        }

        return result;
    }

    /// <summary>Releases a lock the session still holds, then frees the backend's resources.</summary>
    /// <returns>A task that completes when done.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (LockId is not null)
        {
            // AGENT_INTEGRATION 3.5 "Agent gone": the proxy releases the lock when its stdin closes.
            using var cts = new CancellationTokenSource(ReleaseTimeout);
            try
            {
                var result = await CallAsync("lock.release", Cutlass.Agent.Tools.AgentCall.EmptyArgs, cts.Token).ConfigureAwait(false);
                Logger.LogInformation(
                    "Released the edit lock on exit: {Outcome}.",
                    result.Error is { } error ? error.CodeName : "ok");
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException or InvalidOperationException)
            {
                Logger.LogWarning("Could not release the edit lock on exit: {Message}", ex.Message);
            }
        }

        await DisposeCoreAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Sends one call with the session's lock id and agent name.</summary>
    /// <param name="tool">The catalog name.</param>
    /// <param name="args">The arguments.</param>
    /// <param name="lockId">The lock id to send, or null.</param>
    /// <param name="agentName">The agent name to send, or null.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The result; API failures are error results, never exceptions.</returns>
    protected abstract Task<ApiResult> SendAsync(string tool, JsonElement args, string? lockId, string? agentName, CancellationToken ct);

    /// <summary>Frees the backend's own resources after the lock was released.</summary>
    /// <returns>A task that completes when done.</returns>
    protected abstract ValueTask DisposeCoreAsync();
}
