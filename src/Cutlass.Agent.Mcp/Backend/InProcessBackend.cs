using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Agent.Hosting;
using Cutlass.Agent.Tools.Audit;
using Cutlass.Agent.Tools.Dispatch;
using Microsoft.Extensions.Logging;

namespace Cutlass.Agent.Mcp.Backend;

/// <summary>
/// The headless backend (docs/AGENT_INTEGRATION.md 5.1, <c>--headless</c>): a <see cref="ToolDispatcher"/> over an
/// in-process <see cref="HeadlessHost"/>, with no HTTP in between. Calls take the same path as the HTTP host's, so
/// tools are written once. Disposing releases a held lock, then the dispatcher and, when owned, the host (which writes
/// an autosave of unsaved changes and never overwrites the project).
/// </summary>
public sealed class InProcessBackend : AgentBackendBase
{
    private readonly bool _ownsHost;

    /// <summary>Creates the backend over a started host.</summary>
    /// <param name="host">The headless host.</param>
    /// <param name="ownsHost">True to dispose the host with the backend.</param>
    /// <param name="logger">The logger (stderr).</param>
    public InProcessBackend(HeadlessHost host, bool ownsHost, ILogger<InProcessBackend> logger)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(host);
        Host = host;
        _ownsHost = ownsHost;
        Dispatcher = new ToolDispatcher(host, new AuditLog(host.Engine.Log));
    }

    /// <summary>The host.</summary>
    public HeadlessHost Host { get; }

    /// <summary>The dispatcher every call runs through.</summary>
    public ToolDispatcher Dispatcher { get; }

    /// <inheritdoc />
    public override string Kind => "headless";

    /// <inheritdoc />
    protected override Task<ApiResult> SendAsync(string tool, JsonElement args, string? lockId, string? agentName, CancellationToken ct) =>
        Dispatcher.DispatchAsync(new AgentCall(tool, args, lockId, agentName), ct);

    /// <inheritdoc />
    protected override async ValueTask DisposeCoreAsync()
    {
        Dispatcher.Dispose();
        if (_ownsHost)
        {
            await Host.DisposeAsync().ConfigureAwait(false);
        }
    }
}
