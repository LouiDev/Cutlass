using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

public sealed class TimelineQueryTests
{
    private static Project Sample() => new ProjectBuilder()
        .With(V1, V("c1", 10, 40, link: "l1", sourceFrame: 100), V("c2", 50, 30, sourceFrame: 300), V("c3", 100, 20, sourceFrame: 500) with { Enabled = false }, V("c4", 120, 10))
        .With(A1, A("a1", 10, 40, link: "l1", sourceFrame: 100))
        .Build();

    [Fact]
    public void ClipAtUsesHalfOpenRanges()
    {
        var v1 = ModelAssert.Track(Sample(), V1);
        Assert.Null(TimelineQuery.ClipAt(v1, 9));
        Assert.Equal("c1", TimelineQuery.ClipAt(v1, 10)!.Id);
        Assert.Equal("c1", TimelineQuery.ClipAt(v1, 49)!.Id);
        Assert.Equal("c2", TimelineQuery.ClipAt(v1, 50)!.Id);
        Assert.Null(TimelineQuery.ClipAt(v1, 80));
    }

    [Fact]
    public void ClipsAtListsEveryTrack()
    {
        var hits = TimelineQuery.ClipsAt(Sample(), 20);
        Assert.Equal(["c1", "a1"], hits.Select(h => h.Clip.Id));
    }

    [Fact]
    public void EditPointsAreSortedAndDistinct()
    {
        var p = Sample();
        Assert.Equal([10, 50, 80, 100, 120, 130], TimelineQuery.EditPoints(p).ToArray());
        Assert.Equal([10, 50], TimelineQuery.EditPoints(p, [A1]).ToArray());
        Assert.Equal(50, TimelineQuery.PreviousEditPoint(p, 80));
        Assert.Equal(100, TimelineQuery.NextEditPoint(p, 80));
        Assert.Null(TimelineQuery.PreviousEditPoint(p, 10));
        Assert.Null(TimelineQuery.NextEditPoint(p, 130));
    }

    [Fact]
    public void DurationIsTheLastEnd()
    {
        Assert.Equal(130, TimelineQuery.Duration(Sample()));
        Assert.Equal(0, TimelineQuery.Duration(new ProjectBuilder().Build()));
    }

    [Fact]
    public void GapsIncludeTheLeadingGap()
    {
        var v1 = ModelAssert.Track(Sample(), V1);
        Assert.Equal([(0L, 10L), (80L, 100L)], TimelineQuery.Gaps(v1).ToArray());
        Assert.Equal((80L, 100L), TimelineQuery.GapAt(v1, 90));
        Assert.Null(TimelineQuery.GapAt(v1, 60));
        Assert.Null(TimelineQuery.GapAt(v1, 500));
    }

    [Fact]
    public void RunsAreMaximalButtingEnabledSequences()
    {
        var runs = TimelineQuery.Runs(ModelAssert.Track(Sample(), V1));
        Assert.Equal(2, runs.Length);
        Assert.Equal(["c1", "c2"], runs[0].Select(c => c.Id));
        Assert.Equal(["c4"], runs[1].Select(c => c.Id));
    }

    [Fact]
    public void LinkGroupsAndPartners()
    {
        var p = Sample();
        Assert.Equal(["c1", "a1"], TimelineQuery.LinkGroup(p, "a1").ToArray());
        Assert.Equal(["c2"], TimelineQuery.LinkGroup(p, "c2").ToArray());
        Assert.Empty(TimelineQuery.LinkGroup(p, "nope"));
        Assert.Equal(["c2", "c1", "a1"], TimelineQuery.WithLinkedPartners(p, ["c2", "a1"]).ToArray());
    }

    [Fact]
    public void ExistingIdsFiltersEveryKindOfId()
    {
        var p = Sample();
        Assert.Equal(["c1", V1, "m_long"], TimelineQuery.ExistingIds(p, ["c1", "gone", V1, "m_long"]).ToArray());
    }

    [Fact]
    public void HandlesAccountForSpeed()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, media: Short, sourceFrame: 20), V("c2", 50, 20, media: Short, sourceFrame: 20, speed: 200)).Build();
        var c1 = ModelAssert.Clip(p, "c1");
        Assert.Equal(20, TimelineQuery.HeadHandle(p, c1));
        Assert.Equal(30, TimelineQuery.TailHandle(p, c1));
        var c2 = ModelAssert.Clip(p, "c2");
        Assert.Equal(10, TimelineQuery.HeadHandle(p, c2));
        Assert.Equal(20, TimelineQuery.TailHandle(p, c2));
    }

    [Fact]
    public void HandlesOfUnboundedMaterialAreUnbounded()
    {
        var p = new ProjectBuilder().With(V1, V("s1", 0, 50, media: Still), Title("t1", 50, 10)).Build();
        Assert.Equal(TimelineQuery.Unbounded, TimelineQuery.HeadHandle(p, ModelAssert.Clip(p, "s1")));
        Assert.Equal(TimelineQuery.Unbounded, TimelineQuery.TailHandle(p, ModelAssert.Clip(p, "t1")));
    }

    [Fact]
    public void ExtentAndCutFrameFollowTransitions()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
            .Transition(V1, "x1", "c1", "c2", 7)
            .Build();
        var track = ModelAssert.Track(p, V1);
        Assert.Equal((0L, 4L), TimelineQuery.Extent(track, ModelAssert.Clip(p, "c1")));
        Assert.Equal((3L, 0L), TimelineQuery.Extent(track, ModelAssert.Clip(p, "c2")));
        Assert.Equal(50, TimelineQuery.CutFrame(track, track.Transitions[0]));
        Assert.Equal("x1", TimelineQuery.TransitionAtCut(track, 50)!.Id);
        Assert.Equal((3L, 4L), TimelineQuery.TransitionHalves(7));
    }

    [Fact]
    public void MaxTransitionDurationUsesLengthsAndHandles()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, sourceFrame: 2440), V("c2", 50, 50, sourceFrame: 4)).Build();
        var track = ModelAssert.Track(p, V1);
        // Left tail handle is 2500 - 2440 - 50 = 10 frames (b <= 10); right head handle is 4 (a <= 4).
        Assert.Equal(9, TimelineQuery.MaxTransitionDuration(p, track, ModelAssert.Clip(p, "c1"), ModelAssert.Clip(p, "c2")));
    }

    [Fact]
    public void SyncOffsetMeasuresDrift()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 100, 50, sourceFrame: 200, link: "l1"))
            .With(A1, A("a1", 95, 60, sourceFrame: 200, link: "l1"))
            .Build();
        // At frame 100 the audio already shows source frame 205, so the video lags by 5 and the audio leads by 5.
        Assert.Equal(-5, TimelineQuery.SyncOffset(p, "c1"));
        Assert.Equal(5, TimelineQuery.SyncOffset(p, "a1"));
        var synced = new ProjectBuilder()
            .With(V1, V("c1", 100, 50, sourceFrame: 200, link: "l1"))
            .With(A1, A("a1", 90, 60, sourceFrame: 190, link: "l1"))
            .Build();
        Assert.Equal(0, TimelineQuery.SyncOffset(synced, "c1"));
        Assert.Null(TimelineQuery.SyncOffset(synced, "nope"));
    }

    [Fact]
    public void DisplayNameUsesFileNameOrTitleText()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 10), Title("t1", 10, 10, "Day One\nsecond line")).Build();
        Assert.Equal("long.mp4", TimelineQuery.DisplayName(p, ModelAssert.Clip(p, "c1")));
        Assert.Equal("Day One", TimelineQuery.DisplayName(p, ModelAssert.Clip(p, "t1")));
    }

    [Fact]
    public void KindIndexAndTracksOfKind()
    {
        var p = Sample();
        Assert.Equal(1, TimelineQuery.KindIndex(p, V2));
        Assert.Equal(0, TimelineQuery.KindIndex(p, A1));
        Assert.Equal(-1, TimelineQuery.KindIndex(p, "nope"));
        Assert.Equal([A1, A2], TimelineQuery.TracksOfKind(p, TrackKind.Audio).Select(t => t.Id));
    }
}
