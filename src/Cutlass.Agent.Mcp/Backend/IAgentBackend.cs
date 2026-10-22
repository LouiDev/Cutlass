using System.Text.Json;
using Cutlass.Agent.Api;

namespace Cutlass.Agent.Mcp.Backend;

/// <summary>
/// Where the MCP tools send their calls (docs/AGENT_INTEGRATION.md 5.1): <see cref="HttpBackend"/> proxies to the
/// running app over its HTTP API, <see cref="InProcessBackend"/> calls a <c>ToolDispatcher</c> over an in-process
/// headless host. Both keep the lock id of this MCP session and send it themselves, so the model never handles it.
/// Disposing releases a lock the session still holds (the agent went away: stdin closed).
/// </summary>
public interface IAgentBackend : IAsyncDisposable
{
    /// <summary><c>proxy</c> or <c>headless</c>, for logs.</summary>
    string Kind { get; }

    /// <summary>The lock id this session holds, or null.</summary>
    string? LockId { get; }

    /// <summary>Runs one tool call. Never throws for API failures; they come back as error results.</summary>
    /// <param name="tool">The catalog name, for example <c>edit.split</c>.</param>
    /// <param name="args">The arguments as a JSON object with the HTTP field names.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The result: JSON, PNG or text, or an error.</returns>
    Task<ApiResult> CallAsync(string tool, JsonElement args, CancellationToken ct);
}
