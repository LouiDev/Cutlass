using Cutlass.App.Services;
using Cutlass.App.ViewModels;
using Cutlass.Core.Session;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App.Playback;

/// <summary>DESIGN 6: the viewer's "Audio unavailable" badge follows the engine's device state.</summary>
public sealed class ViewerAudioBadgeTests
{
    [Fact]
    public void TheViewerShowsTheAudioUnavailableBadgeWhileTheDeviceIsLost()
    {
        var project = new ProjectBuilder().With(V1, V("v1", 0, 100)).Build();
        var timeline = new TimelineViewModel(new ProjectSession(project, () => 0), new SequentialIdGenerator(1000));
        var viewer = new ViewerViewModel(timeline, new FramePresenter(), action => action());
        var engine = new FakePlaybackEngine();
        viewer.AttachPlayback(engine, null);
        Assert.False(viewer.IsAudioUnavailable);
        Assert.True(viewer.Playback.TogglePlay());

        engine.HasAudio = false;
        engine.RaiseAudioUnavailable();
        Assert.True(viewer.IsAudioUnavailable);

        viewer.Playback.Stop();
        engine.HasAudio = true;
        Assert.True(viewer.Playback.TogglePlay());
        Assert.False(viewer.IsAudioUnavailable);
    }
}
