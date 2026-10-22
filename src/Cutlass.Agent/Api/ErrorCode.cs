namespace Cutlass.Agent.Api;

/// <summary>The error codes of the agent API (AGENT_INTEGRATION 4.3). The wire spelling is snake_case.</summary>
public enum ErrorCode
{
    /// <summary><c>unauthorized</c> (401): missing or wrong token.</summary>
    Unauthorized,

    /// <summary><c>forbidden</c> (403): path outside the allowed folders, non-loopback peer, Origin header, connections off, denied by the user.</summary>
    Forbidden,

    /// <summary><c>lock_required</c> (423): a mutating call without a valid lock.</summary>
    LockRequired,

    /// <summary><c>lock_lost</c> (423): the lock was revoked, expired or released.</summary>
    LockLost,

    /// <summary><c>paused</c> (503): paused by the user and not resumed in time.</summary>
    Paused,

    /// <summary><c>invalid_argument</c> (400): bad JSON, a missing or out-of-range field.</summary>
    InvalidArgument,

    /// <summary><c>not_found</c> (404): unknown route or id.</summary>
    NotFound,

    /// <summary><c>conflict</c> (409): revision mismatch, nested batch, undo outside agent steps, unsaved changes.</summary>
    Conflict,

    /// <summary><c>busy</c> (503): the user is busy, another agent holds the lock, too many heavy calls.</summary>
    Busy,

    /// <summary><c>unavailable</c> (503): ffmpeg missing or unusable, or no project open.</summary>
    Unavailable,

    /// <summary><c>payload_too_large</c> (413): body over 1 MiB or a section 4.6 limit.</summary>
    PayloadTooLarge,

    /// <summary><c>rate_limited</c> (429): the token bucket is empty.</summary>
    RateLimited,

    /// <summary><c>internal</c> (500): a bug; the stack goes to the log only.</summary>
    Internal,
}
