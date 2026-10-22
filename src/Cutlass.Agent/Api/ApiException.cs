namespace Cutlass.Agent.Api;

/// <summary>An <see cref="ApiError"/> thrown by tool code and by <see cref="Http.AgentClient"/>.</summary>
public sealed class ApiException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="error">The error.</param>
    public ApiException(ApiError error)
        : base((error ?? throw new ArgumentNullException(nameof(error))).Message)
    {
        Error = error;
    }

    /// <summary>Creates the exception from parts.</summary>
    /// <param name="code">The code.</param>
    /// <param name="message">The message.</param>
    /// <param name="details">Details, or null.</param>
    /// <param name="retryAfterMs">The retry delay, or null.</param>
    public ApiException(ErrorCode code, string message, IReadOnlyDictionary<string, string>? details = null, int? retryAfterMs = null)
        : this(new ApiError(code, message, details, retryAfterMs))
    {
    }

    /// <summary>The error.</summary>
    public ApiError Error { get; }

    /// <summary>The code of <see cref="Error"/>.</summary>
    public ErrorCode Code => Error.Code;

    /// <summary>An <c>invalid_argument</c> error naming the field.</summary>
    /// <param name="field">The field, for example <c>frame</c>.</param>
    /// <param name="message">The message.</param>
    /// <returns>The exception.</returns>
    public static ApiException InvalidArgument(string field, string message) =>
        new(ErrorCode.InvalidArgument, message, new Dictionary<string, string> { ["field"] = field });

    /// <summary>A <c>not_found</c> error for an unknown id.</summary>
    /// <param name="kind">What was looked up, for example <c>clip</c>.</param>
    /// <param name="id">The id.</param>
    /// <returns>The exception.</returns>
    public static ApiException NotFound(string kind, string id) =>
        new(
            ErrorCode.NotFound,
            $"There is no {kind} with id '{id}'. Read the project (get_project or describe_project) for the current ids.",
            new Dictionary<string, string> { ["kind"] = kind, ["id"] = id });

    /// <summary>A <c>conflict</c> error with a reason.</summary>
    /// <param name="reason">The reason, for example <c>not_agent_step</c>.</param>
    /// <param name="message">The message.</param>
    /// <returns>The exception.</returns>
    public static ApiException Conflict(string reason, string message) =>
        new(ErrorCode.Conflict, message, new Dictionary<string, string> { ["reason"] = reason });
}
