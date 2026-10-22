namespace Cutlass.Agent.Lock;

/// <summary>
/// An immutable snapshot of the agent lock (AGENT_INTEGRATION 3.1). Times are Unix milliseconds from the clock the
/// caller passes to <see cref="AgentLock"/>.
/// </summary>
/// <param name="Phase">The phase.</param>
/// <param name="AgentName">The holder's name (Held, Paused), else null.</param>
/// <param name="Reason">The holder's reason, else null.</param>
/// <param name="SinceMs">When the lock was granted, else null.</param>
/// <param name="LeaseExpiresAtMs">When the lease runs out (Held), the frozen expiry while paused, else null.</param>
/// <param name="LeaseSeconds">The lease length, else 0.</param>
/// <param name="LastTool">The holder's last tool, else null.</param>
/// <param name="LastToolAtMs">When the last tool ran, else null.</param>
/// <param name="BatchLabel">The open batch label (without the "Agent: " prefix), else null.</param>
/// <param name="PendingAgent">The first waiting agent's name, else null.</param>
/// <param name="PendingReason">The first waiting agent's reason, else null.</param>
/// <param name="AskingUser">True while the "ask first" prompt waits for Allow or Deny.</param>
/// <param name="PausedRemainingMs">The lease left when paused, else null.</param>
/// <param name="EndedReason">How the last lock ended (for the banner's 3 s outcome), else null.</param>
/// <param name="EndedAgent">The agent of the last lock that ended, else null.</param>
public sealed record AgentLockState(
    LockPhase Phase,
    string? AgentName,
    string? Reason,
    long? SinceMs,
    long? LeaseExpiresAtMs,
    int LeaseSeconds,
    string? LastTool,
    long? LastToolAtMs,
    string? BatchLabel,
    string? PendingAgent,
    string? PendingReason,
    bool AskingUser,
    long? PausedRemainingMs,
    LockLostReason? EndedReason,
    string? EndedAgent)
{
    /// <summary>The free state.</summary>
    public static AgentLockState Free { get; } = new(LockPhase.Free, null, null, null, null, 0, null, null, null, null, null, false, null, null, null);

    /// <summary>True in Held and Paused: an agent holds the project and the user may not edit.</summary>
    public bool IsHeld => Phase is LockPhase.Held or LockPhase.Paused;

    /// <summary><c>user</c> or <c>agent:&lt;name&gt;</c>.</summary>
    public string Holder => IsHeld ? "agent:" + AgentName : "user";
}
