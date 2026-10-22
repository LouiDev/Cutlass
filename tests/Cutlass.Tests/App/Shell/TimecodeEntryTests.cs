using Cutlass.App.Services;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.App.Shell;

/// <summary>Click-to-type timecode entry (DESIGN 4.3): absolute timecodes and +N / -N offsets, culture-proof.</summary>
[UseCulture("de-DE")]
public sealed class TimecodeEntryTests
{
    [Theory]
    [InlineData("00:00:12:04", 25, 304)]
    [InlineData("00:01:00:00", 25, 1500)]
    [InlineData("12:04", 25, 304)]
    [InlineData("150", 25, 150)]
    [InlineData(" 00:00:01:00 ", 30, 30)]
    [InlineData("00;00;01;00", 30, 30)]
    [InlineData("00.00.01.00", 30, 30)]
    [InlineData("01:00:00:00", 30, 108000)]
    public void AbsoluteTimecodesSeek(string text, int fps, long expected)
    {
        Assert.True(TimecodeEntry.TryResolve(text, 77, Rate(fps), out var frame));
        Assert.Equal(expected, frame);
    }

    [Theory]
    [InlineData("+15", 100, 115)]
    [InlineData("-15", 100, 85)]
    [InlineData("+ 15", 100, 115)]
    [InlineData("-00:00:01:00", 100, 75)]
    [InlineData("+00:00:02:05", 0, 55)]
    [InlineData("+1:00", 10, 35)]
    [InlineData("-500", 100, 0)]
    public void OffsetsMoveFromTheCurrentFrame(string text, long current, long expected)
    {
        Assert.True(TimecodeEntry.TryResolve(text, current, FrameRate.Fps25, out var frame));
        Assert.Equal(expected, frame);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("+-5")]
    [InlineData("--5")]
    [InlineData("1,5")]
    [InlineData("00:00:60:00")]
    [InlineData("00:00:01:25")]
    [InlineData("12a")]
    [InlineData("1:2:3:4:5")]
    public void InvalidTextLeavesThePlayhead(string text)
    {
        Assert.False(TimecodeEntry.TryResolve(text, 42, FrameRate.Fps25, out var frame));
        Assert.Equal(42, frame);
    }

    [Fact]
    public void DropFrameRatesCountTheirTimecodeBase()
    {
        // 29.97 counts 30 frames per timecode second, 23.976 counts 24 (non-drop, DESIGN 5.2).
        Assert.True(TimecodeEntry.TryResolve("00:00:01:00", 0, FrameRate.Fps2997, out var ntsc));
        Assert.Equal(30, ntsc);
        Assert.True(TimecodeEntry.TryResolve("+00:00:01:00", 10, FrameRate.Fps23976, out var film));
        Assert.Equal(34, film);
    }

    [Fact]
    public void TheResultRoundTripsThroughTheReadout()
    {
        var rate = FrameRate.Fps2997;
        var text = Timecode.Format(12345, rate);
        Assert.True(TimecodeEntry.TryResolve(text, 0, rate, out var frame));
        Assert.Equal(12345, frame);
    }

    private static FrameRate Rate(int fps) => fps switch
    {
        25 => FrameRate.Fps25,
        30 => FrameRate.Fps30,
        _ => throw new ArgumentOutOfRangeException(nameof(fps)),
    };
}
