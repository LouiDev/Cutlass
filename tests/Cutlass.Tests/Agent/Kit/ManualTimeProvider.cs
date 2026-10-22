namespace Cutlass.Tests.Agent.Kit;

/// <summary>A clock the test moves by hand (UTC time and the timestamp move together).</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;

    public ManualTimeProvider(DateTimeOffset? start = null)
    {
        Now = start ?? new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset Now { get; private set; }

    public long NowMs => Now.ToUnixTimeMilliseconds();

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => Now;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by)
    {
        Now += by;
        _ticks += by.Ticks;
    }

    public void AdvanceMs(long ms) => Advance(TimeSpan.FromMilliseconds(ms));
}
