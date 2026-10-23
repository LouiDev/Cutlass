using System.Text.Json;

namespace Cutlass.Agent.Tools.Dispatch;

/// <summary>One tool call as the HTTP host and the MCP in-process backend hand it to <see cref="ToolDispatcher"/>.</summary>
/// <param name="Tool">The catalog name, for example <c>edit.split</c>.</param>
/// <param name="Args">The arguments as a JSON object (route and query values merged in).</param>
/// <param name="LockId">The caller's <c>X-Cutlass-Lock</c>, or null.</param>
/// <param name="AgentName">The caller's <c>X-Cutlass-Agent</c>, or null.</param>
public sealed record AgentCall(string Tool, JsonElement Args, string? LockId = null, string? AgentName = null)
{
    /// <summary>An empty JSON object.</summary>
    public static JsonElement EmptyArgs { get; } = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>Creates a call with no arguments.</summary>
    /// <param name="tool">The catalog name.</param>
    /// <param name="lockId">The lock id, or null.</param>
    /// <returns>The call.</returns>
    public static AgentCall Of(string tool, string? lockId = null) => new(tool, EmptyArgs, lockId);

    /// <summary>Creates a call from JSON argument text.</summary>
    /// <param name="tool">The catalog name.</param>
    /// <param name="json">The arguments as JSON text.</param>
    /// <param name="lockId">The lock id, or null.</param>
    /// <returns>The call.</returns>
    public static AgentCall Of(string tool, string json, string? lockId)
    {
        using var doc = JsonDocument.Parse(json);
        return new AgentCall(tool, doc.RootElement.Clone(), lockId);
    }
}
