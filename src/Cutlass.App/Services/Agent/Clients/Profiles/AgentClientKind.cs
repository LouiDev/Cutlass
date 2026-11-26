namespace Cutlass.App.Services.Agent.Clients.Profiles;

/// <summary>How a client profile registers Cutlass (AGENT_INTEGRATION 9.3).</summary>
public enum AgentClientKind
{
    /// <summary>The client's own command line tool adds and removes the server (run through <see cref="ICliRunner"/>).</summary>
    Cli,

    /// <summary>The entry is merged into the client's JSON or TOML configuration file.</summary>
    ConfigFile,

    /// <summary>Nothing is registered; the row only copies its setup text (harnesses and plain HTTP).</summary>
    Text,
}
