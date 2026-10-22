namespace Cutlass.App.Services.Agent.Clients;

/// <summary>
/// How a profile finds out whether its client is installed (AGENT_INTEGRATION 9.3): a command on PATH, or a file or
/// folder that exists. Either one is enough; a configuration file that exists counts as well.
/// </summary>
/// <param name="Command">A command looked up on PATH, or null.</param>
/// <param name="File">A file or folder that exists when the client is installed, or null.</param>
public sealed record AgentClientDetect(string? Command = null, AgentClientPath? File = null);
