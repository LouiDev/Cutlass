using Cutlass.Core.Editing;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

public sealed class SnapEngineTests
{
    [Fact]
    public void CollectGathersEveryTargetSortedAndDistinct()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 10, 40), V("c2", 50, 30, sourceFrame: 100))
            .With(A1, A("a1", 10, 70))
            .Marker("k1", 200)
            .InOut(5, 300)
            .Build();
        Assert.Equal([0, 5, 10, 50, 80, 120, 200, 300], SnapEngine.Collect(p, 120).ToArray());
        Assert.Equal([0, 5, 10, 50, 80, 120, 200, 300], SnapEngine.Collect(p, 120, ["c1"]).ToArray());
        Assert.Equal([0, 5, 10, 80, 120, 200, 300], SnapEngine.Collect(p, 120, ["c1", "c2"]).ToArray());
        Assert.Equal([0, 5, 120, 200, 300], SnapEngine.Collect(p, 120, ["c1", "c2", "a1"]).ToArray());
    }

    [Theory]
    [InlineData(1.0, 107, 100)]     // 8 px is 8 frames: 7 frames away snaps
    [InlineData(1.0, 109, 109)]     // 9 frames away does not
    [InlineData(0.1, 170, 100)]     // zoomed out: 8 px is 80 frames
    [InlineData(40.0, 100, 100)]    // zoomed in: 8 px is 0.2 frames, only exact hits
    [InlineData(40.0, 101, 101)]
    public void ThresholdScalesWithZoom(double pixelsPerFrame, long requestedPosition, long expectedPosition)
    {
        var result = SnapEngine.Resolve([0], requestedPosition, [100, 400], pixelsPerFrame);
        Assert.Equal(expectedPosition, result.Delta);
        Assert.Equal(expectedPosition == 100, result.Snapped);
    }

    [Fact]
    public void NearestCandidateOverAllEdgesWins()
    {
        // A clip [10, 50) moved by 47: its start lands on 57 (3 from 60) and its end on 97 (1 from 98).
        var result = SnapEngine.Resolve([10, 50], 47, [60, 98], pixelsPerFrame: 2);
        Assert.Equal(new SnapResult(48, 98), result);
    }

    [Fact]
    public void TiesGoToTheEarlierEdge()
    {
        var result = SnapEngine.Resolve([10, 50], 0, [8, 52], pixelsPerFrame: 1);
        Assert.Equal(new SnapResult(-2, 8), result);
    }

    [Fact]
    public void NothingInRangeKeepsTheDelta()
    {
        Assert.Equal(new SnapResult(5, null), SnapEngine.Resolve([0], 5, [100], 1));
        Assert.Equal(new SnapResult(5, null), SnapEngine.Resolve([0], 5, [], 1));
        Assert.Equal(new SnapResult(5, null), SnapEngine.Resolve([], 5, [5], 1));
    }

    [Fact]
    public void CustomThresholdIsHonoured()
    {
        Assert.True(SnapEngine.Resolve([0], 15, [20], 1, thresholdPx: 5).Snapped);
        Assert.False(SnapEngine.Resolve([0], 14, [20], 1, thresholdPx: 5).Snapped);
    }
}
