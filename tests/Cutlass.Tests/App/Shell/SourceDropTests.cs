using Cutlass.App.Services;
using Cutlass.App.ViewModels;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App.Shell;

/// <summary>
/// Drags out of the Source monitor (DESIGN 4.3): the canvas drags video and audio, the "Video only" and "Audio only"
/// handles one stream. The payload travels as text and the timeline places it like a media bin drop.
/// </summary>
public sealed class SourceDropTests
{
    [Fact]
    [UseCulture("de-DE")]
    public void ThePayloadRoundTripsInInvariantText()
    {
        var drag = new SourceDrag("m_long", 1_234_567_890_123, 9_876_543_210_987, SourceStreams.Video);
        Assert.Equal("m_long\n1234567890123\n9876543210987\nVideo", drag.Format());
        Assert.True(SourceDrag.TryParse(drag.Format(), out var parsed));
        Assert.Equal(drag, parsed);

        var open = new SourceDrag("m_short", 0, null, SourceStreams.Both);
        Assert.True(SourceDrag.TryParse(open.Format(), out var parsedOpen));
        Assert.Equal(open, parsedOpen);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("m_long\n0\n\n")]
    [InlineData("\n0\n\nBoth")]
    [InlineData("m_long\nx\n\nBoth")]
    [InlineData("m_long\n0\n1.5\nBoth")]
    [InlineData("m_long\n0\n\nSubtitles")]
    [InlineData("m_long\n0\n\n7")]
    public void BadPayloadsAreRejected(string? text)
    {
        Assert.False(SourceDrag.TryParse(text, out var drag));
        Assert.Null(drag);
    }

    [Fact]
    public void TheViewerBuildsThePayloadFromSourceInAndOut()
    {
        var (_, viewer) = Viewer();
        Assert.Null(viewer.CreateSourceDrag(SourceStreams.Both));
        viewer.OpenSource(All.Single(m => m.Id == Long));
        viewer.Source.Seek(100);
        viewer.Source.MarkIn();
        viewer.Source.Seek(149);
        viewer.Source.MarkOut();
        Assert.Equal(new SourceDrag(Long, 100 * F, 150 * F, SourceStreams.Audio), viewer.CreateSourceDrag(SourceStreams.Audio));

        viewer.OpenSource(All.Single(m => m.Id == Music));
        Assert.False(viewer.Source.HasVideo);
        Assert.True(viewer.Source.HasAudio);
        Assert.Null(viewer.CreateSourceDrag(SourceStreams.Video));
        Assert.Equal(new SourceDrag(Music, 0, null, SourceStreams.Both), viewer.CreateSourceDrag(SourceStreams.Both));
    }

    [Fact]
    public void TheCanvasDragPlacesLinkedVideoAndAudio()
    {
        var (timeline, _) = Viewer();
        var drag = new SourceDrag(Long, 100 * F, 150 * F, SourceStreams.Both);
        var ghost = timeline.PreviewSourcePlacements(drag, V2);
        Assert.Equal([V2, A2], ghost.Select(p => p.TrackId));

        timeline.PlaceSource(drag, V2, 30, insert: false);
        var project = timeline.Session.Current;
        var v = (MediaClip)Assert.Single(TimelineQuery.FindTrack(project, V2)!.Clips);
        var a = (MediaClip)Assert.Single(TimelineQuery.FindTrack(project, A2)!.Clips);
        Assert.Equal((30, 50, 100 * F), (v.Start, v.Duration, v.SourceInFlicks));
        Assert.Equal((30, 50, 100 * F), (a.Start, a.Duration, a.SourceInFlicks));
        Assert.NotNull(v.LinkId);
        Assert.Equal(v.LinkId, a.LinkId);
        Assert.Equal(new HashSet<string> { v.Id, a.Id }, timeline.SelectedClipIds.ToHashSet());
        Assert.Equal(0, timeline.Playhead);
    }

    [Fact]
    public void VideoOnlyAndAudioOnlyPlaceOneStreamOnTheMatchingTrack()
    {
        var (timeline, _) = Viewer();
        // "Video only" dropped on A2 goes to V2; "Audio only" dropped on V1 goes to A1.
        timeline.PlaceSource(new SourceDrag(Short, 10 * F, null, SourceStreams.Video), A2, 0, insert: false);
        timeline.PlaceSource(new SourceDrag(Short, 0, 40 * F, SourceStreams.Audio), V1, 200, insert: false);
        var project = timeline.Session.Current;
        var v = (MediaClip)Assert.Single(TimelineQuery.FindTrack(project, V2)!.Clips);
        Assert.Equal((0, 90, StreamKind.Video), (v.Start, v.Duration, v.Stream));
        Assert.Null(v.LinkId);
        Assert.Empty(TimelineQuery.FindTrack(project, A2)!.Clips);
        var a = (MediaClip)Assert.Single(TimelineQuery.FindTrack(project, A1)!.Clips);
        Assert.Equal((200, 40, StreamKind.Audio), (a.Start, a.Duration, a.Stream));
        Assert.Null(a.LinkId);
        Assert.Empty(TimelineQuery.FindTrack(project, V1)!.Clips);
    }

    [Fact]
    public void AStreamTheMediaLacksIsNotAccepted()
    {
        var (timeline, _) = Viewer();
        var before = timeline.Session.Current;
        var drag = new SourceDrag(Still, 0, null, SourceStreams.Audio);
        Assert.Empty(timeline.PreviewSourcePlacements(drag, A1));
        timeline.PlaceSource(drag, A1, 0, insert: false);
        Assert.Same(before, timeline.Session.Current);
    }

    [Fact]
    public void ControlDropInsertsAndShiftsLaterClips()
    {
        var project = new ProjectBuilder().With(V1, V("c1", 0, 100, Long)).With(A1, A("a1", 0, 100, Long)).Build();
        var (timeline, _) = Viewer(project);
        timeline.PlaceSource(new SourceDrag(Short, 0, 20 * F, SourceStreams.Both), V1, 40, insert: true);
        Assert.Equal([(0L, 40L), (40L, 20L), (60L, 60L)], ModelAssert.Spans(timeline.Session.Current, V1));
        Assert.Equal(1, timeline.Session.UndoCount);
    }

    private static (TimelineViewModel Timeline, ViewerViewModel Viewer) Viewer(Project? project = null)
    {
        var session = new ProjectSession(project ?? new ProjectBuilder().Build(), () => 0);
        var timeline = new TimelineViewModel(session, new SequentialIdGenerator());
        var viewer = new ViewerViewModel(timeline, new FramePresenter(), action => action());
        return (timeline, viewer);
    }
}
