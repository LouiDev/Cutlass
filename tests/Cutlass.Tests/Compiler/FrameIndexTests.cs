using Cutlass.Compiler;
using Cutlass.Core.Time;
using Cutlass.Media.Services;

namespace Cutlass.Tests.Compiler;

/// <summary>FrameIndex: exact floor lookups against flick positions (TECH_BRIEF 6.4 and 7.2).</summary>
public sealed class FrameIndexTests
{
    private const long F = TimeMath.FlicksPerSecond;

    [Fact]
    public void FloorOrdinalIsSampleAndHold()
    {
        // 25 fps in 1/12800: frame i at 512 * i.
        var index = new FrameIndex(new Rational(1, 12800), Enumerable.Range(0, 100).Select(i => 512L * i));

        Assert.Equal(0, index.FloorOrdinal(0));
        Assert.Equal(0, index.FloorOrdinal((F / 25) - 1));
        Assert.Equal(1, index.FloorOrdinal(F / 25));
        Assert.Equal(25, index.FloorOrdinal(F));
        Assert.Equal(99, index.FloorOrdinal(1000 * F));
        Assert.Equal(0, index.FloorOrdinal(-F));
    }

    [Fact]
    public void ExactComparisonAtNtscBoundaries()
    {
        // 30000/1001 in 1/30000: frame i at 1001 * i. Frame 1 sits at exactly 23,543,520 flicks.
        var index = new FrameIndex(new Rational(1, 30000), Enumerable.Range(0, 10).Select(i => 1001L * i));

        Assert.Equal(1, index.FloorOrdinal(23_543_520));
        Assert.Equal(0, index.FloorOrdinal(23_543_519));
        Assert.Equal(23_543_520, index.FlicksAt(1));
        Assert.Equal(33_366, index.FloorMicrosAt(1));
    }

    [Fact]
    public void StartOffsetsAreAbsolute()
    {
        // MPEG-TS style: 1/90000 starting at 1.4 s.
        var index = new FrameIndex(new Rational(1, 90000), Enumerable.Range(0, 10).Select(i => 126_000 + (3600L * i)));

        Assert.Equal(0, index.FloorOrdinal(0));
        Assert.Equal(0, index.FloorOrdinal((F * 14 / 10) + (F / 25) - 1));
        Assert.Equal(1, index.FloorOrdinal((F * 14 / 10) + (F / 25)));
        Assert.Equal(1_400_000, index.FloorMicrosAt(0));
        Assert.Equal(126_000, index[0]);
        Assert.Equal(10, index.Count);
        Assert.Equal(10, index.Pts.Length);
    }

    [Fact]
    public void RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => new FrameIndex(new Rational(1, 25), []));
        Assert.Throws<ArgumentException>(() => new FrameIndex(new Rational(1, 25), [0, 2, 2]));
        Assert.Throws<ArgumentException>(() => new FrameIndex(new Rational(1, 25), [3, 1]));
        Assert.Throws<ArgumentException>(() => new FrameIndex(default, [0]));
    }

    [Fact]
    public void BuildsFromAPacketListing()
    {
        const string csv = "0,0,K__\n1024,512,___\nN/A,1536,___\n512,N/A,___\n2048,2048,_D_\n512,512,___\n";

        var index = FrameIndexService.Build(csv, new Rational(1, 12800));

        Assert.NotNull(index);
        Assert.Equal(new long[] { 0, 512, 1024, 1536 }, index.Pts.ToArray());
        Assert.Null(FrameIndexService.Build(csv, default));
        Assert.Null(FrameIndexService.Build("garbage\nmore garbage\n", new Rational(1, 25)));
    }
}
