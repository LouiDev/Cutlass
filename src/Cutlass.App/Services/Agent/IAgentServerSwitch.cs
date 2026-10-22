namespace Cutlass.App.Services.Agent;

/// <summary>
/// Agent > Start/Stop MCP server (AGENT_INTEGRATION 6.6): switches the agent server for this session without changing
/// the saved "Allow agent connections" (unless "Keep it on in future sessions" was ticked), and names what the
/// briefing needs. <see cref="AgentServerController"/> implements it; tests use a fake.
/// </summary>
public interface IAgentServerSwitch
{
    /// <summary>True when the server is meant to run now (the setting, <c>--agent-server</c> or Start for this session).</summary>
    bool IsSwitchedOn { get; }

    /// <summary>The base URL while listening, else null.</summary>
    string? BaseUrl { get; }

    /// <summary>The URL the server uses or would use, for example <c>http://localhost:47390/</c> (the briefing).</summary>
    string ExpectedBaseUrl { get; }

    /// <summary>The token file (<c>&lt;data&gt;/agent/token</c>).</summary>
    string TokenFile { get; }

    /// <summary>Turns the server on for this session (UI thread), optionally saving "Allow agent connections".</summary>
    /// <param name="keepOn">True to also save the setting on ("Keep it on in future sessions").</param>
    /// <returns>A task that completes when the server listens or failed to start.</returns>
    Task StartForSessionAsync(bool keepOn);

    /// <summary>
    /// Turns the server off for this session (UI thread); an agent that holds the project loses it (the caller asks
    /// first). The saved setting is unchanged.
    /// </summary>
    /// <returns>A task that completes when the server stopped.</returns>
    Task StopForSessionAsync();
}
