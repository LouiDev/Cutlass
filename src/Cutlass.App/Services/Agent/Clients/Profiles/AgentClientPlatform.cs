namespace Cutlass.App.Services.Agent.Clients.Profiles;

/// <summary>The platform a client profile resolves its paths for (AGENT_INTEGRATION 9.3).</summary>
public enum AgentClientPlatform
{
    /// <summary>Windows: <c>{appdata}</c> is the roaming application data folder.</summary>
    Windows,

    /// <summary>macOS.</summary>
    MacOS,

    /// <summary>Linux and other Unix systems.</summary>
    Linux,
}
