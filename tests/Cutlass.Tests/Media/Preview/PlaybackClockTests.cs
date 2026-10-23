using System.Diagnostics;
using Cutlass.Core.Time;
using Cutlass.Media.Preview.Playback;

namespace Cutlass.Tests.Media.Preview;

/// <summary>
/// The playback clock of TECH_BRIEF 8.2: audio-master <c>FrameAtSample(SamplesAt(start) + played)</c> with at most
/// 20 ms of extrapolation, the Stopwatch clock <c>start + floor(elapsed * R * Speed)</c>, clamping at the end, and
/// monotonicity.
/// </summary>
public sealed class PlaybackClockTests
{
    private const long Freq = 10_000_000;

    [Theory]
    [InlineData(30, 1, 0, 0, 0)]
    [InlineData(30, 1, 0, 1599, 0)]
    [InlineData(30, 1, 0, 1600, 1)]
    [InlineData(30, 1, 100, 48000, 130)]
    [InlineData(30000, 1001, 0, 1600, 0)]
    [InlineData(30000, 1001, 0, 1601, 1)]
    [InlineData(25, 1, 10, 1920, 11)]
    public void TheAudioClockMapsPlayedSamplesToFrames(long num, long den, long start, long played, long expected)
    {
        var rate = new FrameRate(new Rational(num, den));
        Assert.Equal(expected, PlaybackClock.AudioFrame(rate, start, 1_000_000, played, 0, Freq));
    }

    [Fact]
    public void TheAudioClockAddsTheSamplesOfStartFrameSoNtscStartsStayExact()
    {
        // At 29.97 SamplesAt(start) is not a multiple of the per-frame samples; the clock must add, not multiply.
        var rate = FrameRate.Fps2997;
        for (long start = 0; start < 200; start += 7)
        {
            for (long frame = start; frame < start + 60; frame++)
            {
                var played = TimeMath.SamplesAt(frame, rate) - TimeMath.SamplesAt(start, rate);
                Assert.Equal(frame, PlaybackClock.AudioFrame(rate, start, 1_000_000, played, 0, Freq));
                Assert.Equal(frame - 1 < start ? start : frame - 1, PlaybackClock.AudioFrame(rate, start, 1_000_000, Math.Max(0, played - 1), 0, Freq));
            }
        }
    }

    [Fact]
    public void ExtrapolationIsCappedAt20Milliseconds()
    {
        var rate = FrameRate.Fps30;
        var tenMs = Freq / 100;

        Assert.Equal(480, PlaybackClock.AudioSample(rate, 0, 1000, 0, tenMs, Freq));
        Assert.Equal(960, PlaybackClock.AudioSample(rate, 0, 1000, 0, 2 * tenMs, Freq));
        Assert.Equal(960, PlaybackClock.AudioSample(rate, 0, 1000, 0, 50 * tenMs, Freq));
        Assert.Equal(960, PlaybackClock.AudioSample(rate, 0, 1000, 0, 500 * tenMs, Freq));
        Assert.Equal(0, PlaybackClock.AudioSample(rate, 0, 1000, 0, -tenMs, Freq));

        // 1590 played samples plus 20 ms (960 samples) crosses into frame 1 (1600 samples per frame).
        Assert.Equal(1, PlaybackClock.AudioFrame(rate, 0, 1000, 1590, 2 * tenMs, Freq));
    }

    [Fact]
    public void TheClockStopsAtTheEnd()
    {
        var rate = FrameRate.Fps30;
        Assert.Equal(90, PlaybackClock.AudioFrame(rate, 60, 90, 999_999, 0, Freq));
        Assert.Equal(TimeMath.SamplesAt(90, rate), PlaybackClock.AudioSample(rate, 60, 90, 999_999, 0, Freq));
        Assert.Equal(90, PlaybackClock.StopwatchFrame(rate, 60, 90, 4, 100 * Freq, Freq));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, (Freq / 30) + 1, 1)]
    [InlineData(1, (Freq / 30) - 1, 0)]
    [InlineData(2, Freq, 60)]
    [InlineData(4, Freq, 120)]
    [InlineData(4, Freq / 2, 60)]
    [InlineData(2, -Freq, 0)]
    public void TheStopwatchClockIsStartPlusFloorOfElapsedTimesRateTimesSpeed(int speed, long elapsed, long expected)
    {
        Assert.Equal(1000 + expected, PlaybackClock.StopwatchFrame(FrameRate.Fps30, 1000, 1_000_000, speed, elapsed, Freq));
    }

    [Fact]
    public void TheStopwatchClockIsExactAtNtscRates()
    {
        // 30000/1001: 1001 s of playback is exactly 30000 frames, at 2x 60000.
        Assert.Equal(30000, PlaybackClock.StopwatchFrame(FrameRate.Fps2997, 0, long.MaxValue, 1, 1001 * Freq, Freq));
        Assert.Equal(60000, PlaybackClock.StopwatchFrame(FrameRate.Fps2997, 0, long.MaxValue, 2, 1001 * Freq, Freq));
        Assert.Equal(29999, PlaybackClock.StopwatchFrame(FrameRate.Fps2997, 0, long.MaxValue, 1, (1001 * Freq) - 1, Freq));
    }

    [Fact]
    public void AStoppedClockReportsItsStartAndAPublishStartsIt()
    {
        var clock = new PlaybackClock(FrameRate.Fps30, 300, 600, 1, audioMaster: true);
        Assert.False(clock.IsRunning);
        Assert.Equal(300, clock.Frame);
        Assert.Equal(TimeMath.SamplesAt(300, FrameRate.Fps30), clock.SampleAt(Stopwatch.GetTimestamp()));

        var now = Stopwatch.GetTimestamp();
        clock.PublishSamples(48000, now);
        Assert.True(clock.IsRunning);
        Assert.Equal(330, clock.FrameAt(now));
    }

    [Fact]
    public void TheClockNeverStepsBackAfterAnOvershootingExtrapolation()
    {
        var clock = new PlaybackClock(FrameRate.Fps30, 0, 1000, 1, audioMaster: true);
        var t0 = Stopwatch.GetTimestamp();
        clock.PublishSamples(1590, t0);

        // 20 ms later the extrapolation reaches frame 1 ...
        var later = t0 + (Stopwatch.Frequency / 50);
        Assert.Equal(1, clock.FrameAt(later));

        // ... then a stalled output (an underrun) publishes the same position again: the clock holds at 1.
        clock.PublishSamples(1590, later);
        Assert.Equal(1, clock.FrameAt(later));
    }

    [Fact]
    public void TheStopwatchModeIgnoresAudioPublishes()
    {
        var clock = new PlaybackClock(FrameRate.Fps30, 10, 1000, 2, audioMaster: false);
        var t0 = Stopwatch.GetTimestamp();
        clock.StartStopwatch(t0);

        Assert.Equal(10, clock.FrameAt(t0));
        Assert.Equal(70, clock.FrameAt(t0 + Stopwatch.Frequency));
        Assert.False(clock.AudioMaster);
        Assert.Equal(2, clock.Speed);
    }
}
