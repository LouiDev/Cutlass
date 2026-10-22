namespace Cutlass.Agent.Lock;

/// <summary>The result of <see cref="AgentLock.Acquire"/>.</summary>
/// <param name="Status">Granted, busy, denied or waiting.</param>
/// <param name="LockId">The lock id when granted, else null.</param>
/// <param name="State">The lock state right after the decision.</param>
/// <param name="BusyReason"><c>gesture</c>, <c>modal</c>, <c>export</c> or <c>held_by_agent</c> when busy, else null.</param>
/// <param name="Wait">When waiting: completes (on the UI thread) with the granted, busy or denied outcome.</param>
public sealed record AcquireOutcome(AcquireStatus Status, string? LockId, AgentLockState State, string? BusyReason, Task<AcquireOutcome>? Wait)
{
    /// <summary>True when the lock was renewed for its holder rather than newly granted.</summary>
    public bool Renewed { get; init; }
}
