using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

public sealed class PlaceTests
{
    private readonly SequentialIdGenerator _ids = new(1000);

    private static ClipPlacement P(string track, Clip clip) => new(track, clip);

    [Fact]
    public void OverwriteIntoEmptySpaceAssignsNewIdsAndSelectsThem()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50)).Build();
        var r = TimelineEdits.Place(_ids, p, [P(V1, V("x", 0, 30, sourceFrame: 10))], 100, EditMode.Overwrite);
        var q = ModelAssert.Changed(p, r);

        Assert.Equal("Overwrite", r.Label);
        Assert.Equal([("c1", 0L, 50L), ("c_00001000", 100L, 30L)], ModelAssert.Layout(q, V1));
        Assert.Equal(["c_00001000"], r.Select.ToArray());
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void PartialOverwriteTrimsTheCoveredClips()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50), V("c2", 50, 50, sourceFrame: 100)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, [P(V1, Title("t", 0, 20))], 40, EditMode.Overwrite));

        Assert.Equal([("c1", 0L, 40L), ("c_00001000", 40L, 20L), ("c2", 60L, 40L)], ModelAssert.Layout(q, V1));
        Assert.Equal(110 * F, ModelAssert.Media(q, "c2").SourceInFlicks);
        Assert.Equal(0, ModelAssert.Media(q, "c1").SourceInFlicks);
    }

    [Fact]
    public void OverwriteInsideAClipSplitsIt()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 100, link: "l1")).With(A1, A("a1", 0, 100, link: "l1")).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, [P(V1, Title("t", 0, 10))], 30, EditMode.Overwrite));

        var layout = ModelAssert.Layout(q, V1);
        Assert.Equal(3, layout.Length);
        Assert.Equal(("c1", 0L, 30L), layout[0]);
        Assert.Equal((40L, 60L), (layout[2].Start, layout[2].Duration));
        var right = ModelAssert.Media(q, layout[2].Id);
        Assert.Equal(40 * F, right.SourceInFlicks);
        Assert.NotEqual("l1", right.LinkId);
        Assert.Equal("l1", ModelAssert.Clip(q, "c1").LinkId);
        Assert.Equal([("a1", 0L, 100L)], ModelAssert.Layout(q, A1));
    }

    [Fact]
    public void FullOverwriteRemovesCoveredClips()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 10, 20), V("c2", 30, 20, sourceFrame: 100)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, [P(V1, Title("t", 0, 40))], 10, EditMode.Overwrite));
        Assert.Single(ModelAssert.Track(q, V1).Clips);
    }

    [Fact]
    public void InsertMovesLaterClipsOnAllUnlockedTracksAndSplitsStraddlers()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50), V("c2", 50, 50, sourceFrame: 100))
            .With(A1, A("a1", 0, 100))
            .With(A2, A("a2", 60, 10, media: Music))
            .Lock(A2)
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, [P(V1, Title("t", 0, 20))], 30, EditMode.Insert));

        Assert.Equal([(0L, 30L), (30L, 20L), (50L, 20L), (70L, 50L)], ModelAssert.Spans(q, V1));
        Assert.Equal([(0L, 30L), (50L, 70L)], ModelAssert.Spans(q, A1));
        Assert.Equal([(60L, 10L)], ModelAssert.Spans(q, A2));
        var a1Right = ModelAssert.Media(q, ModelAssert.Track(q, A1).Clips[1].Id);
        Assert.Equal(30 * F, a1Right.SourceInFlicks);
    }

    [Fact]
    public void InsertWithALockedTargetSkipsThatPlacement()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50)).Lock(V1).Build();
        var r = TimelineEdits.Place(_ids, p, [P(V1, Title("t", 0, 20))], 10, EditMode.Insert);
        ModelAssert.Unchanged(p, r);
        Assert.Contains("V1 is locked.", r.Warnings);
    }

    [Fact]
    public void InsertingInsideATransitionRemovesIt()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, [P(V2, Title("t", 0, 5))], 48, EditMode.Insert));
        Assert.Empty(ModelAssert.Track(q, V1).Transitions);
    }

    [Fact]
    public void InsertAfterATransitionKeepsIt()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, [P(V2, Title("t", 0, 5))], 80, EditMode.Insert));
        Assert.Single(ModelAssert.Track(q, V1).Transitions);
    }

    [Fact]
    public void PlacedLinkGroupsGetFreshIds()
    {
        var p = new ProjectBuilder().Build();
        var placements = TimelineEdits.SourcePlacements(p, Long, 0, 40, V1, A1);
        var q = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, placements, 0, EditMode.Overwrite));

        var v = ModelAssert.Track(q, V1).Clips[0];
        var a = ModelAssert.Track(q, A1).Clips[0];
        Assert.NotNull(v.LinkId);
        Assert.Equal(v.LinkId, a.LinkId);
        Assert.StartsWith("l_", v.LinkId);
        Assert.NotEqual("l_source", v.LinkId);
    }

    [Fact]
    public void ASingleMemberOfAPlaceholderGroupIsNotLinked()
    {
        var p = new ProjectBuilder().Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, [P(V1, V("x", 0, 10, link: "l_x"))], 0, EditMode.Overwrite));
        Assert.Null(ModelAssert.Track(q, V1).Clips[0].LinkId);
    }

    [Fact]
    public void DurationClampsToTheSource()
    {
        var p = new ProjectBuilder().Build();
        var r = TimelineEdits.Place(_ids, p, [P(V1, V("x", 0, 500, media: Short, sourceFrame: 60))], 0, EditMode.Overwrite);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal(40, ModelAssert.Track(q, V1).Clips[0].Duration);
        Assert.Contains(r.Warnings, w => w.StartsWith("Shortened to 40 frames", StringComparison.Ordinal));
    }

    [Fact]
    public void WrongKindOrMissingTrackIsSkippedWithAWarning()
    {
        var p = new ProjectBuilder().Build();
        var r = TimelineEdits.Place(_ids, p, [P(A1, V("x", 0, 10)), P("tr_gone", Title("t", 0, 10)), P(A1, Title("t2", 0, 10))], 0, EditMode.Overwrite);
        ModelAssert.Unchanged(p, r);
        Assert.Equal(3, r.Warnings.Length);
    }

    [Fact]
    public void NegativePlacementFrameClampsToZero()
    {
        var p = new ProjectBuilder().Build();
        var r = TimelineEdits.Place(_ids, p, [P(V1, Title("t", 0, 10))], -5, EditMode.Overwrite);
        Assert.Equal(0, ModelAssert.Track(ModelAssert.Changed(p, r), V1).Clips[0].Start);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void EmptyPlacementReturnsTheSameReference()
    {
        var p = new ProjectBuilder().Build();
        ModelAssert.Unchanged(p, TimelineEdits.Place(_ids, p, [], 0, EditMode.Insert));
    }

    [Fact]
    [UseCulture("de-DE")]
    public void FirstClipInANeverUsedSequenceAdoptsItsFormat()
    {
        var p = new ProjectBuilder().Fresh().Marker("k1", 50).InOut(25, 75).Build();
        var placements = TimelineEdits.SourcePlacements(p, Uhd, 0, 50, V1, A1);       // 2 s at 25 fps
        var r = TimelineEdits.Place(_ids, p, placements, 25, EditMode.Overwrite);      // at 1 s
        var q = ModelAssert.Changed(p, r);

        Assert.Equal(new SequenceSettings(3840, 2160, FrameRate.Fps2997), q.Sequence.Settings);
        Assert.True(q.Sequence.HasHadClips);
        Assert.Contains(r.Warnings, w => w == "Sequence set to 3840x2160 at 29.97 fps to match uhd.mp4.");
        Assert.Equal([(30L, 60L)], ModelAssert.Spans(q, V1));
        Assert.Equal(60, q.Sequence.Markers[0].Frame);
        Assert.Equal(30, q.Sequence.InFrame);
        Assert.Equal(90, q.Sequence.OutFrame);
    }

    [Fact]
    public void LaterClipsDoNotChangeTheFormat()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 10)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, TimelineEdits.SourcePlacements(p, Uhd, 0, 50, V2, null), 0, EditMode.Overwrite));
        Assert.Equal(p.Sequence.Settings, q.Sequence.Settings);
    }

    [Fact]
    public void AudioOnlyFirstClipKeepsTheFormatButMarksTheSequenceUsed()
    {
        var p = new ProjectBuilder().Fresh().Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, TimelineEdits.SourcePlacements(p, Music, 0, 50, V1, A1), 0, EditMode.Overwrite));
        Assert.Equal(p.Sequence.Settings, q.Sequence.Settings);
        Assert.True(q.Sequence.HasHadClips);
        Assert.Empty(ModelAssert.Track(q, V1).Clips);
    }

    [Fact]
    public void SourcePlacementsSkipMissingStreamsAndZeroStillSource()
    {
        var p = new ProjectBuilder().Build();
        var still = TimelineEdits.SourcePlacements(p, Still, 99 * F, 125, V1, A1);
        Assert.Single(still);
        Assert.Equal(0, ((MediaClip)still[0].Clip).SourceInFlicks);
        Assert.Null(still[0].Clip.LinkId);
        Assert.Empty(TimelineEdits.SourcePlacements(p, "m_gone", 0, 10, V1, A1));
    }

    [Fact]
    public void ThreePointEditUsesSourceRangeAtTimelineIn()
    {
        var p = new ProjectBuilder().InOut(200, null).Build();
        var r = TimelineEdits.SourceEdit(_ids, p, Long, 100 * F, 150 * F, 7, EditMode.Overwrite, 125);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal([(200L, 50L)], ModelAssert.Spans(q, V1));
        Assert.Equal([(200L, 50L)], ModelAssert.Spans(q, A1));
        Assert.Equal(100 * F, ModelAssert.Media(q, ModelAssert.Track(q, V1).Clips[0].Id).SourceInFlicks);
    }

    [Fact]
    public void ThreePointEditUsesTheTimelineRangeWithoutSourceMarks()
    {
        var p = new ProjectBuilder().InOut(20, 70).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SourceEdit(_ids, p, Long, null, null, 0, EditMode.Insert, 125));
        Assert.Equal([(20L, 50L)], ModelAssert.Spans(q, V1));
    }

    [Fact]
    public void ThreePointEditWithoutMarksPlacesTheWholeMediaAtThePlayhead()
    {
        var p = new ProjectBuilder().Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SourceEdit(_ids, p, Short, null, null, 33, EditMode.Overwrite, 125));
        Assert.Equal([(33L, 100L)], ModelAssert.Spans(q, V1));
        var still = ModelAssert.Changed(p, TimelineEdits.SourceEdit(_ids, p, Still, null, null, 0, EditMode.Overwrite, 125));
        Assert.Equal([(0L, 125L)], ModelAssert.Spans(still, V1));
        Assert.Empty(ModelAssert.Track(still, A1).Clips);
    }

    [Fact]
    public void ThreePointEditUsesTheTopmostTargetedUnlockedTracks()
    {
        var p = new ProjectBuilder().Target(V1, V2, A1, A2).Lock(A1).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SourceEdit(_ids, p, Short, null, null, 0, EditMode.Overwrite, 125));
        Assert.Single(ModelAssert.Track(q, V2).Clips);
        Assert.Empty(ModelAssert.Track(q, V1).Clips);
        Assert.Single(ModelAssert.Track(q, A2).Clips);
    }

    [Fact]
    public void ThreePointEditWithoutTargetsWarns()
    {
        var p = new ProjectBuilder().Target().Build();
        var r = TimelineEdits.SourceEdit(_ids, p, Short, null, null, 0, EditMode.Overwrite, 125);
        ModelAssert.Unchanged(p, r);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void SourceRangeFramesRoundsTheRangeUpAndTakesTheRestWithoutAnOut()
    {
        var p = new ProjectBuilder().Build();
        Assert.Equal(50, TimelineEdits.SourceRangeFrames(p, Long, 100 * F, 150 * F, 125));
        Assert.Equal(51, TimelineEdits.SourceRangeFrames(p, Long, 100 * F, (150 * F) + 1, 125));
        Assert.Equal(70, TimelineEdits.SourceRangeFrames(p, Short, 30 * F, null, 125));
        Assert.Equal(100, TimelineEdits.SourceRangeFrames(p, Short, -5, null, 125));
        Assert.Equal(125, TimelineEdits.SourceRangeFrames(p, Still, 99 * F, null, 125));
        Assert.Equal(0, TimelineEdits.SourceRangeFrames(p, "m_gone", 0, null, 125));
    }

    [Fact]
    public void SourcePlacementsWithOneTargetPlaceOneUnlinkedStream()
    {
        // The Source monitor's "Video only" and "Audio only" drags (DESIGN 4.3) pass a single track.
        var p = new ProjectBuilder().Build();
        var video = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, TimelineEdits.SourcePlacements(p, Short, 10 * F, 40, V1, null), 5, EditMode.Overwrite));
        Assert.Equal([(5L, 40L)], ModelAssert.Spans(video, V1));
        Assert.Empty(ModelAssert.Track(video, A1).Clips);
        Assert.Null(ModelAssert.Track(video, V1).Clips[0].LinkId);
        Assert.Equal(10 * F, ModelAssert.Media(video, ModelAssert.Track(video, V1).Clips[0].Id).SourceInFlicks);

        var audio = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, TimelineEdits.SourcePlacements(p, Short, 10 * F, 40, null, A1), 5, EditMode.Overwrite));
        Assert.Empty(ModelAssert.Track(audio, V1).Clips);
        Assert.Equal([(5L, 40L)], ModelAssert.Spans(audio, A1));
        Assert.Null(ModelAssert.Track(audio, A1).Clips[0].LinkId);
    }
}
