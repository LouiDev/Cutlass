using System.Numerics;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Tests.Time;

public sealed class TimeMathTests
{
    public static TheoryData<FrameRate> AllRates
    {
        get
        {
            var data = new TheoryData<FrameRate>();
            foreach (var rate in FrameRate.Supported)
            {
                data.Add(rate);
            }

            return data;
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1601)]
    [InlineData(2, 3203)]
    [InlineData(5, 8008)]
    [InlineData(30000, 48_048_000)]
    [InlineData(-1, -1602)]
    public void SamplesAtFloorsAt2997(long frame, long expected)
    {
        // 48000 * 1001 / 30000 = 1601.6 samples per frame.
        Assert.Equal(expected, TimeMath.SamplesAt(frame, FrameRate.Fps2997));
    }

    [Theory]
    [MemberData(nameof(AllRates))]
    public void SamplesAtMatchesBigIntegerFloorForEveryFrame(FrameRate rate)
    {
        for (long frame = 0; frame < 20_000; frame++)
        {
            Assert.Equal(ReferenceSamplesAt(frame, rate), TimeMath.SamplesAt(frame, rate));
        }
    }

    [Theory]
    [MemberData(nameof(AllRates))]
    public void FrameAtSampleInvertsSamplesAt(FrameRate rate)
    {
        for (long frame = -50; frame < 5_000; frame++)
        {
            var first = TimeMath.SamplesAt(frame, rate);
            var next = TimeMath.SamplesAt(frame + 1, rate);
            Assert.True(next > first);
            Assert.Equal(frame, TimeMath.FrameAtSample(first, rate));
            Assert.Equal(frame, TimeMath.FrameAtSample(next - 1, rate));
        }
    }

    [Fact]
    public void NoDriftOver24HoursAt2997()
    {
        var rate = FrameRate.Fps2997;

        // 24 h of 29.97 material: 24 * 3600 * 30000 / 1001 frames (floored).
        var frames = 24L * 3600 * 30000 / 1001;
        var samples = TimeMath.SamplesAt(frames, rate);

        Assert.Equal(ReferenceSamplesAt(frames, rate), samples);

        // Exactly 48,048,000 samples per 30,000 frames, at any offset.
        Assert.Equal(48_048_000, TimeMath.SamplesAt(frames + 30_000, rate) - samples);
        Assert.Equal(48_048_000L * 86, TimeMath.SamplesAt(30_000L * 86, rate));

        // Round trip near the end of the day: summing per-frame sample counts reproduces the absolute position.
        long sum = TimeMath.SamplesAt(frames - 1000, rate);
        for (var f = frames - 1000; f < frames; f++)
        {
            sum += TimeMath.SamplesAt(f + 1, rate) - TimeMath.SamplesAt(f, rate);
            Assert.Equal(f + 1, TimeMath.FrameAtSample(TimeMath.SamplesAt(f + 1, rate), rate));
        }

        Assert.Equal(samples, sum);
    }

    [Fact]
    public void SamplesAtIntegerRatesIsExactMultiplication()
    {
        Assert.Equal(1920L * 1000, TimeMath.SamplesAt(1000, FrameRate.Fps25));
        Assert.Equal(1600L * 1000, TimeMath.SamplesAt(1000, FrameRate.Fps30));
        Assert.Equal(2000L * 1000, TimeMath.SamplesAt(1000, FrameRate.Fps24));
        Assert.Equal(800L * 1000, TimeMath.SamplesAt(1000, FrameRate.Fps60));
    }

    [Theory]
    [InlineData(100, 0, 0)]
    [InlineData(100, 30, 30 * 23_543_520L)]
    [InlineData(50, 30, 15 * 23_543_520L)]
    [InlineData(200, 30, 60 * 23_543_520L)]
    [InlineData(25, 1, 5_885_880)]
    [InlineData(400, 7, 28 * 23_543_520L)]
    [InlineData(50, 1, 11_771_760)]
    [InlineData(100, -1, -23_543_520)]
    [InlineData(25, -1, -5_885_880)]
    public void SourceFlicksAtScalesBySpeed(int speed, long offsetFrames, long expectedOffset)
    {
        var clip = MakeClip(start: 100, sourceIn: 1_000_000, speed);

        var flicks = TimeMath.SourceFlicksAt(clip, 100 + offsetFrames, FrameRate.Fps2997);

        Assert.Equal(1_000_000 + expectedOffset, flicks);
    }

    [Fact]
    public void SourceFlicksAtFloorsFractionalFlicks()
    {
        // 23,543,520 * 33 / 100 = 7,769,361.6, floored; negative offsets floor towards negative infinity.
        var clip = MakeClip(start: 0, sourceIn: 0, speed: 33);

        Assert.Equal(7_769_361, TimeMath.SourceFlicksAt(clip, 1, FrameRate.Fps2997));
        Assert.Equal(-7_769_362, TimeMath.SourceFlicksAt(clip, -1, FrameRate.Fps2997));
    }

    [Theory]
    [InlineData(1, 3, 333_333)]
    [InlineData(-1, 3, -333_334)]
    [InlineData(1001, 30000, 33_366)]
    [InlineData(5, 1, 5_000_000)]
    [InlineData(0, 1, 0)]
    public void FloorMicrosFloors(long num, long den, long expected)
    {
        Assert.Equal(expected, TimeMath.FloorMicros(new Rational(num, den)));
    }

    [Fact]
    public void FlickConversionsAreExactForCommonTimeBases()
    {
        // One 29.97 frame in a 1/90000 time base is 3003 ticks.
        Assert.Equal(23_543_520, TimeMath.FlicksFromTicks(3003, new Rational(1, 90000)));
        Assert.Equal(23_543_520, TimeMath.FlicksFromTicks(1001, new Rational(1, 30000)));
        Assert.Equal(705_600_000, TimeMath.FlicksFromMicros(1_000_000));
        Assert.Equal(705, TimeMath.FlicksFromMicros(1));
        Assert.Equal(-706, TimeMath.FlicksFromMicros(-1));
        Assert.Equal(33_366, TimeMath.FloorMicrosFromFlicks(23_543_520));
        Assert.Equal(-1, TimeMath.FloorMicrosFromFlicks(-1));
        Assert.Equal(30 * 23_543_520L, TimeMath.FlicksAtFrame(30, FrameRate.Fps2997));
        Assert.Equal(new Rational(1001, 1000), TimeMath.SecondsAt(30, FrameRate.Fps2997));
    }

    [Theory]
    [InlineData(7, 2, 3)]
    [InlineData(-7, 2, -4)]
    [InlineData(7, -2, -4)]
    [InlineData(-7, -2, 3)]
    [InlineData(6, 3, 2)]
    [InlineData(-6, 3, -2)]
    [InlineData(0, 5, 0)]
    public void FloorDivRoundsTowardsNegativeInfinity(long a, long b, long expected)
    {
        Assert.Equal(expected, TimeMath.FloorDiv(a, b));
        Assert.Equal((Int128)expected, TimeMath.FloorDiv((Int128)a, (Int128)b));
    }

    private static long ReferenceSamplesAt(long frame, FrameRate rate)
    {
        var numerator = new BigInteger(frame) * TimeMath.SampleRate * rate.Value.Den;
        var q = BigInteger.DivRem(numerator, rate.Value.Num, out var rem);
        if (rem < 0)
        {
            q -= 1;
        }

        return (long)q;
    }

    private static MediaClip MakeClip(long start, long sourceIn, int speed) =>
        new(
            "c_00000001",
            start,
            Duration: 1000,
            Enabled: true,
            LinkId: null,
            MediaId: "m_00000001",
            StreamKind.Video,
            sourceIn,
            speed,
            VideoProps.Default,
            AudioProps.Default);
}
