using System.Text.Json;
using Cutlass.Agent.Http;

namespace Cutlass.App.Services.Smoke;

/// <summary>
/// The smoke runner's agent (AGENT_INTEGRATION 6.4): an <see cref="AgentClient"/> built from <c>endpoint.json</c> and
/// the token file, calling tools through <c>POST /v1/call</c> over real HTTP. It keeps the lock id the way an MCP
/// proxy does, so later calls carry <c>X-Cutlass-Lock</c>.
/// </summary>
public sealed class SmokeAgentConnection : IDisposable
{
    private readonly AgentClient _client;

    private SmokeAgentConnection(AgentClient client)
    {
        _client = client;
    }

    /// <summary>The server's base URL.</summary>
    public string BaseUrl => _client.BaseUrl;

    /// <summary>The lock id sent with every call, or null.</summary>
    public string? LockId
    {
        get => _client.LockId;
        set => _client.LockId = value;
    }

    /// <summary>Reads the endpoint and token files (off the UI thread) and creates the client.</summary>
    /// <param name="agentDirectory">The agent folder, <c>AppPaths.AgentDirectory</c>.</param>
    /// <param name="agentName">The name sent as <c>X-Cutlass-Agent</c>.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="InvalidOperationException">When no server is recorded in <c>endpoint.json</c>.</exception>
    public static async Task<SmokeAgentConnection> ConnectAsync(string agentDirectory, string agentName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentDirectory);
        var client = await Task.Run(() => AgentClient.FromAgentDirectory(agentDirectory), ct)
            ?? throw new InvalidOperationException("no agent server is recorded in " + Path.Combine(agentDirectory, EndpointFile.FileName) + " (start the app with --agent-server)");
        client.AgentName = agentName;
        client.Timeout = TimeSpan.FromSeconds(100);
        return new SmokeAgentConnection(client);
    }

    /// <summary>Runs a tool through <c>/v1/call</c>.</summary>
    /// <param name="tool">The catalog name.</param>
    /// <param name="jsonArgs">The arguments as JSON text.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The code (<c>ok</c> or the error code), the envelope as JSON, and the PNG of an image answer.</returns>
    public async Task<SmokeAgentResponse> CallAsync(string tool, string jsonArgs, CancellationToken ct)
    {
        using var args = JsonDocument.Parse(jsonArgs);
        var result = await _client.CallGenericAsync(tool, args.RootElement.Clone(), ct);
        var envelope = result.Png is null ? JsonDocument.Parse(result.ToJson()) : null;
        return new SmokeAgentResponse(result.Error?.CodeName ?? "ok", result.Error?.Message, envelope, result.Png, result.Revision);
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
