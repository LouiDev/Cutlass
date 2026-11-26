namespace Cutlass.App.Services.Agent.Clients.Profiles;

/// <summary>
/// A command line of a client's own tool (AGENT_INTEGRATION 9.3), for example its "mcp add". The arguments may hold
/// placeholders; an argument whose placeholder is empty is dropped together with the option before it.
/// </summary>
/// <param name="Command">The tool's name on PATH.</param>
/// <param name="Args">The arguments, one per element.</param>
public sealed record AgentClientCommand(string Command, IReadOnlyList<string>? Args = null);
