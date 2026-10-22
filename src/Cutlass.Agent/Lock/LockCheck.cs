namespace Cutlass.Agent.Lock;

/// <summary>The result of <see cref="AgentLock.Check"/>.</summary>
/// <param name="Status">Ok, paused, required or lost.</param>
/// <param name="Holder"><c>user</c> or <c>agent:&lt;name&gt;</c>: who holds the project now.</param>
/// <param name="LostReason">Why the caller's lock ended (with <see cref="LockCheckStatus.Lost"/>), else null.</param>
public sealed record LockCheck(LockCheckStatus Status, string Holder, LockLostReason? LostReason)
{
    /// <summary>True when the caller may write now.</summary>
    public bool IsOk => Status == LockCheckStatus.Ok;

    /// <summary>The wire spelling of a reason: <c>expired</c>, <c>released</c> or <c>revoked_by_user</c>.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The spelling.</returns>
    public static string WireReason(LockLostReason reason) => reason switch
    {
        LockLostReason.Expired => "expired",
        LockLostReason.Released => "released",
        _ => "revoked_by_user",
    };
}
