using Cutlass.Agent.Api;
using Cutlass.Agent.Lock;

namespace Cutlass.Agent.Tools;

/// <summary>The <c>lock_required</c>, <c>lock_lost</c> and <c>paused</c> errors (AGENT_INTEGRATION 3.4 and 3.5).</summary>
public static class LockErrors
{
    /// <summary>The error for a failed lock check.</summary>
    /// <param name="check">The check.</param>
    /// <param name="state">The lock state (for the holder's name).</param>
    /// <returns>The exception.</returns>
    public static ApiException From(LockCheck check, AgentLockState state)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(state);
        if (check.Status == LockCheckStatus.Lost && check.LostReason is { } reason)
        {
            var message = reason switch
            {
                LockLostReason.RevokedByUser => "The user took over the project. Stop editing and report what you did; do not reacquire the lock unless your user asks you to.",
                LockLostReason.Expired => "Your lock expired because you were idle too long, and any open batch was committed. Stop editing and report what you did.",
                _ => "You released the lock. Call lock/acquire again before editing.",
            };
            return new ApiException(ErrorCode.LockLost, message, new Dictionary<string, string> { ["reason"] = LockCheck.WireReason(reason) });
        }

        var holder = check.Holder;
        var text = state.IsHeld
            ? $"Another agent ({state.AgentName}) holds the project. Wait for it to finish or tell your user."
            : "The user holds the project. Call lock/acquire first.";
        return new ApiException(ErrorCode.LockRequired, text, new Dictionary<string, string> { ["holder"] = holder });
    }

    /// <summary>The error for a mutating call that waited 25 s while paused.</summary>
    /// <param name="retryAfterMs">The suggested delay.</param>
    /// <returns>The exception.</returns>
    public static ApiException Paused(int retryAfterMs) =>
        new(ErrorCode.Paused, "The user paused you to review the project. Retry after the suggested delay; reads still work.", null, retryAfterMs);
}
