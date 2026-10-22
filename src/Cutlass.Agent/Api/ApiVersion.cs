namespace Cutlass.Agent.Api;

/// <summary>The agent API version (AGENT_INTEGRATION 4.2).</summary>
public static class ApiVersion
{
    /// <summary>The major version; paths start with <c>/v1</c>. Breaking changes go to <c>/v2</c>.</summary>
    public const int Current = 1;

    /// <summary>
    /// The additive revision reported by <c>/v1/capabilities</c>; raised by every additive change. 2 added the discovery
    /// routes and MCP names in <c>/v1/call</c> (AGENT_INTEGRATION 9.2).
    /// </summary>
    public const int Revision = 2;

    /// <summary>The route prefix of this version.</summary>
    public const string Prefix = "/v1";

    /// <summary>The header that carries the lock id.</summary>
    public const string LockHeader = "X-Cutlass-Lock";

    /// <summary>The optional header that names the calling agent.</summary>
    public const string AgentHeader = "X-Cutlass-Agent";

    /// <summary>The header that carries a contact sheet layout (JSON) next to its PNG.</summary>
    public const string LayoutHeader = "X-Cutlass-Layout";
}
