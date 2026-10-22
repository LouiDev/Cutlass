namespace Cutlass.Agent.Lock;

/// <summary>Why a lock ended (AGENT_INTEGRATION 3.5). Wire spelling: <c>expired</c>, <c>released</c>, <c>revoked_by_user</c>.</summary>
public enum LockLostReason
{
    /// <summary>The lease ran out.</summary>
    Expired,

    /// <summary>The agent released it.</summary>
    Released,

    /// <summary>The user took over.</summary>
    RevokedByUser,
}
