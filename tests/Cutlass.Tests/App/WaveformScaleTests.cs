using Cutlass.App.Controls;

namespace Cutlass.Tests.App;

/// <summary>DESIGN 5.1: the waveform display curve (square root of the normalised peak).</summary>
public sealed class WaveformScaleTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(32768, 1)]
    [InlineData(-32768, -1)]
    [InlineData(8192, 0.5)]
    [InlineData(-8192, -0.5)]
    public void MapsTheEndsAndKeepsTheSign(double sample, double expected) =>
        Assert.Equal(expected, WaveformScale.Display(sample), 9);

    [Fact]
    public void AQuietToneIsMuchTallerThanOnALinearScale()
    {
        // -20 dBFS: a tenth of full scale draws at about a third of the half height (a linear scale gave 1 px lines).
        var tenth = WaveformScale.SampleScale / 10;
        Assert.Equal(Math.Sqrt(0.1), WaveformScale.Display(tenth), 9);
        Assert.True(WaveformScale.Display(tenth) > 3 * (tenth / WaveformScale.SampleScale));
    }

    [Fact]
    public void IsMonotonicAndClamped()
    {
        var previous = -2.0;
        for (var s = -40000; s <= 40000; s += 250)
        {
            var value = WaveformScale.Display(s);
            Assert.InRange(value, -1, 1);
            Assert.True(value >= previous);
            previous = value;
        }
    }
}
