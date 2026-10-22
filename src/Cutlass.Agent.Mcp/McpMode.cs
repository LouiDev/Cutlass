namespace Cutlass.Agent.Mcp;

/// <summary>How the exe runs (docs/AGENT_INTEGRATION.md 5.1).</summary>
public enum McpMode
{
    /// <summary>MCP over stdio, proxying to the running app's HTTP API (<see cref="Backend.HttpBackend"/>).</summary>
    Proxy,

    /// <summary>MCP over stdio with an in-process headless host (<see cref="Backend.InProcessBackend"/>).</summary>
    Headless,

    /// <summary>No MCP: the headless host plus the HTTP API for scripts.</summary>
    Serve,

    /// <summary>Prints one registration block per agent client profile (docs/AGENT_INTEGRATION.md 9.4), then exits.</summary>
    PrintRegistration,

    /// <summary>Prints the usage text, then exits.</summary>
    Help,
}
