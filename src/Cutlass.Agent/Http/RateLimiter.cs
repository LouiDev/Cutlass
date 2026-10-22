namespace Cutlass.Agent.Http;

/// <summary>A token bucket (AGENT_INTEGRATION 4.6): 30 requests per second with a burst of 60. Thread-safe.</summary>
public sealed class RateLimiter
{
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private double _tokens;
    private long _lastTicks;

    /// <summary>Creates a full bucket.</summary>
    /// <param name="perSecond">The refill rate.</param>
    /// <param name="burst">The capacity.</param>
    /// <param name="time">The clock, or null for the system clock.</param>
    public RateLimiter(double perSecond = 30, int burst = 60, TimeProvider? time = null)
    {
        PerSecond = perSecond;
        Burst = burst;
        _time = time ?? TimeProvider.System;
        _tokens = burst;
        _lastTicks = _time.GetTimestamp();
    }

    /// <summary>The refill rate.</summary>
    public double PerSecond { get; }

    /// <summary>The capacity.</summary>
    public int Burst { get; }

    /// <summary>Takes one token.</summary>
    /// <param name="retryAfterMs">When refused: how long until a token is available.</param>
    /// <returns>True when the request may proceed.</returns>
    public bool TryTake(out int retryAfterMs)
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            var elapsed = _time.GetElapsedTime(_lastTicks, now).TotalSeconds;
            _lastTicks = now;
            _tokens = Math.Min(Burst, _tokens + (elapsed * PerSecond));
            if (_tokens >= 1)
            {
                _tokens -= 1;
                retryAfterMs = 0;
                return true;
            }

            retryAfterMs = (int)Math.Ceiling((1 - _tokens) / PerSecond * 1000);
            return false;
        }
    }
}
