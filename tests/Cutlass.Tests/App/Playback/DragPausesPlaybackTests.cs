using Cutlass.App.Services.Playback;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.App.ViewModels.Viewer;
using Cutlass.Core.Session;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App.Playback;

/// <summary>DESIGN 5.6: starting a timeline drag pauses playback.</summary>
public sealed class DragPausesPlaybackTests
{
    [Fact]
    public void ADragStartStopsPlaybackAndParks()
    {
        var project = new ProjectBuilder().With(V1, V("v1", 0, 100)).Build();
        var timeline = new TimelineViewModel(new ProjectSession(project, () => 0), new SequentialIdGenerator(1000));
        var viewer = new ViewerViewModel(timeline, new FramePresenter(), action => action());
        var engine = new FakePlaybackEngine();
        viewer.AttachPlayback(engine, null);
        Assert.True(viewer.Playback.TogglePlay());

        engine.ParkFrame = 42;
        timeline.NotifyDragStarted();
        Assert.False(viewer.Playback.IsPlaying);
        Assert.Equal(42, timeline.Playhead);
        Assert.Equal("a timeline drag started", viewer.Playback.LastStopReason);

        // Without playback a drag start changes nothing.
        timeline.NotifyDragStarted();
        Assert.Equal(42, timeline.Playhead);
    }
}
