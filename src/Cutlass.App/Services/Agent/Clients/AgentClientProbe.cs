namespace Cutlass.App.Services.Agent.Clients;

/// <summary>
/// What a check of one client found (AGENT_INTEGRATION 9.3): whether it is installed, where its configuration lives,
/// the registration state and the details for the tooltip.
/// </summary>
/// <param name="Installed">True when the client was detected.</param>
/// <param name="ConfigPath">The configuration file (config file profiles), or null.</param>
/// <param name="State">The registration state.</param>
/// <param name="Detail">The registered command, the error, or empty.</param>
/// <param name="Scope">The registration scope a CLI reported (for example <c>user</c>), or null.</param>
public sealed record AgentClientProbe(bool Installed, string? ConfigPath, AgentClientState State, string Detail = "", string? Scope = null)
{
    /// <summary>The probe before the first check.</summary>
    public static AgentClientProbe NotChecked { get; } = new(false, null, AgentClientState.Unknown);

    /// <summary>The probe while the check runs.</summary>
    public static AgentClientProbe Checking { get; } = new(false, null, AgentClientState.Checking);

    /// <summary>True when a <c>cutlass</c> entry exists (this exe, another one, or someone else's).</summary>
    public bool Exists => State is AgentClientState.Current or AgentClientState.Stale or AgentClientState.Foreign;

    /// <summary>True when the client is there but cannot reach this app's MCP exe yet (not registered or stale).</summary>
    public bool IsMissing => Installed && State is AgentClientState.NotRegistered or AgentClientState.Stale;

    /// <summary>
    /// The state in words: "not installed", "not registered", "current" (with the scope), "stale: another exe",
    /// "foreign entry", "checking...", "could not check" or "not checked".
    /// </summary>
    public string Text => State switch
    {
        AgentClientState.NotInstalled => "not installed",
        AgentClientState.NotRegistered => "not registered",
        AgentClientState.Current => Scope is { Length: > 0 } scope ? "current (" + scope + " scope)" : "current",
        AgentClientState.Stale => "stale: another exe",
        AgentClientState.Foreign => "foreign entry",
        AgentClientState.Checking => "checking...",
        AgentClientState.Error => "could not check",
        _ => "not checked",
    };
}
