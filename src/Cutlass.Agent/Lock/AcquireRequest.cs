namespace Cutlass.Agent.Lock;

/// <summary>A lock acquisition (AGENT_INTEGRATION 3.2).</summary>
/// <param name="AgentName">The agent's name, shown to the user.</param>
/// <param name="Reason">Why it wants the project, shown to the user.</param>
/// <param name="WaitMs">How long to wait for a busy user or another agent (0: fail at once); capped at 120 s.</param>
/// <param name="LeaseSeconds">The lease asked for, or null for the default; clamped to 15 and the maximum.</param>
/// <param name="CurrentLockId">The caller's <c>X-Cutlass-Lock</c>, or null; when it holds the lock, acquire renews it.</param>
public sealed record AcquireRequest(string AgentName, string Reason, int WaitMs = 0, int? LeaseSeconds = null, string? CurrentLockId = null);
