using Cutlass.App.Controls.Drag;
using Cutlass.App.Services.Playback;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.App.ViewModels.Viewer;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App.Shell;

/// <summary>
/// Source mode (DESIGN 4.3) and three-point edits (DESIGN 5.5): the one-clip Source sequence, Source In/Out rules,
/// the edit arguments handed to the timeline, match frame parking and the routing of seeks and edits.
/// </summary>
public sealed class SourceMonitorTests
{
    private static readonly SequenceSettings Settings = new(1920, 1080, FrameRate.Fps25);

    [Fact]
    public void TheSourceSequenceIsTheWholeMediaOnV1AndA1()
    {
        var project = SourceViewerViewModel.BuildProject(Media(Long), Settings, 125);
        Assert.Equal(new SequenceSettings(1920, 1080, FrameRate.Fps25), project.Sequence.Settings);
        Assert.Same(Media(Long), Assert.Single(project.Media));
        var v1 = Assert.Single(Track(project, "V1").Clips);
        var a1 = Assert.Single(Track(project, "A1").Clips);
        Assert.Equal((0, 2500), (v1.Start, v1.Duration));
        Assert.Equal((0, 2500), (a1.Start, a1.Duration));
        Assert.Equal(StreamKind.Video, ((MediaClip)v1).Stream);
        Assert.Equal(StreamKind.Audio, ((MediaClip)a1).Stream);
        Assert.NotNull(v1.LinkId);
        Assert.Equal(v1.LinkId, a1.LinkId);
        Assert.Empty(Track(project, "V2").Clips);
        Assert.True(project.Sequence.HasHadClips);
        Assert.Equal(2500, TimelineQuery.Duration(project));
    }

    [Fact]
    public void TheSourceSequenceUsesTheMediaSizeAndNearestRate()
    {
        var project = SourceViewerViewModel.BuildProject(Media(Uhd), Settings, 125);
        Assert.Equal(new SequenceSettings(3840, 2160, FrameRate.Fps2997), project.Sequence.Settings);
        // 10 s at 30000/1001: the frames whose start lies before the end, ceil(299.7).
        Assert.Equal(300, TimelineQuery.Duration(project));
    }

    [Fact]
    public void AudioOnlyMediaAndStillsUseTheFallback()
    {
        var music = SourceViewerViewModel.BuildProject(Media(Music), Settings, 125);
        Assert.Equal(Settings, music.Sequence.Settings);
        Assert.Empty(Track(music, "V1").Clips);
        Assert.Equal(1500, Assert.Single(Track(music, "A1").Clips).Duration);
        Assert.Null(Track(music, "A1").Clips[0].LinkId);

        var still = SourceViewerViewModel.BuildProject(Media(Still), Settings, 125);
        Assert.Equal(125, Assert.Single(Track(still, "V1").Clips).Duration);
        Assert.Empty(Track(still, "A1").Clips);
        Assert.Equal(FrameRate.Fps25, still.Sequence.Settings.Rate);
    }

    [Fact]
    public void KeptInAndOutAreClampedToTheMedia()
    {
        var project = SourceViewerViewModel.BuildProject(Media(Short), Settings, 125, inFrame: 40, outFrame: 500);
        Assert.Equal(40, project.Sequence.InFrame);
        Assert.Null(project.Sequence.OutFrame);
        var swapped = SourceViewerViewModel.BuildProject(Media(Short), Settings, 125, inFrame: 60, outFrame: 50);
        Assert.Equal(60, swapped.Sequence.InFrame);
        Assert.Null(swapped.Sequence.OutFrame);
    }

    [Theory]
    [InlineData(null, null, 0, null)]
    [InlineData(30L, 90L, 30, 90L)]
    [InlineData(30L, null, 30, null)]
    [InlineData(null, 90L, 0, 90L)]
    [InlineData(90L, 30L, 90, null)]
    [InlineData(-5L, 10L, 0, 10L)]
    public void ThreePointArgumentsAreFlicksOfTheSourceRange(long? inFrame, long? outFrame, long expectedIn, long? expectedOut)
    {
        var (inFlicks, outFlicks) = SourceViewerViewModel.EditArgumentsFor(inFrame, outFrame, FrameRate.Fps25);
        Assert.Equal(expectedIn * F, inFlicks);
        Assert.Equal(expectedOut * F, outFlicks);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(28_224_000, 1)]
    [InlineData(28_223_999, 0)]
    [InlineData(-1, 0)]
    [InlineData(1_058_400_000, 37)]
    public void MatchFrameParksOnTheFrameHoldingTheSourceTime(long flicks, long expected) =>
        Assert.Equal(expected, SourceViewerViewModel.FrameAtFlicks(flicks, FrameRate.Fps25));

    [Fact]
    public void InAndOutFollowTheDesignRules()
    {
        var source = Source();
        source.Open(Media(Long), Settings, 125);
        Assert.True(source.HasMedia);
        Assert.Equal(2500, source.Duration);
        Assert.Equal((0L, (long?)null), source.EditArguments());
        Assert.Equal(0, source.Playhead);

        source.Seek(100);
        source.MarkIn();
        source.Seek(199);
        source.MarkOut();
        // O includes the frame under the playhead: Out is exclusive.
        Assert.Equal((100L, 200L), (source.InPoint, source.OutPoint));
        Assert.Equal((100 * F, (long?)(200 * F)), source.EditArguments());
        Assert.Equal("Dur 00:00:04:00", source.RangeText);
        Assert.Equal("00:00:04:00", source.InText);
        Assert.Equal("00:00:07:24", source.OutText);

        // An In at or after the Out clears the Out; an Out at or before the In clears the In.
        source.Seek(250);
        source.MarkIn();
        Assert.Equal((250L, (long?)null), (source.InPoint, source.OutPoint));
        source.SetOut(50);
        Assert.Equal(((long?)null, 50L), (source.InPoint, source.OutPoint));
        source.SetIn(10);
        Assert.Equal((10L, 50L), (source.InPoint, source.OutPoint));

        source.ClearInOut();
        Assert.Equal(((long?)null, (long?)null), (source.InPoint, source.OutPoint));
        source.MarkAll();
        Assert.Equal((0L, 2500L), (source.InPoint, source.OutPoint));
    }

    [Fact]
    public void ThePlayheadStaysOnAFrameOfTheMedia()
    {
        var source = Source();
        source.Open(Media(Short), Settings, 125);
        source.Seek(5000);
        Assert.Equal(99, source.Playhead);
        source.Step(-200);
        Assert.Equal(0, source.Playhead);
        source.ScrubTo(0.5);
        Assert.Equal(50, source.Playhead);
        source.ScrubTo(1);
        Assert.Equal(99, source.Playhead);
        source.GoToEnd();
        Assert.Equal(99, source.Playhead);
        source.Seek(30);
        source.MarkOut();
        source.GoToStart();
        source.GoToOut();
        Assert.Equal(30, source.Playhead);
        source.GoToIn();
        Assert.Equal(0, source.Playhead);
    }

    [Fact]
    public void EachMediaRemembersItsMarksAndMatchFrameParks()
    {
        var source = Source();
        source.Open(Media(Long), Settings, 125);
        source.Seek(10);
        source.MarkIn();
        source.Seek(40);
        source.Open(Media(Short), Settings, 125);
        Assert.Null(source.InPoint);
        Assert.Equal(0, source.Playhead);
        source.Open(Media(Long), Settings, 125);
        Assert.Equal(10, source.InPoint);
        Assert.Equal(40, source.Playhead);

        // Match frame: 1.5 s of source time is frame 37 at 25 fps; the marks stay.
        source.Open(Media(Long), Settings, 125, parkFlicks: 1_058_400_000);
        Assert.Equal(37, source.Playhead);
        Assert.Equal(10, source.InPoint);
    }

    [Fact]
    public void RemovingTheMediaClosesTheSourceMonitor()
    {
        var source = Source();
        source.Open(Media(Long), Settings, 125);
        var project = new ProjectBuilder().Build();
        source.Sync(project, Settings, 125);
        Assert.True(source.HasMedia);
        var without = project with { Media = [.. project.Media.Where(m => m.Id != Long)] };
        source.Sync(without, Settings, 125);
        Assert.False(source.HasMedia);
        Assert.Equal(string.Empty, source.Name);
    }

    [Fact]
    public void InsertPlacesTheSourceRangeAtThePlayheadAndMovesIt()
    {
        // A locked V2 clip gives the sequence a length (so the playhead can sit at 50) and stays put on insert.
        var (timeline, viewer) = Viewer(new ProjectBuilder().With(ProjectBuilder.V2, V("bg", 0, 300, Long)).Lock(ProjectBuilder.V2).Build());
        timeline.Playhead = 50;
        viewer.OpenSource(Media(Long));
        Assert.True(viewer.IsSourceActive);
        viewer.Source.Seek(100);
        viewer.Source.MarkIn();
        viewer.Source.Seek(149);
        viewer.Source.MarkOut();

        Assert.True(viewer.EditFromSource(insert: true));

        var project = timeline.Session.Current;
        var v = (MediaClip)Assert.Single(project.Sequence.Tracks.Single(t => t.Id == ProjectBuilder.V1).Clips);
        var a = (MediaClip)Assert.Single(project.Sequence.Tracks.Single(t => t.Id == ProjectBuilder.A1).Clips);
        Assert.Equal((50, 50, 100 * F), (v.Start, v.Duration, v.SourceInFlicks));
        Assert.Equal((50, 50, 100 * F), (a.Start, a.Duration, a.SourceInFlicks));
        Assert.Equal(100, timeline.Playhead);
        // Source mode stays, so the next range can be marked and placed at once.
        Assert.True(viewer.IsSourceActive);
    }

    [Fact]
    public void OverwriteWithoutOutTakesTheRestOfTheMediaFromIn()
    {
        var (timeline, viewer) = Viewer();
        viewer.OpenSource(Media(Short));
        viewer.Source.Seek(60);
        viewer.Source.MarkIn();
        Assert.True(viewer.EditFromSource(insert: false));
        var clip = (MediaClip)Assert.Single(timeline.Session.Current.Sequence.Tracks.Single(t => t.Id == ProjectBuilder.V1).Clips);
        Assert.Equal((0, 40, 60 * F), (clip.Start, clip.Duration, clip.SourceInFlicks));
    }

    [Fact]
    public void WithoutSourceMarksTheTimelineRangeSetsTheDuration()
    {
        var (timeline, viewer) = Viewer(new ProjectBuilder().InOut(20, 45).Build());
        viewer.OpenSource(Media(Long));
        Assert.True(viewer.EditFromSource(insert: true));
        var clip = (MediaClip)Assert.Single(timeline.Session.Current.Sequence.Tracks.Single(t => t.Id == ProjectBuilder.V1).Clips);
        // Placed at the timeline In, as long as the timeline range.
        Assert.Equal((20, 25, 0L), (clip.Start, clip.Duration, clip.SourceInFlicks));
    }

    [Fact]
    public void NothingInSourceMeansNoSourceEdit()
    {
        var (timeline, viewer) = Viewer();
        var before = timeline.Session.Current;
        Assert.False(viewer.EditFromSource(insert: true));
        Assert.Same(before, timeline.Session.Current);
    }

    [Fact]
    public void MatchFrameOpensTheClipUnderThePlayheadAtItsSourceFrame()
    {
        var project = new ProjectBuilder().With(ProjectBuilder.V1, V("c1", 0, 100, Long, sourceFrame: 40)).Build();
        var (timeline, viewer) = Viewer(project);
        timeline.Playhead = 10;
        var args = Assert.IsType<MatchFrameEventArgs>(timeline.FindMatchFrame());
        viewer.OpenSource(TimelineQuery.FindMedia(project, args.AssetId)!, args.SourceFlicks);
        Assert.Equal(Long, viewer.Source.Asset!.Id);
        Assert.Equal(50, viewer.Source.Playhead);
    }

    [Fact]
    public void TypedTimecodesSeekTheMonitorShown()
    {
        var project = new ProjectBuilder().With(ProjectBuilder.V1, V("c1", 0, 200, Long)).Build();
        var (timeline, viewer) = Viewer(project);
        timeline.Playhead = 10;

        viewer.BeginTimecodeEdit();
        Assert.True(viewer.IsEditingTimecode);
        Assert.Equal("00:00:00:10", viewer.TimecodeEditText);
        viewer.TimecodeEditText = "+15";
        Assert.True(viewer.CommitTimecodeEdit());
        Assert.False(viewer.IsEditingTimecode);
        Assert.Equal(25, timeline.Playhead);

        viewer.BeginTimecodeEdit();
        viewer.TimecodeEditText = "nonsense";
        Assert.False(viewer.CommitTimecodeEdit());
        Assert.Equal(25, timeline.Playhead);
        Assert.False(viewer.CancelTimecodeEdit());

        viewer.OpenSource(Media(Long));
        viewer.BeginTimecodeEdit();
        viewer.TimecodeEditText = "00:00:02:00";
        Assert.True(viewer.CancelTimecodeEdit());
        Assert.Equal(0, viewer.Source.Playhead);
        Assert.True(viewer.SeekToText("00:00:02:00"));
        Assert.Equal(50, viewer.Source.Playhead);
        Assert.Equal(25, timeline.Playhead);
        Assert.Equal("00:00:02:00", viewer.TimecodeText);
        Assert.Equal("00:01:40:00", viewer.DurationText);

        viewer.ShowTimeline();
        Assert.False(viewer.IsSourceActive);
        Assert.Equal("00:00:01:00", viewer.TimecodeText);
    }

    [Fact]
    public void TheSourceButtonNeedsMedia()
    {
        var (_, viewer) = Viewer();
        Assert.False(viewer.CanShowSource);
        viewer.SetModeCommand.Execute(ViewerMode.Source);
        Assert.Equal(ViewerMode.Timeline, viewer.Mode);
        viewer.OpenSource(Media(Short));
        Assert.True(viewer.CanShowSource);
        Assert.Equal(ViewerMode.Source, viewer.Mode);
        Assert.Equal(viewer.Source.Playback, viewer.ActivePlayback);
    }

    [Fact]
    public void RollAndSlipHintsShowATwoUp()
    {
        var project = new ProjectBuilder().With(ProjectBuilder.V1, V("c1", 0, 100, Long), V("c2", 100, 100, Short)).Build();
        var (timeline, viewer) = Viewer(project);
        timeline.Playhead = 50;
        timeline.DragHint = new ViewerHint(99, 100);
        Assert.True(viewer.IsTwoUp);
        Assert.Equal(99, viewer.RequestedFrame);
        timeline.DragHint = new ViewerHint(40);
        Assert.False(viewer.IsTwoUp);
        Assert.Equal(40, viewer.RequestedFrame);
        timeline.DragHint = null;
        Assert.False(viewer.IsTwoUp);
        Assert.Equal(50, viewer.RequestedFrame);
    }

    private static MediaAsset Media(string id) => All.Single(m => m.Id == id);

    private static Track Track(Project project, string name) => project.Sequence.Tracks.Single(t => t.Name == name);

    private static SourceViewerViewModel Source() => new(action => action());

    private static (TimelineViewModel Timeline, ViewerViewModel Viewer) Viewer(Project? project = null)
    {
        var session = new ProjectSession(project ?? new ProjectBuilder().Build(), () => 0);
        var timeline = new TimelineViewModel(session, new SequentialIdGenerator());
        var viewer = new ViewerViewModel(timeline, new FramePresenter(), action => action());
        return (timeline, viewer);
    }
}
