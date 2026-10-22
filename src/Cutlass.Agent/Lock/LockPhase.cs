namespace Cutlass.Agent.Lock;

/// <summary>The phase of the agent lock (AGENT_INTEGRATION 3.1).</summary>
public enum LockPhase
{
    /// <summary>Only the user edits; mutating agent calls get <c>lock_required</c>.</summary>
    Free,

    /// <summary>An agent waits for a busy user or for the "ask first" answer.</summary>
    Pending,

    /// <summary>An agent holds the lease; the UI is read-only.</summary>
    Held,

    /// <summary>Held, but mutating calls wait and the lease does not run down.</summary>
    Paused,
}
