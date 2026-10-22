namespace Cutlass.App.Services.Agent.Clients;

/// <summary>
/// The registration state of the Cutlass MCP server in one client (AGENT_INTEGRATION 9.3). The five states of the
/// concept (not installed, not registered, current, stale, foreign) plus the two states of the check itself.
/// </summary>
public enum AgentClientState
{
    /// <summary>Not checked yet.</summary>
    Unknown,

    /// <summary>The check runs.</summary>
    Checking,

    /// <summary>The client was not found (no command on PATH, no folder, no configuration file).</summary>
    NotInstalled,

    /// <summary>The client is there, but has no <c>cutlass</c> entry.</summary>
    NotRegistered,

    /// <summary>The <c>cutlass</c> entry starts this app's MCP exe (or names this server's URL).</summary>
    Current,

    /// <summary>The <c>cutlass</c> entry points at another Cutlass MCP exe or URL (an old install or another build).</summary>
    Stale,

    /// <summary>A <c>cutlass</c> entry exists that the app did not write (it starts something else).</summary>
    Foreign,

    /// <summary>The check failed (the CLI failed or timed out, or the configuration file could not be read).</summary>
    Error,
}
