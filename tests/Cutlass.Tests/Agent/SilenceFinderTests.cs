using Cutlass.Agent.Tools.Analysis;
using Cutlass.Core.Time;

namespace Cutlass.Tests.Agent;

/// <summary>Silence detection (AGENT_INTEGRATION 7.1): threshold, minimum duration and padding, at the edges too.</summary>
public sealed class SilenceFinderTests
{
    private static List<double> Levels(params (int Count, double Db)[] runs) =>
        [.. runs.SelectMany(r => Enumerable.Repeat(r.Db, r.Count))];

    [Fact]
    public void ASilenceInTheMiddleIsShrunkByThePaddingOnBothSides()
    {
        // 10 ms intervals at 25 fps: 1 s of sound, 1.5 s of silence, 1 s of sound.
        var rms = Levels((100, -10), (150, -120), (100, -10));
        var ranges = SilenceFinder.Find(rms, 10, -40, 500, 100, 0, 88, FrameRate.Fps25);
        var (start, end) = Assert.Single(ranges);
        Assert.Equal(28, start);  // 1.1 s
        Assert.Equal(60, end);    // 2.4 s
    }

    [Fact]
    public void WithoutPaddingTheSilenceMatchesTheGateWithinAFrame()
    {
        var rms = Levels((100, -10), (150, -120), (100, -10));
        var (start, end) = Assert.Single(SilenceFinder.Find(rms, 10, -40, 500, 0, 30, 118, FrameRate.Fps25));
        Assert.Equal(30 + 25, start);
        Assert.Equal(30 + 62, end);
    }

    [Fact]
    public void ShorterSilencesThanTheMinimumAreIgnored()
    {
        var rms = Levels((50, -10), (49, -120), (50, -10), (50, -120), (50, -10));
        var ranges = SilenceFinder.Find(rms, 10, -40, 500, 0, 0, 1000, FrameRate.Fps25);
        Assert.Single(ranges);
        Assert.Equal(149 * 10 * 25 / 1000 + 1, ranges[0].Start);
    }

    [Fact]
    public void SilenceAtTheEdgesIsNotPaddedThere()
    {
        var rms = Levels((60, -120), (100, -10), (60, -120));
        var ranges = SilenceFinder.Find(rms, 10, -40, 500, 100, 10, 65, FrameRate.Fps25);
        Assert.Equal(2, ranges.Count);
        Assert.Equal((10L, 22L), ranges[0]);  // 0 to 0.6 s - 0.1 s padding at the sound side only
        Assert.Equal(10 + 43, ranges[1].Start); // 1.6 s + 0.1 s = 1.7 s, frame 42.5 rounded up
        Assert.Equal(65, ranges[1].End);
    }

    [Fact]
    public void PaddingThatEatsTheSilenceLeavesNothing()
    {
        var rms = Levels((50, -10), (60, -120), (50, -10));
        Assert.Empty(SilenceFinder.Find(rms, 10, -40, 500, 300, 0, 100, FrameRate.Fps25));
    }

    [Fact]
    public void LevelsAtTheThresholdAreSound()
    {
        var rms = Levels((100, -40), (100, -40.01));
        var (start, _) = Assert.Single(SilenceFinder.Find(rms, 10, -40, 500, 0, 0, 50, FrameRate.Fps25));
        Assert.Equal(25, start);
    }
}
