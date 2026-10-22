using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

public sealed class TransitionTests
{
    private readonly SequentialIdGenerator _ids = new(1000);

    private static Project Cut(long leftSource = 100, long rightSource = 300, string media = Long) =>
        new ProjectBuilder()
            .With(V1, V("c1", 0, 50, media: media, sourceFrame: leftSource), V("c2", 50, 50, media: media, sourceFrame: rightSource))
            .With(A1, A("a1", 0, 50, sourceFrame: 100), A("a2", 50, 50, sourceFrame: 300))
            .Build();

    [Fact]
    public void AddCentresTheTransitionOnTheCut()
    {
        var p = Cut();
        var r = TimelineEdits.AddTransition(_ids, p, V1, 50, TransitionKind.WipeLeft, 25);
        var q = ModelAssert.Changed(p, r);
        var t = Assert.Single(ModelAssert.Track(q, V1).Transitions);
        Assert.Equal(new Transition("x_00001000", TransitionKind.WipeLeft, "c1", "c2", 25), t);
        Assert.Equal(["x_00001000"], r.Select.ToArray());
        Assert.Equal((12L, 13L), TimelineQuery.TransitionHalves(t.Duration));
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void DurationClampsToTheClipLengths()
    {
        var p = Cut();
        var r = TimelineEdits.AddTransition(_ids, p, V1, 50, TransitionKind.Crossfade, 300);
        Assert.Equal(100, Assert.Single(ModelAssert.Track(ModelAssert.Changed(p, r), V1).Transitions).Duration);
        Assert.Contains("Shortened to 100 frames: long.mp4 is too short.", r.Warnings);
    }

    [Fact]
    public void DurationClampsToTheSourceAfterTheLeftClip()
    {
        var p = Cut(leftSource: 40, rightSource: 20, media: Short);   // left shows 40..90: 10 frames after it
        var r = TimelineEdits.AddTransition(_ids, p, V1, 50, TransitionKind.Crossfade, 30);
        Assert.Equal(20, Assert.Single(ModelAssert.Track(ModelAssert.Changed(p, r), V1).Transitions).Duration);
        Assert.Contains("Shortened to 20 frames: not enough source after short.mov.", r.Warnings);
    }

    [Fact]
    public void DurationClampsToTheSourceBeforeTheRightClip()
    {
        var p = Cut(leftSource: 10, rightSource: 6, media: Short);
        var r = TimelineEdits.AddTransition(_ids, p, V1, 50, TransitionKind.Crossfade, 30);
        Assert.Equal(13, Assert.Single(ModelAssert.Track(ModelAssert.Changed(p, r), V1).Transitions).Duration);
        Assert.Contains("Shortened to 13 frames: not enough source before short.mov.", r.Warnings);
    }

    [Fact]
    public void NoRoomMeansNoTransition()
    {
        var p = Cut(leftSource: 50, rightSource: 0, media: Short);
        var r = TimelineEdits.AddTransition(_ids, p, V1, 50, TransitionKind.Crossfade, 30);
        ModelAssert.Unchanged(p, r);
        Assert.Contains("No room for a transition: not enough source after short.mov.", r.Warnings);
    }

    [Fact]
    public void NoCutMeansNoTransition()
    {
        var p = Cut();
        ModelAssert.Unchanged(p, TimelineEdits.AddTransition(_ids, p, V1, 30, TransitionKind.Crossfade, 10));
        ModelAssert.Unchanged(p, TimelineEdits.AddTransition(_ids, p, V2, 50, TransitionKind.Crossfade, 10));
    }

    [Fact]
    public void KindsFollowTheTrack()
    {
        var p = Cut();
        var q = ModelAssert.Changed(p, TimelineEdits.AddTransitions(_ids, p, [(V1, 50), (A1, 50)], TransitionKind.AudioCrossfade, 10));
        Assert.Equal(TransitionKind.Crossfade, Assert.Single(ModelAssert.Track(q, V1).Transitions).Kind);
        Assert.Equal(TransitionKind.AudioCrossfade, Assert.Single(ModelAssert.Track(q, A1).Transitions).Kind);
    }

    [Fact]
    public void AddingOnAnExistingCutUpdatesIt()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Build();
        var r = TimelineEdits.AddTransition(_ids, p, V1, 50, TransitionKind.DipToBlack, 20);
        var t = Assert.Single(ModelAssert.Track(ModelAssert.Changed(p, r), V1).Transitions);
        Assert.Equal(new Transition("x1", TransitionKind.DipToBlack, "c1", "c2", 20), t);
        Assert.Equal(["x1"], r.Select.ToArray());
    }

    [Fact]
    public void TransitionsOnOneClipNeverOverlap()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 20, sourceFrame: 300), V("c3", 70, 50, sourceFrame: 500))
            .Transition(V1, "x1", "c1", "c2", 24)     // b = 12 frames of c2
            .Build();
        var r = TimelineEdits.AddTransition(_ids, p, V1, 70, TransitionKind.Crossfade, 30);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal(17, ModelAssert.Track(q, V1).Transitions.Single(t => t.LeftClipId == "c2").Duration);   // a = 8 fits beside 12
        Assert.Contains("Shortened to 17 frames: long.mp4 is too short.", r.Warnings);
    }

    [Fact]
    public void SetDurationClampsAndKeepsTheMinimum()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Build();
        Assert.Equal(40, ModelAssert.Track(ModelAssert.Changed(p, TimelineEdits.SetTransitionDuration(p, "x1", 40)), V1).Transitions[0].Duration);
        Assert.Equal(100, ModelAssert.Track(ModelAssert.Changed(p, TimelineEdits.SetTransitionDuration(p, "x1", 1000)), V1).Transitions[0].Duration);
        var r = TimelineEdits.SetTransitionDuration(p, "x1", 0);
        Assert.Equal(2, ModelAssert.Track(ModelAssert.Changed(p, r), V1).Transitions[0].Duration);
        Assert.NotEmpty(r.Warnings);
        ModelAssert.Unchanged(p, TimelineEdits.SetTransitionDuration(p, "x1", 10));
    }

    [Fact]
    public void OpeningTheCutRemovesTheTransition()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Trim(p, "c1", ClipEdge.End, -5, false, false));
        Assert.Empty(ModelAssert.Track(q, V1).Transitions);
    }

    [Fact]
    public void RippleTrimMovesTheCutAndKeepsTheTransition()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Trim(p, "c1", ClipEdge.End, -5, ripple: true, includeLinked: false));
        Assert.Equal(10, Assert.Single(ModelAssert.Track(q, V1).Transitions).Duration);
        Assert.Equal(45, ModelAssert.Clip(q, "c2").Start);
    }

    [Fact]
    public void TrimmingAClipShorterThanItsTransitionReclampsIt()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
            .Transition(V1, "x1", "c1", "c2", 40)
            .Build();
        var r = TimelineEdits.Trim(p, "c2", ClipEdge.End, -40, false, false);
        Assert.Equal(20, Assert.Single(ModelAssert.Track(ModelAssert.Changed(p, r), V1).Transitions).Duration);
        Assert.Contains(r.Warnings, w => w.StartsWith("Shortened to 20 frames", StringComparison.Ordinal));
    }

    [Fact]
    public void SetKindAndRemove()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
            .With(A1, A("a1", 0, 50, sourceFrame: 100), A("a2", 50, 50, sourceFrame: 300))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Transition(A1, "x2", "a1", "a2", 10)
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SetTransitionKind(p, ["x1", "x2"], TransitionKind.CircleOpen));
        Assert.Equal(TransitionKind.CircleOpen, ModelAssert.Track(q, V1).Transitions[0].Kind);
        Assert.Equal(TransitionKind.AudioCrossfade, ModelAssert.Track(q, A1).Transitions[0].Kind);
        var removed = ModelAssert.Changed(q, TimelineEdits.RemoveTransitions(q, ["x1"]));
        Assert.Empty(ModelAssert.Track(removed, V1).Transitions);
        Assert.Single(ModelAssert.Track(removed, A1).Transitions);
        ModelAssert.Unchanged(removed, TimelineEdits.RemoveTransitions(removed, ["x1"]));
    }

    [Fact]
    public void LockedTracksRefuseTransitionEdits()
    {
        var p = Cut();
        var locked = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
            .Lock(V1)
            .Build();
        var r = TimelineEdits.AddTransition(_ids, locked, V1, 50, TransitionKind.Crossfade, 10);
        ModelAssert.Unchanged(locked, r);
        Assert.Contains("V1 is locked.", r.Warnings);
        Assert.NotSame(p, locked);
    }
}
