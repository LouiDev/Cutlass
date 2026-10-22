namespace Cutlass.App.Services.Agent.Clients;

/// <summary>
/// What the app remembers about client registrations (AGENT_INTEGRATION 9.4), in <c>&lt;data&gt;/agent/clients.state.json</c>:
/// the client registered last, whose briefing the plain "Copy briefing for your agent" renders.
/// </summary>
/// <param name="LastRegisteredClientId">The profile id registered last, or null.</param>
public sealed record AgentClientStateFile(string? LastRegisteredClientId);
