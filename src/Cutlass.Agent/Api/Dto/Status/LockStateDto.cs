namespace Cutlass.Agent.Api.Dto.Status;

/// <summary>The result of <c>GET /v1/lock</c> and the <c>lock</c> part of the status.</summary>
public sealed class LockStateDto
{
    /// <summary><c>free</c>, <c>pending</c>, <c>held</c> or <c>paused</c>.</summary>
    public string Phase { get; set; } = "free";

    /// <summary>The holder's name, or null.</summary>
    public string? AgentName { get; set; }

    /// <summary>The holder's reason, or null.</summary>
    public string? Reason { get; set; }

    /// <summary>When the lock was granted (UTC), or null.</summary>
    public DateTimeOffset? Since { get; set; }

    /// <summary>When the lease runs out (UTC), or null.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>The lease length in seconds, or 0.</summary>
    public int LeaseSeconds { get; set; }

    /// <summary>True when the caller's lock id holds the lock.</summary>
    public bool Mine { get; set; }

    /// <summary>The first waiting agent, or null.</summary>
    public string? Pending { get; set; }

    /// <summary>The holder's last tool, or null.</summary>
    public string? LastTool { get; set; }

    /// <summary>The open batch label, or null.</summary>
    public string? Batch { get; set; }

    /// <summary>The holder: <c>user</c> or <c>agent:&lt;name&gt;</c>.</summary>
    public string Holder { get; set; } = "user";
}
