using System.ComponentModel;
using Cutlass.App.Services.Playback;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Session;
using Cutlass.Media.Preview;
using Cutlass.Media.Preview.Audio;
using Cutlass.Media.Preview.Playback;

namespace Cutlass.App.ViewModels.Viewer;

/// <summary>
/// The transport (DESIGN 6 and 7, TECH_BRIEF 8.2) on top of an <see cref="IPlaybackEngine"/>: Space, Shift+Space, the
/// L shuttle (1x, 2x, 4x), K, parking the playhead on the exact frame shown when playback stops, following the clock
/// with the playhead, live mixer changes and the restart after 150 ms of edit quiet. Every member runs on the UI thread;
/// engine events are marshalled through the post callback. It touches no controls and is unit tested with a fake
/// engine.
/// </summary>
public sealed class PlaybackController
{
    /// <summary>How long timeline edits must be quiet before playback restarts on the new project (DESIGN 6).</summary>
    public static readonly TimeSpan RestartQuiet = TimeSpan.FromMilliseconds(150);

    private readonly TimelineViewModel _timeline;
    private readonly Action<Action> _post;
    private readonly Func<TimeSpan, Task> _delay;
    private IPlaybackEngine? _engine;
    private PlaybackOptions? _options;
    private bool _settingPlayhead;
    private long _restartVersion;

    /// <summary>Creates the controller.</summary>
    /// <param name="timeline">The timeline (playhead, In/Out, session).</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    /// <param name="delay">Waits (tests pass an immediate one); defaults to <see cref="Task.Delay(TimeSpan)"/>.</param>
    public PlaybackController(TimelineViewModel timeline, Action<Action> post, Func<TimeSpan, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(post);
        _timeline = timeline;
        _post = post;
        _delay = delay ?? Task.Delay;
        timeline.PropertyChanged += OnTimelinePropertyChanged;
        timeline.Session.Changed += OnSessionChanged;
    }

    /// <summary>Raised when <see cref="IsPlaying"/> or <see cref="Speed"/> changed.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised when a run failed (playback has stopped).</summary>
    public event EventHandler<string>? Failed;

    /// <summary>Raised on the UI thread when <see cref="IsAudioUnavailable"/> changed.</summary>
    public event EventHandler? AudioStateChanged;

    /// <summary>The preview size for a run (the viewer's quality, capped while playing).</summary>
    public Func<PreviewSize> SizeProvider { get; set; } = () => new PreviewSize(960, 540);

    /// <summary>True to read video from proxies.</summary>
    public Func<bool> UseProxies { get; set; } = () => true;

    /// <summary>True when loop is on.</summary>
    public Func<bool> Loop { get; set; } = () => false;

    /// <summary>The engine, or null while ffmpeg is unavailable.</summary>
    public IPlaybackEngine? Engine => _engine;

    /// <summary>True while playing.</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>The speed of the current or last run.</summary>
    public int Speed { get; private set; } = 1;

    /// <summary>
    /// True when the engine plays on a silent clock because the audio output is unavailable (the device could not be
    /// opened or was disconnected during a run). Checked after every start, since a start may reopen the device, and
    /// when the engine reports a lost device.
    /// </summary>
    public bool IsAudioUnavailable { get; private set; }

    /// <summary>The options of the current or last run.</summary>
    public PlaybackOptions? Options => _options;

    /// <summary>Why playback stopped last time, for example "K" or "the end was reached", or null.</summary>
    public string? LastStopReason { get; private set; }

    /// <summary>The timeline frame of the last frame <see cref="Tick"/> handed out, or -1.</summary>
    public long LastFrame { get; private set; } = -1;

    /// <summary>Uses an engine (null while ffmpeg is unavailable); stops playback on the old one.</summary>
    /// <param name="engine">The engine.</param>
    public void Attach(IPlaybackEngine? engine)
    {
        if (ReferenceEquals(engine, _engine))
        {
            return;
        }

        Stop(reason: "the engine changed");
        if (_engine is { } old)
        {
            old.ReachedEnd -= OnReachedEnd;
            old.Failed -= OnFailed;
            old.AudioUnavailable -= OnAudioUnavailable;
        }

        _engine = engine;
        if (engine is not null)
        {
            engine.ReachedEnd += OnReachedEnd;
            engine.Failed += OnFailed;
            engine.AudioUnavailable += OnAudioUnavailable;
        }

        UpdateAudioState();
    }

    /// <summary>Space: stops when playing, else plays from the playhead at 1x.</summary>
    /// <returns>True when playback runs afterwards.</returns>
    public bool TogglePlay()
    {
        if (IsPlaying)
        {
            Stop(reason: "Space");
            return false;
        }

        var range = PlaybackPlanner.ForPlayhead(_timeline.Playhead, _timeline.SequenceEnd, _timeline.InPoint, _timeline.OutPoint, Loop());
        return range is { } r && Start(r, 1);
    }

    /// <summary>Shift+Space: plays In to Out at 1x.</summary>
    /// <returns>True when playback started.</returns>
    public bool PlayInToOut()
    {
        Stop(reason: "Shift+Space restarts In to Out");
        var range = PlaybackPlanner.ForInToOut(_timeline.SequenceEnd, _timeline.InPoint, _timeline.OutPoint, Loop());
        return range is { } r && Start(r, 1);
    }

    /// <summary>L: plays at 1x, then 2x, then 4x (DESIGN 6).</summary>
    /// <returns>True when playback runs afterwards.</returns>
    public bool Shuttle()
    {
        if (!IsPlaying || _engine is null)
        {
            var range = PlaybackPlanner.ForPlayhead(_timeline.Playhead, _timeline.SequenceEnd, _timeline.InPoint, _timeline.OutPoint, Loop());
            return range is { } r && Start(r, 1);
        }

        var next = PlaybackPlanner.NextShuttleSpeed(true, Speed);
        return next == Speed || StartFrom(_engine.Position, next);
    }

    /// <summary>K: stops and parks the playhead.</summary>
    public void Pause() => Stop();

    /// <summary>
    /// Stops playback without blocking. With <paramref name="park"/> the playhead moves to the exact frame that was
    /// shown last (TECH_BRIEF 8.2).
    /// </summary>
    /// <param name="park">True to move the playhead to the parked frame.</param>
    /// <param name="reason">Why playback stopped (diagnostics and the smoke log).</param>
    /// <returns>The parked frame, or null when nothing was playing.</returns>
    public long? Stop(bool park = true, string reason = "K")
    {
        if (!IsPlaying || _engine is null)
        {
            return null;
        }

        _restartVersion++;
        var parked = _engine.Stop();
        LastStopReason = reason;
        IsPlaying = false;
        if (park)
        {
            SetPlayhead(parked);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        return parked;
    }

    /// <summary>Restarts at the clock frame with the current project, size and proxy setting (quality or proxy toggle).</summary>
    public void RestartNow()
    {
        if (IsPlaying && _engine is not null)
        {
            StartFrom(_engine.Position, Speed);
        }
    }

    /// <summary>
    /// Called on every animation frame while playing (UI thread): moves the playhead to the clock frame (the timeline
    /// pages along) and returns the newest frame due, which the caller shows and returns to its pool.
    /// </summary>
    /// <param name="dropped">Frames skipped by this call.</param>
    /// <returns>The frame to show, or null to keep the current one.</returns>
    public VideoFrame? Tick(out int dropped)
    {
        dropped = 0;
        if (!IsPlaying || _engine is not { } engine)
        {
            return null;
        }

        var position = engine.Position;
        SetPlayhead(position);
        if (engine.TryTakeFrame(position, out var frame, out dropped) && frame is not null)
        {
            LastFrame = frame.TimelineFrame;
            return frame;
        }

        return null;
    }

    /// <summary>The peaks since the last call, or silence when stopped.</summary>
    /// <returns>The peaks.</returns>
    public MixerPeaks ReadPeaks() => IsPlaying && _engine is { } engine ? engine.ReadAndResetPeaks() : MixerPeaks.Silent;

    /// <summary>Health of the current run.</summary>
    /// <returns>The stats, or empty when stopped.</returns>
    public PlaybackStats Stats() => IsPlaying && _engine is { } engine ? engine.Stats : PlaybackStats.Empty;

    private bool Start(PlayRange range, int speed)
    {
        if (_engine is not { } engine)
        {
            return false;
        }

        var project = _timeline.Session.Current;
        var options = new PlaybackOptions(project, range.Start, range.End, speed, SizeProvider(), UseProxies(), MixerState.FromSequence(project.Sequence))
        {
            Loop = range.Loop,
            LoopStartFrame = range.LoopStart,
        };
        return Run(engine, options);
    }

    private bool StartFrom(long frame, int speed)
    {
        if (_engine is not { } engine || _options is not { } last)
        {
            return false;
        }

        var project = _timeline.Session.Current;
        var duration = TimelineQuery.Duration(project);

        // A run to the sequence end follows the new end; a run to an Out point keeps it.
        var end = last.EndFrame >= TimelineQuery.Duration(last.Snapshot) ? duration : Math.Min(last.EndFrame, duration);
        if (frame >= end && last.Loop)
        {
            frame = last.LoopStartFrame ?? last.StartFrame;
        }

        if (frame < 0 || frame >= end)
        {
            Stop(reason: "the range ended");
            return false;
        }

        var options = last with
        {
            Snapshot = project,
            StartFrame = frame,
            EndFrame = end,
            Speed = speed,
            Size = SizeProvider(),
            UseProxies = UseProxies(),
            Mixer = MixerState.FromSequence(project.Sequence),
        };
        return Run(engine, options);
    }

    private bool Run(IPlaybackEngine engine, PlaybackOptions options)
    {
        try
        {
            engine.Start(options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            if (IsPlaying)
            {
                IsPlaying = false;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }

            Failed?.Invoke(this, ex.Message);
            return false;
        }

        _restartVersion++;
        _options = options;
        Speed = options.Speed;
        IsPlaying = true;
        SetPlayhead(options.StartFrame);
        UpdateAudioState();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // The engine's device state after a start (Open may have reopened a lost device) or a loss report.
    private void UpdateAudioState()
    {
        var unavailable = _engine is { HasAudio: false };
        if (unavailable != IsAudioUnavailable)
        {
            IsAudioUnavailable = unavailable;
            AudioStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetPlayhead(long frame)
    {
        _settingPlayhead = true;
        try
        {
            _timeline.Playhead = frame;
        }
        finally
        {
            _settingPlayhead = false;
        }
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The user moved the playhead (ruler click, arrow keys, typed timecode): stop where the user put it.
        if (e.PropertyName == nameof(TimelineViewModel.Playhead) && IsPlaying && !_settingPlayhead)
        {
            Stop(park: false, reason: "the playhead was moved");
        }
    }

    private void OnSessionChanged(object? sender, ProjectChangedEventArgs e)
    {
        if (!IsPlaying || _engine is not { } engine || _options is not { } options || e.Kind == ChangeKind.Saved)
        {
            return;
        }

        // Mute, solo and gain apply live (TECH_BRIEF 8.2); anything the render sees restarts after 150 ms of quiet.
        engine.SetMixer(MixerState.FromSequence(e.New.Sequence));
        if (!PlaybackPlanner.IsRenderUnchanged(options.Snapshot, e.New))
        {
            _ = RestartAfterQuietAsync(++_restartVersion);
        }
    }

    private async Task RestartAfterQuietAsync(long version)
    {
        // Every gesture update is a change too, so a running drag keeps postponing the restart.
        await _delay(RestartQuiet);
        if (version == _restartVersion && IsPlaying && _engine is { } engine)
        {
            StartFrom(engine.Position, Speed);
        }
    }

    private void OnReachedEnd() => _post(() =>
    {
        // A stale event from a run that was already replaced is ignored.
        if (IsPlaying && _engine is { IsPlaying: false })
        {
            Stop(reason: "the end was reached");
        }
    });

    private void OnAudioUnavailable() => _post(UpdateAudioState);

    private void OnFailed(string message) => _post(() =>
    {
        if (!IsPlaying)
        {
            return;
        }

        Stop(reason: "playback failed: " + message);
        Failed?.Invoke(this, message);
    });
}
