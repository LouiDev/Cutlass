namespace Cutlass.App.Services.Agent.Clients.Profiles;

/// <summary>Which tail the briefing gets for a target (AGENT_INTEGRATION 9.4).</summary>
public enum AgentClientBriefingKind
{
    /// <summary>No client known: the MCP tool names plus a pointer to the HTTP discovery routes.</summary>
    Generic,

    /// <summary>An MCP client: the tool names and that client's registration as the fallback.</summary>
    Mcp,

    /// <summary>A tool-calling harness over HTTP: base URL, tools URL, the <c>/v1/call</c> contract and the example script.</summary>
    Harness,

    /// <summary>Plain HTTP: the <c>curl</c> examples.</summary>
    Http,
}
