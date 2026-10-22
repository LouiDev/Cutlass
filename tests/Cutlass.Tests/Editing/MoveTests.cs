using Cutlass.Core.Editing;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

public sealed class MoveTests
{
    private readonly SequentialIdGenerator _ids = new(1000);

    [Fact]
    public void OverwriteMoveTrimsTheDestination()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 20), V("c2", 50, 50, sourceFrame: 100)).Build();
        var r = TimelineEdits.Move(_ids, p, ["c1"], 40, 0, EditMode.Overwrite);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal([("c1", 40L, 20L), ("c2", 60L, 40L)], ModelAssert.Layout(q, V1));
        Assert.Equal(["c1"], r.Select.ToArray());
        Assert.Equal("Move", r.Label);
    }

    [Fact]
    public void InsertMoveOpensRoomOnEveryUnlockedTrack()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 20), V("c2", 50, 50, sourceFrame: 100))
            .With(A1, A("a1", 50, 50, sourceFrame: 100))
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Move(_ids, p, ["c1"], 50, 0, EditMode.Insert));
        Assert.Equal([("c1", 50L, 20L), ("c2", 70L, 50L)], ModelAssert.Layout(q, V1));
        Assert.Equal([("a1", 70L, 50L)], ModelAssert.Layout(q, A1));
    }

    [Fact]
    public void LinkedPartnersFollowTheTrackOffsetWithinTheirKind()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 20, link: "l1")).With(A1, A("a1", 0, 20, link: "l1")).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Move(_ids, p, ["c1", "a1"], 10, 1, EditMode.Overwrite));
        Assert.Equal([("c1", 10L, 20L)], ModelAssert.Layout(q, V2));
        Assert.Equal([("a1", 10L, 20L)], ModelAssert.Layout(q, A2));
    }

    [Fact]
    public void APartnerWithoutATargetTrackStaysOnItsTrack()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 20, link: "l1")).With(A2, A("a1", 0, 20, link: "l1")).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Move(_ids, p, ["c1", "a1"], 0, 1, EditMode.Overwrite));
        Assert.Single(ModelAssert.Track(q, V2).Clips);
        Assert.Single(ModelAssert.Track(q, A2).Clips);
    }

    [Fact]
    public void NothingMovesOntoALockedTrack()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 20)).Lock(V2).Build();
        var r = TimelineEdits.Move(_ids, p, ["c1"], 0, 1, EditMode.Overwrite);
        ModelAssert.Unchanged(p, r);
        Assert.Contains("Clips cannot move onto a locked track.", r.Warnings);
    }

    [Fact]
    public void ClipsOnLockedTracksDoNotMove()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 20)).Lock(V1).Build();
        var r = TimelineEdits.Move(_ids, p, ["c1"], 10, 0, EditMode.Overwrite);
        ModelAssert.Unchanged(p, r);
        Assert.Contains("V1 is locked.", r.Warnings);
    }

    [Fact]
    public void MovesClampAtFrameZero()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 10, 20), V("c2", 40, 20, sourceFrame: 100)).Build();
        var r = TimelineEdits.Move(_ids, p, ["c1", "c2"], -25, 0, EditMode.Overwrite);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal([("c1", 0L, 20L), ("c2", 30L, 20L)], ModelAssert.Layout(q, V1));
        Assert.Contains("Clips cannot move before frame 0.", r.Warnings);
    }

    [Fact]
    public void ZeroMoveReturnsTheSameReference()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 10, 20)).Build();
        ModelAssert.Unchanged(p, TimelineEdits.Move(_ids, p, ["c1"], 0, 0, EditMode.Overwrite));
        ModelAssert.Unchanged(p, TimelineEdits.Move(_ids, p, ["nope"], 5, 0, EditMode.Overwrite));
    }

    [Fact]
    public void TransitionsBetweenMovedClipsTravelWithThem()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300), V("c3", 100, 50, sourceFrame: 500))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Transition(V1, "x2", "c2", "c3", 10)
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Move(_ids, p, ["c1", "c2"], 0, 1, EditMode.Overwrite));
        Assert.Equal(["x1"], ModelAssert.Track(q, V2).Transitions.Select(t => t.Id));
        Assert.Empty(ModelAssert.Track(q, V1).Transitions);
    }

    [Fact]
    public void NudgeMovesByFramesInOverwriteMode()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 10, 20), V("c2", 30, 20, sourceFrame: 100)).Build();
        var r = TimelineEdits.Nudge(_ids, p, ["c1"], 1);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal("Nudge", r.Label);
        Assert.Equal([("c1", 11L, 20L), ("c2", 31L, 19L)], ModelAssert.Layout(q, V1));
    }

    [Fact]
    public void MoveIntoSyncRealignsAPartner()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 100, 50, sourceFrame: 200, link: "l1"))
            .With(A1, A("a1", 95, 60, sourceFrame: 200, link: "l1"))
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.MoveIntoSync(_ids, p, "a1"));
        Assert.Equal(100, ModelAssert.Clip(q, "a1").Start);
        Assert.Equal(0, TimelineQuery.SyncOffset(q, "a1"));
        ModelAssert.Unchanged(q, TimelineEdits.MoveIntoSync(_ids, q, "a1"));
    }
}
