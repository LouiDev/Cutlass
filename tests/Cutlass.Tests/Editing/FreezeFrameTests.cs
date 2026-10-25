using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

/// <summary>The freeze frame edit (effects batch 4): split at the frame, hold the frame's source position.</summary>
public sealed class FreezeFrameTests
{
    private readonly SequentialIdGenerator _ids = new(1000);

    private static Project Scene() => new ProjectBuilder()
        .With(V1, V("c1", 0, 100, sourceFrame: 100, link: "l1"), V("c2", 100, 50, sourceFrame: 300))
        .With(A1, A("a1", 0, 100, sourceFrame: 100, link: "l1"))
        .Build();

    [Fact]
    public void FreezeAtThePlayheadSplitsTheClipAndInsertsTheHold()
    {
        var p = Scene();
        var r = TimelineEdits.FreezeFrame(_ids, p, "c1", 40, 25, ripple: true);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal("Freeze Frame", r.Label);
        var frozenId = Assert.Single(r.Select);
        var frozen = ModelAssert.Media(q, frozenId);
        Assert.Equal((40L, 25L, 140 * F, 100, true, StreamKind.Video), (frozen.Start, frozen.Duration, frozen.SourceInFlicks, frozen.SpeedPercent, frozen.Freeze, frozen.Stream));
        Assert.Null(frozen.LinkId);
        Assert.Equal(TestMedia.Long, frozen.MediaId);

        // The left half keeps its id, the right half follows the hold with its source continuing at frame 140,
        // and the clips after it ripple on every unlocked track (the linked audio is split with the video).
        Assert.Equal([(0L, 40L), (40L, 25L), (65L, 60L), (125L, 50L)], ModelAssert.Spans(q, V1));
        Assert.Equal(140 * F, ModelAssert.Media(q, ModelAssert.Track(q, V1).Clips[2].Id).SourceInFlicks);
        Assert.Equal([(0L, 40L), (65L, 60L)], ModelAssert.Spans(q, A1));
        AssertRightPiecesShareOneLink(q);
        Assert.False(ModelAssert.Media(q, "c1").Freeze);
        Assert.Equal(140 * F, TimeMath.SourceFlicksAt(frozen, 64, Rate));
    }

    [Fact]
    public void WithoutRippleTheHoldOverwritesWhatFollows()
    {
        var p = Scene();
        var r = TimelineEdits.FreezeFrame(_ids, p, "c1", 40, 25, ripple: false);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal([(0L, 40L), (40L, 25L), (65L, 35L), (100L, 50L)], ModelAssert.Spans(q, V1));
        // The right half lost its first 25 frames, so its source starts at frame 165; the audio did not move but is
        // split at the frame with the video, so the right-hand pieces stay linked to each other.
        Assert.Equal(165 * F, ModelAssert.Media(q, ModelAssert.Track(q, V1).Clips[2].Id).SourceInFlicks);
        Assert.Equal([(0L, 40L), (40L, 60L)], ModelAssert.Spans(q, A1));
        Assert.Equal(100L, ModelAssert.Clip(q, "c2").Start);
        AssertRightPiecesShareOneLink(q);
    }

    [Fact]
    public void SplittingAFrozenClipKeepsTheFrameOnBothHalves()
    {
        var p = new ProjectBuilder().With(V1, V("f1", 10, 50, sourceFrame: 120, freeze: true)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Split(_ids, p, [V1], 30));
        var halves = ModelAssert.Track(q, V1).Clips.Cast<MediaClip>().ToList();
        Assert.Equal([(10L, 20L, 120 * F, true), (30L, 30L, 120 * F, true)], halves.Select(c => (c.Start, c.Duration, c.SourceInFlicks, c.Freeze)));
    }

    [Fact]
    public void AFrameAtTheClipStartDoesNotSplit()
    {
        var p = Scene();
        var r = TimelineEdits.FreezeFrame(_ids, p, "c1", 0, 10, ripple: true);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal([(0L, 10L), (10L, 100L), (110L, 50L)], ModelAssert.Spans(q, V1));
        var frozen = ModelAssert.Media(q, r.Select[0]);
        Assert.Equal((100 * F, true), (frozen.SourceInFlicks, frozen.Freeze));
        Assert.Equal(100 * F, ModelAssert.Media(q, "c1").SourceInFlicks);
        Assert.Equal(10L, ModelAssert.Clip(q, "c1").Start);
    }

    [Fact]
    public void TheHeldSourcePositionFollowsTheClipSpeedAndTheDurationIsAtLeastOneFrame()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, sourceFrame: 100, speed: 200)).Build();
        var r = TimelineEdits.FreezeFrame(_ids, p, "c1", 10, 0, ripple: true);
        var q = ModelAssert.Changed(p, r);
        var frozen = ModelAssert.Media(q, r.Select[0]);
        Assert.Equal((120 * F, 1L, 100), (frozen.SourceInFlicks, frozen.Duration, frozen.SpeedPercent));
        Assert.Equal([(0L, 10L), (10L, 1L), (11L, 40L)], ModelAssert.Spans(q, V1));
    }

    [Fact]
    public void TheHoldKeepsTheLookAndEnabledStateButNotTheFades()
    {
        var look = VideoProps.Default with { Opacity = 0.5, Color = ColorAdjust.Neutral with { Saturation = 0 }, FadeInFrames = 10, FadeOutFrames = 10 };
        var p = new ProjectBuilder().With(V1, V("c1", 0, 100, sourceFrame: 100) with { Video = look, Enabled = false }).Build();
        var r = TimelineEdits.FreezeFrame(_ids, p, "c1", 50, 20, ripple: true);
        var frozen = ModelAssert.Media(ModelAssert.Changed(p, r), r.Select[0]);
        Assert.Equal(look with { FadeInFrames = 0, FadeOutFrames = 0 }, frozen.Video);
        Assert.False(frozen.Enabled);
    }

    [Fact]
    public void InvalidTargetsWarnAndChangeNothing()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 100, sourceFrame: 100), V("s1", 100, 50, media: Still))
            .With(V2, V("c3", 0, 50))
            .With(A1, A("a1", 0, 100))
            .Lock(V2)
            .Build();

        Assert.Contains("Freeze frames apply to video clips only.", Unchanged(p, TimelineEdits.FreezeFrame(_ids, p, "a1", 10, 10, true)));
        Assert.Contains("V2 is locked.", Unchanged(p, TimelineEdits.FreezeFrame(_ids, p, "c3", 10, 10, true)));
        Assert.Contains("A still image is already a single frame.", Unchanged(p, TimelineEdits.FreezeFrame(_ids, p, "s1", 110, 10, true)));
        Assert.Contains("The frame to freeze must lie inside the clip.", Unchanged(p, TimelineEdits.FreezeFrame(_ids, p, "c1", 100, 10, true)));
        Assert.Contains("The frame to freeze must lie inside the clip.", Unchanged(p, TimelineEdits.FreezeFrame(_ids, p, "c1", -1, 10, true)));
        Assert.Contains("The clip no longer exists.", Unchanged(p, TimelineEdits.FreezeFrame(_ids, p, "c_gone", 0, 10, true)));
    }

    [Fact]
    public void UndoThroughTheSessionRestoresTheProject()
    {
        var p = Scene();
        var session = new ProjectSession(p, () => 0);
        var r = session.Execute(q => TimelineEdits.FreezeFrame(_ids, q, "c1", 40, 25, ripple: true));
        Assert.True(r.Changed(p));
        Assert.Equal(1, session.UndoCount);
        Assert.Equal("Freeze Frame", session.UndoLabel);
        Assert.True(session.Undo());
        Assert.Same(p, session.Current);
    }

    private static void AssertRightPiecesShareOneLink(Project q)
    {
        // Clip 1 of V1 is the hold (unlinked); the right-hand video and audio pieces form one new group.
        var left = ModelAssert.Clip(q, "c1");
        var rightVideo = ModelAssert.Track(q, V1).Clips[2];
        var rightAudio = ModelAssert.Track(q, A1).Clips[1];
        Assert.Equal("l1", left.LinkId);
        Assert.Equal("l1", ModelAssert.Clip(q, "a1").LinkId);
        Assert.NotNull(rightVideo.LinkId);
        Assert.NotEqual("l1", rightVideo.LinkId);
        Assert.Equal(rightVideo.LinkId, rightAudio.LinkId);
        Assert.Null(ModelAssert.Track(q, V1).Clips[1].LinkId);
    }

    private static System.Collections.Immutable.ImmutableArray<string> Unchanged(Project p, EditResult r)
    {
        ModelAssert.Unchanged(p, r);
        Assert.Equal("Freeze Frame", r.Label);
        return r.Warnings;
    }
}
