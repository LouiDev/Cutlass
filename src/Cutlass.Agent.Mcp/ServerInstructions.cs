using Cutlass.Agent.Api;

namespace Cutlass.Agent.Mcp;

/// <summary>
/// The server instructions every MCP client receives at initialisation (docs/AGENT_INTEGRATION.md 5.4, normative
/// text). The <c>cutlass_workflow</c> prompt returns the same text for clients that surface prompts instead. The
/// text lives in <see cref="AgentInstructions"/> so <c>GET /v1/instructions</c> renders from the same source (9.1).
/// </summary>
public static class ServerInstructions
{
    /// <summary>The normative instruction text, word for word; rendered by <see cref="AgentInstructions.Mcp"/>.</summary>
    public static string Text { get; } = AgentInstructions.Mcp();
}
