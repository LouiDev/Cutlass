namespace Cutlass.Agent.Lock;

/// <summary>What <see cref="AgentLock.Check"/> found for a mutating call.</summary>
public enum LockCheckStatus
{
    /// <summary>The caller holds the lock and may write now.</summary>
    Ok,

    /// <summary>The caller holds the lock but it is paused: wait for <see cref="AgentLock.WhenResumed"/>.</summary>
    Paused,

    /// <summary>No valid lock: <c>lock_required</c>.</summary>
    Required,

    /// <summary>The caller's lock ended: <c>lock_lost</c>.</summary>
    Lost,
}
