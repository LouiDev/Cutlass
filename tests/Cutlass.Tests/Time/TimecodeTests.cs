using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Time;

[UseCulture("de-DE")]
public sealed class TimecodeTests
{
    [Theory]
    [InlineData(0, "00:00:00:00")]
    [InlineData(24, "00:00:00:24")]
    [InlineData(25, "00:00:01:00")]
    [InlineData((25 * 3600) + (25 * 61) + 3, "01:01:01:03")]
    [InlineData(25 * 3600 * 100, "100:00:00:00")]
    [InlineData(-25, "-00:00:01:00")]
    public void FormatsAt25(long frame, string expected)
    {
        Assert.Equal(expected, Timecode.Format(frame, FrameRate.Fps25));
    }

    [Theory]
    [InlineData(29, "00:00:00:29")]
    [InlineData(30, "00:00:01:00")]
    [InlineData(1800, "00:01:00:00")]
    [InlineData(108_000, "01:00:00:00")]
    public void FormatsNonDropAt2997WithBase30(long frame, string expected)
    {
        Assert.Equal(expected, Timecode.Format(frame, FrameRate.Fps2997));
    }

    [Theory]
    [InlineData(23, "00:00:00:23")]
    [InlineData(24, "00:00:01:00")]
    public void FormatsAt23976WithBase24(long frame, string expected)
    {
        Assert.Equal(expected, Timecode.Format(frame, FrameRate.Fps23976));
    }

    [Theory]
    [InlineData("01:01:01:03", (25 * 3600) + (25 * 61) + 3)]
    [InlineData("00:00:00:00", 0)]
    [InlineData("1:00", 25)]
    [InlineData("2:01:00", 3025)]
    [InlineData("150", 150)]
    [InlineData("01;00;00;00", 90_000)]
    [InlineData("00.00.02.10", 60)]
    [InlineData(" 00:00:01:00 ", 25)]
    [InlineData("-00:00:01:00", -25)]
    [InlineData("90:00", 2250)]
    public void ParsesAt25(string text, long expected)
    {
        Assert.True(Timecode.TryParse(text, FrameRate.Fps25, out var frame));
        Assert.Equal(expected, frame);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("00:00:00:25")]
    [InlineData("00:00:60:00")]
    [InlineData("00:60:00:00")]
    [InlineData("aa:bb")]
    [InlineData("00:00:00:00:00")]
    [InlineData("1::2")]
    [InlineData("+5")]
    [InlineData("1,5")]
    [InlineData("99999999999999:00:00:00")]
    public void RejectsInvalidTimecode(string text)
    {
        Assert.False(Timecode.TryParse(text, FrameRate.Fps25, out var frame));
        Assert.Equal(0, frame);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(17_982)]
    [InlineData(2_589_410)]
    [InlineData(-77)]
    public void RoundTripsAtEverySupportedRate(long frame)
    {
        foreach (var rate in FrameRate.Supported)
        {
            var text = Timecode.Format(frame, rate);
            Assert.True(Timecode.TryParse(text, rate, out var parsed), text);
            Assert.Equal(frame, parsed);
        }
    }
}
