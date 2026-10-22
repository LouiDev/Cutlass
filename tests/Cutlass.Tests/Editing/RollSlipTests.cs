using Cutlass.Core.Editing;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

public sealed class RollSlipTests
{
    [Fact]
    public void RollMovesTheSharedCut()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Roll(p, "c1", "c2", 10));
        Assert.Equal([(0L, 60L), (60L, 40L)], ModelAssert.Spans(q, V1));
        Assert.Equal(310 * F, ModelAssert.Media(q, "c2").SourceInFlicks);
        var left = ModelAssert.Changed(p, TimelineEdits.Roll(p, "c1", "c2", -10));
        Assert.Equal([(0L, 40L), (40L, 60L)], ModelAssert.Spans(left, V1));
        Assert.Equal(290 * F, ModelAssert.Media(left, "c2").SourceInFlicks);
    }

    [Theory]
    [InlineData(100, 5)]     // left tail handle: short media 100 frames, c1 shows 45..95
    [InlineData(-100, -10)]  // right head handle: c2 starts at source frame 10
    public void RollIsLimitedByTheHandles(long delta, long expected)
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, media: Short, sourceFrame: 45), V("c2", 50, 50, media: Short, sourceFrame: 10)).Build();
        var r = TimelineEdits.Roll(p, "c1", "c2", delta);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal(50 + expected, ModelAssert.Clip(q, "c1").End);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void RollKeepsOneFrameOnEachSide()
    {
        var p = new ProjectBuilder().With(V1, Title("t1", 0, 5), Title("t2", 5, 5)).Build();
        Assert.Equal(9, ModelAssert.Clip(ModelAssert.Changed(p, TimelineEdits.Roll(p, "t1", "t2", 100)), "t1").End);
        Assert.Equal(1, ModelAssert.Clip(ModelAssert.Changed(p, TimelineEdits.Roll(p, "t1", "t2", -100)), "t1").End);
    }

    [Fact]
    public void RollNeedsButtingClips()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50), V("c2", 60, 50, sourceFrame: 300)).Build();
        ModelAssert.Unchanged(p, TimelineEdits.Roll(p, "c1", "c2", 5));
    }

    [Fact]
    public void RollMovesAndReclampsTheTransition()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, media: Short, sourceFrame: 40), V("c2", 50, 50, media: Short, sourceFrame: 20))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Build();
        // c1 shows source 40..90, so 10 frames remain after it; rolling right by 8 leaves 2, so b shrinks to 2.
        var r = TimelineEdits.Roll(p, "c1", "c2", 8);
        var q = ModelAssert.Changed(p, r);
        var transition = Assert.Single(ModelAssert.Track(q, V1).Transitions);
        Assert.Equal(4, transition.Duration);
        Assert.Contains(r.Warnings, w => w == "Shortened to 4 frames: not enough source after short.mov.");
    }

    [Fact]
    public void LinkedRollMovesMatchingCuts()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, link: "l1", sourceFrame: 100), V("c2", 50, 50, link: "l2", sourceFrame: 300))
            .With(A1, A("a1", 0, 50, link: "l1", sourceFrame: 100), A("a2", 50, 50, link: "l2", sourceFrame: 300))
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Roll(p, "c1", "c2", 5, includeLinked: true));
        Assert.Equal([(0L, 55L), (55L, 45L)], ModelAssert.Spans(q, A1));
        var single = ModelAssert.Changed(p, TimelineEdits.Roll(p, "c1", "c2", 5));
        Assert.Equal([(0L, 50L), (50L, 50L)], ModelAssert.Spans(single, A1));
    }

    [Fact]
    public void SlipChangesOnlyTheSource()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 10, 50, sourceFrame: 100)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Slip(p, "c1", 12, false));
        var c1 = ModelAssert.Media(q, "c1");
        Assert.Equal((10L, 50L, 112 * F), (c1.Start, c1.Duration, c1.SourceInFlicks));
    }

    [Theory]
    [InlineData(100, 30)]
    [InlineData(-100, -20)]
    public void SlipIsLimitedByTheSource(long delta, long expected)
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, media: Short, sourceFrame: 20)).Build();
        var r = TimelineEdits.Slip(p, "c1", delta, false);
        Assert.Equal((20 + expected) * F, ModelAssert.Media(ModelAssert.Changed(p, r), "c1").SourceInFlicks);
        Assert.Contains("Slip stopped at the limit of the source media.", r.Warnings);
    }

    [Fact]
    public void SlipKeepsTransitionHandles()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, media: Short, sourceFrame: 30), V("c2", 50, 40, media: Long, sourceFrame: 300))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Build();
        // c1 needs b = 5 frames after its end: source 30..80 leaves 20, so it may slip at most 15 later.
        var q = ModelAssert.Changed(p, TimelineEdits.Slip(p, "c1", 50, false));
        Assert.Equal(45 * F, ModelAssert.Media(q, "c1").SourceInFlicks);
        Assert.Equal(10, Assert.Single(ModelAssert.Track(q, V1).Transitions).Duration);
    }

    [Fact]
    public void SlipAtHalfSpeedMovesHalfAsMuchSource()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, sourceFrame: 100, speed: 50)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Slip(p, "c1", 10, false));
        Assert.Equal(105 * F, ModelAssert.Media(q, "c1").SourceInFlicks);
    }

    [Fact]
    public void LinkedSlipMovesPartnersTogether()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, link: "l1", sourceFrame: 100)).With(A1, A("a1", 0, 50, link: "l1", sourceFrame: 100)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Slip(p, "c1", -40, true));
        Assert.Equal(60 * F, ModelAssert.Media(q, "a1").SourceInFlicks);
        Assert.Equal(0, TimelineQuery.SyncOffset(q, "a1"));
    }

    [Fact]
    public void TitlesAndStillsCannotSlip()
    {
        var p = new ProjectBuilder().With(V1, Title("t1", 0, 10), V("s1", 10, 10, media: Still)).Build();
        Assert.NotEmpty(TimelineEdits.Slip(p, "t1", 5, false).Warnings);
        ModelAssert.Unchanged(p, TimelineEdits.Slip(p, "t1", 5, false));
        ModelAssert.Unchanged(p, TimelineEdits.Slip(p, "s1", 5, false));
    }
}
