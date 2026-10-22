using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

public sealed class TrimTests
{
    public static TheoryData<ClipEdge, long, bool, long, long> PlainTrimCases => new()
    {
        // edge, delta, ripple, expected start and duration of c2 = [50, 100) between c1 = [0, 40) and c3 = [110, 160)
        { ClipEdge.End, -10, false, 50, 40 },
        { ClipEdge.End, 30, false, 50, 60 },          // stops at the neighbour at 110
        { ClipEdge.Start, 10, false, 60, 40 },
        { ClipEdge.Start, -30, false, 40, 60 },       // stops at the previous clip's end (40)
        { ClipEdge.Start, 60, false, 99, 1 },         // keeps one frame
        { ClipEdge.End, -60, false, 50, 1 },
    };

    [Theory]
    [MemberData(nameof(PlainTrimCases))]
    public void PlainTrimsClampToNeighboursAndLength(ClipEdge edge, long delta, bool ripple, long start, long duration)
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 40), V("c2", 50, 50, sourceFrame: 100), V("c3", 110, 50, sourceFrame: 300)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Trim(p, "c2", edge, delta, ripple, includeLinked: false));
        var c2 = ModelAssert.Clip(q, "c2");
        Assert.Equal((start, duration), (c2.Start, c2.Duration));
        Assert.Equal(40, ModelAssert.Clip(q, "c1").Duration);
        Assert.Equal(110, ModelAssert.Clip(q, "c3").Start);
    }

    [Fact]
    public void StartTrimMovesTheSourceInPoint()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 50, 50, sourceFrame: 100)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Trim(p, "c1", ClipEdge.Start, 7, false, false));
        Assert.Equal(107 * F, ModelAssert.Media(q, "c1").SourceInFlicks);
        var back = ModelAssert.Changed(q, TimelineEdits.Trim(q, "c1", ClipEdge.Start, -7, false, false));
        Assert.Equal(100 * F, ModelAssert.Media(back, "c1").SourceInFlicks);
    }

    [Fact]
    public void TrimsStopAtTheSourceLimits()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 50, 50, media: Short, sourceFrame: 20)).Build();
        var end = TimelineEdits.Trim(p, "c1", ClipEdge.End, 100, false, false);
        Assert.Equal(80, ModelAssert.Clip(ModelAssert.Changed(p, end), "c1").Duration);
        Assert.Contains("Reached the end of the source media.", end.Warnings);

        var start = TimelineEdits.Trim(p, "c1", ClipEdge.Start, -40, false, false);
        var c1 = ModelAssert.Media(ModelAssert.Changed(p, start), "c1");
        Assert.Equal((30L, 70L, 0L), (c1.Start, c1.Duration, c1.SourceInFlicks));
        Assert.Contains("Reached the start of the source media.", start.Warnings);
    }

    [Fact]
    public void TrimAgainstTheNeighbourWarns()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 40), V("c2", 50, 50, sourceFrame: 100)).Build();
        Assert.Contains("Reached the next clip.", TimelineEdits.Trim(p, "c1", ClipEdge.End, 20, false, false).Warnings);
        Assert.Contains("Reached the previous clip.", TimelineEdits.Trim(p, "c2", ClipEdge.Start, -20, false, false).Warnings);
    }

    [Fact]
    public void StillsAndTitlesTrimWithoutLimits()
    {
        var p = new ProjectBuilder().With(V1, V("s1", 100, 10, media: Still), Title("t1", 200, 10)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Trim(p, "s1", ClipEdge.Start, -100, false, false));
        Assert.Equal((0L, 110L), (ModelAssert.Clip(q, "s1").Start, ModelAssert.Clip(q, "s1").Duration));
        Assert.Equal(0, ModelAssert.Media(q, "s1").SourceInFlicks);
        var t = ModelAssert.Changed(p, TimelineEdits.Trim(p, "t1", ClipEdge.End, 5000, false, false));
        Assert.Equal(5010, ModelAssert.Clip(t, "t1").Duration);
    }

    [Fact]
    public void RippleEndTrimShiftsLaterClipsOnAllUnlockedTracks()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50), V("c2", 50, 50, sourceFrame: 100))
            .With(V2, Title("t1", 60, 10))
            .With(A1, A("a1", 0, 50), A("a2", 70, 30, sourceFrame: 100))
            .With(A2, A("m1", 80, 10, media: Music))
            .Lock(A2)
            .Build();
        var r = TimelineEdits.Trim(p, "c1", ClipEdge.End, 10, ripple: true, includeLinked: false);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal("Ripple Trim", r.Label);
        Assert.Equal([(0L, 60L), (60L, 50L)], ModelAssert.Spans(q, V1));
        Assert.Equal([(70L, 10L)], ModelAssert.Spans(q, V2));
        Assert.Equal([(0L, 50L), (80L, 30L)], ModelAssert.Spans(q, A1));
        Assert.Equal([(80L, 10L)], ModelAssert.Spans(q, A2));
    }

    [Fact]
    public void RippleShorteningLeavesTracksWithoutRoomAlone()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50), V("c2", 50, 50, sourceFrame: 100))
            .With(A1, A("a1", 0, 50), A("a2", 50, 50, sourceFrame: 100))
            .With(A2, A("m1", 0, 45, media: Music), A("m2", 55, 10, media: Music, sourceFrame: 100))
            .Build();
        var r = TimelineEdits.Trim(p, "c1", ClipEdge.End, -10, ripple: true, includeLinked: false);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal([(0L, 40L), (40L, 50L)], ModelAssert.Spans(q, V1));
        Assert.Equal([(0L, 50L), (50L, 50L)], ModelAssert.Spans(q, A1));      // no room: a2 butts a1
        Assert.Equal([(0L, 45L), (45L, 10L)], ModelAssert.Spans(q, A2));      // 10 frames of room from 45 to 55
        Assert.Contains("A1 has no room to close up and was left alone.", r.Warnings);
    }

    [Fact]
    public void RippleStartTrimKeepsTheStartAndShiftsLaterClips()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 10, 50, sourceFrame: 100), V("c2", 60, 50, sourceFrame: 300))
            .With(A1, A("a1", 80, 10, media: Music))
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Trim(p, "c1", ClipEdge.Start, 20, ripple: true, includeLinked: false));
        var c1 = ModelAssert.Media(q, "c1");
        Assert.Equal((10L, 30L, 120 * F), (c1.Start, c1.Duration, c1.SourceInFlicks));
        Assert.Equal(40, ModelAssert.Clip(q, "c2").Start);
        Assert.Equal(60, ModelAssert.Clip(q, "a1").Start);
    }

    [Fact]
    public void RippleStartTrimCanExtendIntoTheHandle()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, sourceFrame: 5), V("c2", 50, 50, sourceFrame: 300)).Build();
        var r = TimelineEdits.Trim(p, "c1", ClipEdge.Start, -9, ripple: true, includeLinked: false);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal([(0L, 55L), (55L, 50L)], ModelAssert.Spans(q, V1));
        Assert.Equal(0, ModelAssert.Media(q, "c1").SourceInFlicks);
        Assert.Contains("Reached the start of the source media.", r.Warnings);
    }

    [Fact]
    public void LinkedTrimIncludesPartnersOnTheSameFrame()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, link: "l1"))
            .With(A1, A("a1", 0, 50, link: "l1"))
            .With(A2, A("a2", 0, 60, link: "l1"))
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Trim(p, "c1", ClipEdge.End, -10, false, includeLinked: true));
        Assert.Equal(40, ModelAssert.Clip(q, "c1").Duration);
        Assert.Equal(40, ModelAssert.Clip(q, "a1").Duration);
        Assert.Equal(60, ModelAssert.Clip(q, "a2").Duration);
    }

    [Fact]
    public void AltTrimTouchesOneSideOnly()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, link: "l1")).With(A1, A("a1", 0, 50, link: "l1")).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Trim(p, "a1", ClipEdge.End, -12, false, includeLinked: false));
        Assert.Equal(50, ModelAssert.Clip(q, "c1").Duration);
        Assert.Equal(38, ModelAssert.Clip(q, "a1").Duration);
    }

    [Fact]
    public void LinkedTrimUsesTheTightestLimit()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, link: "l1"))
            .With(A1, A("a1", 0, 50, link: "l1"), A("a2", 55, 10, sourceFrame: 300))
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Trim(p, "c1", ClipEdge.End, 20, false, includeLinked: true));
        Assert.Equal(55, ModelAssert.Clip(q, "c1").End);
        Assert.Equal(55, ModelAssert.Clip(q, "a1").End);
    }

    [Fact]
    public void LockedTrackIsNotTrimmed()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50)).Lock(V1).Build();
        var r = TimelineEdits.Trim(p, "c1", ClipEdge.End, -5, false, false);
        ModelAssert.Unchanged(p, r);
        Assert.Contains("V1 is locked.", r.Warnings);
    }

    [Fact]
    public void ZeroOrBlockedTrimReturnsTheSameReference()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50), V("c2", 50, 50, sourceFrame: 100)).Build();
        ModelAssert.Unchanged(p, TimelineEdits.Trim(p, "c1", ClipEdge.End, 0, false, false));
        ModelAssert.Unchanged(p, TimelineEdits.Trim(p, "c1", ClipEdge.End, 5, false, false));
        ModelAssert.Unchanged(p, TimelineEdits.Trim(p, "nope", ClipEdge.End, 5, false, false));
    }

    [Fact]
    public void TrimShiftsKeyframesAndClampsFades()
    {
        var clip = A("a1", 0, 50) with
        {
            Audio = new AudioProps(0, 10, 30, [new VolumeKey(5, -6), new VolumeKey(20, 0), new VolumeKey(45, -12)]),
        };
        var p = new ProjectBuilder().With(A1, clip).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Trim(p, "a1", ClipEdge.Start, 10, false, false));
        var a1 = ModelAssert.Media(q, "a1");
        Assert.Equal([new VolumeKey(10, 0), new VolumeKey(35, -12)], a1.Audio.Keys.ToArray());
        Assert.Equal((10L, 30L), (a1.Audio.FadeInFrames, a1.Audio.FadeOutFrames));

        var shorter = ModelAssert.Changed(q, TimelineEdits.Trim(q, "a1", ClipEdge.End, -25, false, false));
        var s = ModelAssert.Media(shorter, "a1");
        Assert.Equal([new VolumeKey(10, 0)], s.Audio.Keys.ToArray());
        Assert.Equal((10L, 5L), (s.Audio.FadeInFrames, s.Audio.FadeOutFrames));
    }

    [Fact]
    public void QRippleTrimsFromTheClipStartToThePlayhead()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, link: "l1"), V("c2", 50, 50, sourceFrame: 100))
            .With(A1, A("a1", 0, 50, link: "l1"))
            .Build();
        var r = TimelineEdits.RippleTrimToFrame(p, [V1, A1], 20, ClipEdge.Start);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal("Ripple Trim Start", r.Label);
        Assert.Equal([(0L, 30L), (30L, 50L)], ModelAssert.Spans(q, V1));
        Assert.Equal([(0L, 30L)], ModelAssert.Spans(q, A1));
        Assert.Equal(20 * F, ModelAssert.Media(q, "c1").SourceInFlicks);
    }

    [Fact]
    public void WRippleTrimsFromThePlayheadToTheClipEnd()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50), V("c2", 50, 50, sourceFrame: 100)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.RippleTrimToFrame(p, [V1], 70, ClipEdge.End));
        Assert.Equal([(0L, 50L), (50L, 20L)], ModelAssert.Spans(q, V1));
    }

    [Fact]
    public void QWithoutAClipUnderThePlayheadWarns()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50)).Build();
        var r = TimelineEdits.RippleTrimToFrame(p, [V1], 70, ClipEdge.Start);
        ModelAssert.Unchanged(p, r);
        Assert.NotEmpty(r.Warnings);
    }
}
