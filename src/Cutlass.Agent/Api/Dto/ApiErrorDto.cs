namespace Cutlass.Agent.Api.Dto;

/// <summary>The <c>error</c> object of a failed response (AGENT_INTEGRATION 4.3).</summary>
public sealed class ApiErrorDto
{
    /// <summary>The snake_case code, for example <c>lock_required</c>.</summary>
    public string Code { get; set; } = "internal";

    /// <summary>What happened and what to do next.</summary>
    public string? Message { get; set; }

    /// <summary>Details such as <c>field</c>, <c>reason</c>, <c>holder</c> or <c>path</c>, or null.</summary>
    public Dictionary<string, string>? Details { get; set; }

    /// <summary>How long to wait before retrying, or null.</summary>
    public int? RetryAfterMs { get; set; }
}
