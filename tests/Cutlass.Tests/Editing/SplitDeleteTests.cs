using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

public sealed class SplitDeleteTests
{
    private static readonly string[] AllTracks = [V1, V2, A1, A2];
    private readonly SequentialIdGenerator _ids = new(1000);

    [Fact]
    public void SplitCreatesARightHalfWithANewIdAndSource()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 10, 50, sourceFrame: 100)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Split(_ids, p, [V1], 30));
        Assert.Equal([("c1", 10L, 20L), ("c_00001000", 30L, 30L)], ModelAssert.Layout(q, V1));
        Assert.Equal(120 * F, ModelAssert.Media(q, "c_00001000").SourceInFlicks);
        Assert.Equal(100 * F, ModelAssert.Media(q, "c1").SourceInFlicks);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(5)]
    public void SplitAtABoundaryOrOutsideDoesNothing(long frame)
    {
        var p = new ProjectBuilder().With(V1, V("c1", 10, 50)).Build();
        ModelAssert.Unchanged(p, TimelineEdits.Split(_ids, p, [V1], frame));
    }

    [Fact]
    public void SplitSkipsLockedTracksAndHonoursTheClipFilter()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50))
            .With(V2, Title("t1", 0, 50))
            .With(A1, A("a1", 0, 50))
            .Lock(A1)
            .Build();
        var r = TimelineEdits.Split(_ids, p, AllTracks, 20, ["c1", "a1"]);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal(2, ModelAssert.Track(q, V1).Clips.Length);
        Assert.Single(ModelAssert.Track(q, V2).Clips);
        Assert.Single(ModelAssert.Track(q, A1).Clips);
        Assert.Contains("A1 is locked.", r.Warnings);
    }

    [Fact]
    public void SplitHalvesFormNewLinkGroups()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, link: "l1")).With(A1, A("a1", 0, 50, link: "l1")).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SplitLinked(_ids, p, "c1", 20));
        var v = ModelAssert.Track(q, V1).Clips;
        var a = ModelAssert.Track(q, A1).Clips;
        Assert.Equal(v[0].LinkId, a[0].LinkId);
        Assert.Equal(v[1].LinkId, a[1].LinkId);
        Assert.NotEqual(v[0].LinkId, v[1].LinkId);
        Assert.Equal(2, TimelineQuery.LinkGroup(q, v[1].Id).Length);
    }

    [Fact]
    public void SplitDropsFadesAtTheCutAndSplitsKeyframes()
    {
        var clip = A("a1", 0, 40) with
        {
            Audio = new AudioProps(0, 5, 6, [new VolumeKey(0, 0), new VolumeKey(30, -12)]),
        };
        var p = new ProjectBuilder().With(A1, clip).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Split(_ids, p, [A1], 21));
        var left = ModelAssert.Media(q, "a1");
        var right = ModelAssert.Media(q, ModelAssert.Track(q, A1).Clips[1].Id);
        Assert.Equal((5L, 0L), (left.Audio.FadeInFrames, left.Audio.FadeOutFrames));
        Assert.Equal((0L, 6L), (right.Audio.FadeInFrames, right.Audio.FadeOutFrames));
        Assert.Equal([0L, 20L], left.Audio.Keys.Select(k => k.Frame));
        Assert.Equal(0, left.Audio.Keys[0].Db, 9);
        Assert.Equal(-8, left.Audio.Keys[1].Db, 9);
        Assert.Equal([0L, 9L], right.Audio.Keys.Select(k => k.Frame));
        Assert.Equal(-8.4, right.Audio.Keys[0].Db, 9);
        Assert.Equal(-12, right.Audio.Keys[1].Db, 9);
    }

    [Fact]
    public void SplitMovesTheOutgoingTransitionToTheRightHalf()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Split(_ids, p, [V1], 20));
        var transition = Assert.Single(ModelAssert.Track(q, V1).Transitions);
        Assert.Equal(ModelAssert.Track(q, V1).Clips[1].Id, transition.LeftClipId);
        Assert.Equal(10, transition.Duration);
    }

    [Fact]
    public void SplitInsideATransitionRegionReclampsIt()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
            .Transition(V1, "x1", "c1", "c2", 20)
            .Build();
        var r = TimelineEdits.Split(_ids, p, [V1], 53);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal(6, Assert.Single(ModelAssert.Track(q, V1).Transitions).Duration);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void LiftLeavesAGapAndDropsTransitions()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300), V("c3", 100, 50, sourceFrame: 500))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Build();
        var r = TimelineEdits.Lift(p, ["c2"]);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal("Lift", r.Label);
        Assert.Equal([("c1", 0L, 50L), ("c3", 100L, 50L)], ModelAssert.Layout(q, V1));
        Assert.Empty(ModelAssert.Track(q, V1).Transitions);
        ModelAssert.Unchanged(p, TimelineEdits.Lift(p, ["nope"]));
    }

    [Fact]
    public void RippleDeleteClosesUpTracksWithRoom()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50), V("c2", 50, 30, sourceFrame: 100), V("c3", 80, 20, sourceFrame: 300))
            .With(V2, Title("t1", 90, 10))
            .With(A1, A("a1", 0, 100))
            .With(A2, A("m1", 85, 10, media: Music))
            .Lock(A2)
            .Build();
        var r = TimelineEdits.RippleDelete(p, ["c2"]);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal([("c1", 0L, 50L), ("c3", 50L, 20L)], ModelAssert.Layout(q, V1));
        Assert.Equal([(60L, 10L)], ModelAssert.Spans(q, V2));
        Assert.Equal([(0L, 100L)], ModelAssert.Spans(q, A1));
        Assert.Equal([(85L, 10L)], ModelAssert.Spans(q, A2));
        Assert.Contains("A1 has no room to close up and was left alone.", r.Warnings);
    }

    [Fact]
    public void RippleDeleteOfAJCutKeepsRelativeOffsets()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 100, link: "l1"), V("c2", 100, 50, sourceFrame: 300, link: "l2"))
            .With(A1, A("a1", 0, 90, link: "l1"), A("a2", 90, 60, sourceFrame: 290, link: "l2"))
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.RippleDelete(p, ["c1", "a1"]));
        Assert.Equal([(0L, 50L)], ModelAssert.Spans(q, V1));
        Assert.Equal([(0L, 60L)], ModelAssert.Spans(q, A1));
    }

    [Fact]
    public void CloseGapMovesEveryTrackWithRoom()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 40), V("c2", 60, 40, sourceFrame: 100))
            .With(A1, A("a1", 0, 40), A("a2", 70, 30, sourceFrame: 100))
            .With(A2, A("m1", 0, 100, media: Music))
            .Build();
        var r = TimelineEdits.CloseGap(p, V1, 45);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal("Close Gap", r.Label);
        Assert.Equal([(0L, 40L), (40L, 40L)], ModelAssert.Spans(q, V1));
        Assert.Equal([(0L, 40L), (50L, 30L)], ModelAssert.Spans(q, A1));
        Assert.Equal([(0L, 100L)], ModelAssert.Spans(q, A2));
    }

    [Fact]
    public void CloseLeadingGap()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 30, 40)).Build();
        Assert.Equal([(0L, 40L)], ModelAssert.Spans(ModelAssert.Changed(p, TimelineEdits.CloseGap(p, V1, 0)), V1));
    }

    [Fact]
    public void CloseGapWithoutAGapDoesNothing()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 40)).Build();
        var r = TimelineEdits.CloseGap(p, V1, 10);
        ModelAssert.Unchanged(p, r);
        Assert.Contains("There is no gap there.", r.Warnings);
        ModelAssert.Unchanged(p, TimelineEdits.CloseGap(p, V1, 100));
    }
}
