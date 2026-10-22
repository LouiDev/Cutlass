using Cutlass.App.ViewModels;
using Cutlass.Core.Session;
using Cutlass.Media.Preview;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App.Playback;

public sealed class PlaybackControllerTests
{
    private readonly FakePlaybackEngine _engine = new();
    private readonly TimelineViewModel _timeline;
    private readonly PlaybackController _controller;
    private bool _loop;

    public PlaybackControllerTests()
    {
        var project = new ProjectBuilder()
            .With(V1, V("v1", 0, 100, link: "l1"))
            .With(A1, A("a1", 0, 100, link: "l1"))
            .Build();
        var session = new ProjectSession(project, () => 0);
        _timeline = new TimelineViewModel(session, new SequentialIdGenerator(1000));
        _timeline.ViewState.SetViewport(1000, 400);
        _controller = new PlaybackController(_timeline, a => a(), _ => Task.CompletedTask)
        {
            SizeProvider = () => new PreviewSize(640, 360),
            UseProxies = () => false,
            Loop = () => _loop,
        };
        _controller.Attach(_engine);
    }

    [Fact]
    public void SpacePlaysFromThePlayheadAndParksOnTheShownFrame()
    {
        _timeline.Playhead = 10;
        Assert.True(_controller.TogglePlay());
        var options = Assert.Single(_engine.Starts);
        Assert.Equal(10, options.StartFrame);
        Assert.Equal(100, options.EndFrame);
        Assert.Equal(1, options.Speed);
        Assert.False(options.Loop);
        Assert.Equal(new PreviewSize(640, 360), options.Size);
        Assert.True(_controller.IsPlaying);

        _engine.ParkFrame = 37;
        Assert.False(_controller.TogglePlay());
        Assert.False(_controller.IsPlaying);
        Assert.Equal(37, _timeline.Playhead);
        Assert.Equal("Space", _controller.LastStopReason);
    }

    [Fact]
    public void SpaceAtTheEndStartsOver()
    {
        _timeline.Playhead = 100;
        _controller.TogglePlay();
        Assert.Equal(0, _engine.Starts[0].StartFrame);
    }

    [Fact]
    public void ShuttleGoesOneTwoFourAndKeepsTheClockFrame()
    {
        _controller.Shuttle();
        Assert.Equal(1, _controller.Speed);
        _engine.Position = 20;
        _controller.Shuttle();
        Assert.Equal(2, _engine.Starts[1].Speed);
        Assert.Equal(20, _engine.Starts[1].StartFrame);
        _engine.Position = 30;
        _controller.Shuttle();
        Assert.Equal(4, _controller.Speed);
        _controller.Shuttle();
        Assert.Equal(3, _engine.Starts.Count);

        _engine.ParkFrame = 44;
        _controller.Pause();
        Assert.False(_controller.IsPlaying);
        Assert.Equal(44, _timeline.Playhead);
        Assert.Equal("K", _controller.LastStopReason);
    }

    [Fact]
    public void TickFollowsTheClockAndHandsOutTheFrame()
    {
        _controller.TogglePlay();
        _engine.Position = 42;
        var frame = new VideoFrame(new byte[16], 2, 2, 8, 41, 0, pooled: false);
        _engine.Frames.Enqueue(frame);

        Assert.Same(frame, _controller.Tick(out _));
        Assert.Equal(42, _timeline.Playhead);
        Assert.Equal(41, _controller.LastFrame);
        Assert.True(_controller.IsPlaying);
        Assert.Null(_controller.Tick(out _));
    }

    [Fact]
    public void MovingThePlayheadStopsWithoutParking()
    {
        _controller.TogglePlay();
        _engine.ParkFrame = 5;
        _timeline.Playhead = 70;
        Assert.False(_controller.IsPlaying);
        Assert.Equal(70, _timeline.Playhead);
        Assert.Equal("the playhead was moved", _controller.LastStopReason);
    }

    [Fact]
    public void MuteAppliesLiveWithoutARestart()
    {
        _controller.TogglePlay();
        _timeline.ToggleTrackMuted(A1);
        var mixer = Assert.Single(_engine.Mixers);
        Assert.True(mixer.Find(A1)!.Muted);
        Assert.Single(_engine.Starts);
        Assert.True(_controller.IsPlaying);
    }

    [Fact]
    public void TimelineEditsRestartAtTheClockFrame()
    {
        _controller.TogglePlay();
        _engine.Position = 30;
        _timeline.ToggleTrackHidden(V1);
        Assert.Equal(2, _engine.Starts.Count);
        var restart = _engine.Starts[1];
        Assert.Equal(30, restart.StartFrame);
        Assert.Same(_timeline.Session.Current, restart.Snapshot);
        Assert.True(_controller.IsPlaying);
    }

    [Fact]
    public void TheEndStopsAndAStaleEndIsIgnored()
    {
        _controller.TogglePlay();
        _engine.RaiseReachedEnd();
        Assert.True(_controller.IsPlaying);

        _engine.IsPlaying = false;
        _engine.ParkFrame = 99;
        _engine.RaiseReachedEnd();
        Assert.False(_controller.IsPlaying);
        Assert.Equal(99, _timeline.Playhead);
        Assert.Equal("the end was reached", _controller.LastStopReason);
    }

    [Fact]
    public void AFailureStopsAndIsReported()
    {
        string? reported = null;
        _controller.Failed += (_, message) => reported = message;
        _controller.TogglePlay();
        _engine.RaiseFailed("ffmpeg exploded");
        Assert.False(_controller.IsPlaying);
        Assert.Equal("ffmpeg exploded", reported);
    }

    [Fact]
    public void LoopWithInAndOutPlaysTheRangeFromIn()
    {
        _timeline.InPoint = 20;
        _timeline.OutPoint = 50;
        _loop = true;
        _timeline.Playhead = 5;
        _controller.TogglePlay();
        var options = _engine.Starts[0];
        Assert.Equal(20, options.StartFrame);
        Assert.Equal(50, options.EndFrame);
        Assert.True(options.Loop);
        Assert.Equal(20, options.LoopStartFrame);
    }

    [Fact]
    public void ShiftSpacePlaysInToOut()
    {
        _timeline.InPoint = 10;
        _timeline.OutPoint = 60;
        _timeline.Playhead = 80;
        Assert.True(_controller.PlayInToOut());
        var options = _engine.Starts[0];
        Assert.Equal(10, options.StartFrame);
        Assert.Equal(60, options.EndFrame);
        Assert.False(options.Loop);
    }

    [Fact]
    public void WithoutAnEngineNothingPlays()
    {
        _controller.Attach(null);
        Assert.False(_controller.TogglePlay());
        Assert.False(_controller.IsPlaying);
        Assert.Null(_controller.Stop());
    }

    [Fact]
    public void ALostAudioDeviceIsReportedAndClearedByTheNextStartThatReopensIt()
    {
        var changes = 0;
        _controller.AudioStateChanged += (_, _) => changes++;
        Assert.True(_controller.TogglePlay());
        Assert.False(_controller.IsAudioUnavailable);

        // The device drops out during the run: the engine falls back to the silent clock and reports it.
        _engine.HasAudio = false;
        _engine.RaiseAudioUnavailable();
        Assert.True(_controller.IsAudioUnavailable);
        Assert.Equal(1, changes);

        // A repeated report changes nothing.
        _engine.RaiseAudioUnavailable();
        Assert.Equal(1, changes);

        // The next start reopens the device (Open), so the state is checked again after it.
        _controller.Stop();
        _engine.HasAudio = true;
        Assert.True(_controller.TogglePlay());
        Assert.False(_controller.IsAudioUnavailable);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void AStartOnASilentClockShowsTheAudioUnavailableState()
    {
        _engine.HasAudio = false;
        Assert.True(_controller.TogglePlay());
        Assert.True(_controller.IsAudioUnavailable);
    }
}
