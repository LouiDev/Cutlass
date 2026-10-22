namespace Cutlass.Agent.Lock;

/// <summary>The timing rules of the agent lock (AGENT_INTEGRATION 3.2 to 3.5).</summary>
public sealed record LeaseOptions
{
    /// <summary>The defaults of the spec.</summary>
    public static LeaseOptions Default { get; } = new();

    /// <summary>How long a mutating call waits while paused before it fails with <c>paused</c> (25 s).</summary>
    public int PausedWaitMs { get; init; } = 25_000;

    /// <summary>The least lease left after Resume (30 s).</summary>
    public int ResumeMinimumMs { get; init; } = 30_000;

    /// <summary>How long a revoked, expired or released id is remembered (30 minutes).</summary>
    public long RevokedMemoryMs { get; init; } = 30L * 60 * 1000;

    /// <summary>How many acquisitions may wait at once (4).</summary>
    public int MaxWaiters { get; init; } = 4;

    /// <summary>The longest <c>waitMs</c> of an acquisition (120 s).</summary>
    public int MaxWaitMs { get; init; } = 120_000;

    /// <summary>How long the "ask first" prompt waits for an answer when the request waits less (60 s).</summary>
    public int AskTimeoutMs { get; init; } = 60_000;

    /// <summary>The shortest lease in seconds (15; tests lower it to watch a lease expire in real time).</summary>
    public int MinLeaseSeconds { get; init; } = Hosting.AgentPolicy.MinLeaseSeconds;

    /// <summary>The retry delay suggested with <c>busy</c> and <c>paused</c> (2 s).</summary>
    public int RetryAfterMs { get; init; } = 2_000;
}
