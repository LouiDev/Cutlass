namespace Cutlass.Agent.Lock;

/// <summary>What <see cref="AgentLock.Acquire"/> decided.</summary>
public enum AcquireStatus
{
    /// <summary>The lock was granted (or renewed for its holder).</summary>
    Granted,

    /// <summary>Not granted; <see cref="AcquireOutcome.BusyReason"/> says why.</summary>
    Busy,

    /// <summary>The user denied the request, or did not answer in time.</summary>
    Denied,

    /// <summary>Queued; <see cref="AcquireOutcome.Wait"/> completes with the final outcome.</summary>
    Waiting,
}
