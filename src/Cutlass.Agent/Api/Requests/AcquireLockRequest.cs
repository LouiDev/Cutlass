namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/lock/acquire</c>.</summary>
public sealed class AcquireLockRequest
{
    /// <summary>The agent's name.</summary>
    public string? AgentName { get; set; }

    /// <summary>What it is about to do.</summary>
    public string? Reason { get; set; }

    /// <summary>How long to wait, at most 120000 ms.</summary>
    public int? WaitMs { get; set; }

    /// <summary>The lease asked for.</summary>
    public int? LeaseSeconds { get; set; }
}
