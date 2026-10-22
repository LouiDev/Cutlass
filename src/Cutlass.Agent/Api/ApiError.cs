namespace Cutlass.Agent.Api;

/// <summary>
/// One API error (AGENT_INTEGRATION 4.3): a code, a message written for an LLM (what happened and what to do next),
/// optional details such as <c>field</c>, <c>reason</c>, <c>holder</c> or <c>path</c>, and an optional retry delay.
/// </summary>
/// <param name="Code">The code.</param>
/// <param name="Message">The message.</param>
/// <param name="Details">Details, or null.</param>
/// <param name="RetryAfterMs">How long to wait before retrying, or null.</param>
public sealed record ApiError(ErrorCode Code, string Message, IReadOnlyDictionary<string, string>? Details = null, int? RetryAfterMs = null)
{
    /// <summary>The wire spelling of <see cref="Code"/>, for example <c>lock_required</c>.</summary>
    public string CodeName => WireName(Code);

    /// <summary>The HTTP status of <see cref="Code"/>.</summary>
    public int HttpStatus => StatusOf(Code);

    /// <summary>A detail value, or null.</summary>
    /// <param name="key">The detail key, for example <c>reason</c>.</param>
    /// <returns>The value or null.</returns>
    public string? Detail(string key) => Details is not null && Details.TryGetValue(key, out var v) ? v : null;

    /// <summary>The wire spelling of a code.</summary>
    /// <param name="code">The code.</param>
    /// <returns>For example <c>lock_lost</c>.</returns>
    public static string WireName(ErrorCode code) => code switch
    {
        ErrorCode.Unauthorized => "unauthorized",
        ErrorCode.Forbidden => "forbidden",
        ErrorCode.LockRequired => "lock_required",
        ErrorCode.LockLost => "lock_lost",
        ErrorCode.Paused => "paused",
        ErrorCode.InvalidArgument => "invalid_argument",
        ErrorCode.NotFound => "not_found",
        ErrorCode.Conflict => "conflict",
        ErrorCode.Busy => "busy",
        ErrorCode.Unavailable => "unavailable",
        ErrorCode.PayloadTooLarge => "payload_too_large",
        ErrorCode.RateLimited => "rate_limited",
        _ => "internal",
    };

    /// <summary>The HTTP status of a code.</summary>
    /// <param name="code">The code.</param>
    /// <returns>For example 423.</returns>
    public static int StatusOf(ErrorCode code) => code switch
    {
        ErrorCode.Unauthorized => 401,
        ErrorCode.Forbidden => 403,
        ErrorCode.LockRequired or ErrorCode.LockLost => 423,
        ErrorCode.InvalidArgument => 400,
        ErrorCode.NotFound => 404,
        ErrorCode.Conflict => 409,
        ErrorCode.Paused or ErrorCode.Busy or ErrorCode.Unavailable => 503,
        ErrorCode.PayloadTooLarge => 413,
        ErrorCode.RateLimited => 429,
        _ => 500,
    };

    /// <summary>Parses a wire spelling.</summary>
    /// <param name="wire">For example <c>not_found</c>.</param>
    /// <param name="code">The code.</param>
    /// <returns>True when known.</returns>
    public static bool TryParseCode(string? wire, out ErrorCode code)
    {
        foreach (var candidate in Enum.GetValues<ErrorCode>())
        {
            if (string.Equals(WireName(candidate), wire, StringComparison.Ordinal))
            {
                code = candidate;
                return true;
            }
        }

        code = ErrorCode.Internal;
        return false;
    }
}
