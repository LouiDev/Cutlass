namespace Cutlass.App.Services.Agent;

/// <summary>
/// What the Settings > Agents page (AGENT_INTEGRATION 6.1) needs besides the settings themselves: the server status
/// line, the token (read, regenerate) and the paths the MCP setup text names. <see cref="AgentServerController"/>
/// implements it; tests use a fake.
/// </summary>
public interface IAgentSettingsHost
{
    /// <summary>Raised on the UI thread when <see cref="Status"/> or <see cref="StatusText"/> changed.</summary>
    event EventHandler? StatusChanged;

    /// <summary>The server state for the status line.</summary>
    AgentServerStatus Status { get; }

    /// <summary>
    /// The status line: "Listening on http://localhost:47390/", "Off", "Another Cutlass window serves agents",
    /// "Port 47390 is in use" or another error.
    /// </summary>
    string StatusText { get; }

    /// <summary>True when <c>--agent-server</c> turned connections on for this session (the switch shows on).</summary>
    bool IsForcedOn { get; }

    /// <summary>The path of <c>Cutlass.Agent.Mcp</c> (next to the app when published, else the development build).</summary>
    string McpExecutablePath { get; }

    /// <summary>The agent docs (<c>AGENT_INTEGRATION.md</c>) beside the exe, or in the source tree in development.</summary>
    string AgentDocsPath { get; }

    /// <summary>Reads the bearer token off the UI thread, creating it when missing.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The token.</returns>
    Task<string> ReadTokenAsync(CancellationToken cancellationToken);

    /// <summary>Replaces the token off the UI thread ("Regenerate token"); connected agents must reconnect.</summary>
    /// <param name="cancellationToken">Cancels before the write.</param>
    /// <returns>The new token.</returns>
    Task<string> RegenerateTokenAsync(CancellationToken cancellationToken);
}
