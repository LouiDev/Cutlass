namespace Cutlass.Agent.Api.Dto.Status;

/// <summary>The result of <c>POST /v1/lock/acquire</c> and <c>/v1/lock/renew</c>.</summary>
public sealed class LockGrantDto
{
    /// <summary>The lock id to send as <c>X-Cutlass-Lock</c> (acquire only).</summary>
    public string? LockId { get; set; }

    /// <summary>The phase.</summary>
    public string Phase { get; set; } = "held";

    /// <summary>The lease length in seconds.</summary>
    public int LeaseSeconds { get; set; }

    /// <summary>When the lease runs out (UTC).</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }
}
