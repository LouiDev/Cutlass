namespace Cutlass.App.Services.Agent.Clients;

/// <summary>The outcome of Register, Update or Remove for one client (AGENT_INTEGRATION 9.3).</summary>
/// <param name="Succeeded">True when the command exited with 0 or the file was written.</param>
/// <param name="Output">What ran or was written, and the CLI's output, for the dialog.</param>
/// <param name="Probe">The state checked afterwards.</param>
public sealed record AgentClientRegisterResult(bool Succeeded, string Output, AgentClientProbe Probe);
