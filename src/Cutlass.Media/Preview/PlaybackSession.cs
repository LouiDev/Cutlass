using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Media.Audio;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;

namespace Cutlass.Media.Preview;

/// <summary>
/// Real-time playback (TECH_BRIEF 8.2). Each start renders the timeline in windows of 10 s of wall time: one video
/// process per window (PreviewVideo, <c>FrameStep = Speed</c>, rawvideo BGRA) and, at speed 1, one PerTrack audio
/// process (s16le, 2N channels) that the <see cref="AudioMixer"/> mixes live, so mute, solo and gain need no restart.
/// The next window's processes start when 3 s are left. Audio is the clock master at speed 1; at 2x and 4x (and with
/// <see cref="NullAudioOutput"/>, whose played samples are a Stopwatch) the clock is a Stopwatch.
/// </summary>
/// <remarks>
/// One session lives as long as the viewer and is started and stopped many times. <see cref="Start(PlaybackOptions)"/>,
/// <see cref="Stop"/> and <see cref="TryTakeFrame(long, out VideoFrame?, out int)"/> are called on the UI thread and
/// never block: process startup and teardown run in the background, and a new start waits for the previous teardown
/// before it touches the audio output. <see cref="PositionChanged"/>, <see cref="ReachedEnd"/> and <see cref="Failed"/>
/// are raised on worker threads.
/// </remarks>
public sealed class PlaybackSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly PlaybackServices _services;
    private PlaybackRun? _run;
    private PlaybackOptions? _options;
    private MixerState _mixer = MixerState.Neutral;
    private Task _teardown = Task.CompletedTask;
    private long _parked;
    private int _disposed;

    /// <summary>Creates an idle session.</summary>
    /// <param name="runner">Starts ffmpeg.</param>
    /// <param name="toolset">The validated ffmpeg.</param>
    /// <param name="caps">What the build can do (script syntax, drawtext).</param>
    /// <param name="work">The work directory.</param>
    /// <param name="catalog">Resolves the media of a project.</param>
    /// <param name="fonts">Places title fonts into the work directory.</param>
    /// <param name="audio">The audio output (owned by the caller; the session never disposes it).</param>
    /// <param name="cache">The frame cache shared with the frame server.</param>
    public PlaybackSession(
        ProcessRunner runner,
        FfmpegToolset toolset,
        FfmpegCapabilities caps,
        WorkDirectory work,
        IMediaCatalog catalog,
        FontLocator fonts,
        IAudioOutput audio,
        FrameCache cache)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(toolset);
        ArgumentNullException.ThrowIfNull(caps);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(cache);
        _services = new PlaybackServices(runner, toolset, caps, work, catalog, new PreviewFontResolver(catalog, fonts, work), audio, cache);
    }

    /// <summary>Raised on the feeder thread when the clock frame changes (at most once per frame).</summary>
    public event Action<long>? PositionChanged;

    /// <summary>Raised on the feeder thread when playback reached <see cref="PlaybackOptions.EndFrame"/> without a loop.</summary>
    public event Action? ReachedEnd;

    /// <summary>Raised on a worker thread when playback failed (an ffmpeg error, an odd size, a compile error).</summary>
    public event Action<string>? Failed;

    /// <summary>
    /// Raised on the feeder thread when the audio output became unavailable during playback (a disconnected device).
    /// Playback continues silently on a Stopwatch clock; <see cref="IAudioOutput.IsAvailable"/> is then false, so the
    /// status bar can show "Audio unavailable". The next start tries to reopen the device.
    /// </summary>
    public event Action? AudioUnavailable;

    /// <summary>The audio output.</summary>
    public IAudioOutput Audio => _services.Audio;

    /// <summary>True while a run plays (not stopped, not at its end, not failed).</summary>
    public bool IsPlaying
    {
        get
        {
            var run = Volatile.Read(ref _run);
            return run is not null && !run.HasEnded && !run.HasFailed;
        }
    }

    /// <summary>The speed of the current or last run.</summary>
    public int Speed => Volatile.Read(ref _options)?.Speed ?? 1;

    /// <summary>The options of the current or last run.</summary>
    public PlaybackOptions? Options => Volatile.Read(ref _options);

    /// <summary>The current mixer state.</summary>
    public MixerState Mixer => Volatile.Read(ref _mixer);

    /// <summary>
    /// The clock frame (TECH_BRIEF 8.2 <c>ClockFrame</c>): the timeline frame being heard now, or the parked frame when
    /// stopped.
    /// </summary>
    public long Position
    {
        get
        {
            var run = Volatile.Read(ref _run);
            return run is null ? Interlocked.Read(ref _parked) : run.Clock.Frame;
        }
    }

    /// <summary>Alias of <see cref="Position"/>, the name TECH_BRIEF 8.2 uses.</summary>
    public long ClockFrame => Position;

    /// <summary>Health of the current run (empty when stopped).</summary>
    public PlaybackStats Stats => Volatile.Read(ref _run)?.Stats() ?? PlaybackStats.Empty;

    /// <summary>
    /// Starts playing. A running playback is stopped first (its teardown continues in the background). Never blocks.
    /// </summary>
    /// <param name="options">What to play.</param>
    /// <exception cref="ArgumentException">When the options are invalid.</exception>
    /// <exception cref="ObjectDisposedException">After dispose.</exception>
    public void Start(PlaybackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_gate)
        {
            Volatile.Write(ref _mixer, options.Mixer);
            StartLocked(options);
        }
    }

    /// <summary>
    /// Starts playing the last options' snapshot from another frame at another speed (J/K/L shuttle).
    /// </summary>
    /// <param name="fromFrame">The first frame.</param>
    /// <param name="speed">1, 2 or 4.</param>
    /// <exception cref="InvalidOperationException">Before the first <see cref="Start(PlaybackOptions)"/>.</exception>
    public void Start(long fromFrame, int speed)
    {
        var last = Volatile.Read(ref _options)
            ?? throw new InvalidOperationException("Start(PlaybackOptions) must be called once before Start(fromFrame, speed).");
        Start(last with { StartFrame = fromFrame, Speed = speed, Mixer = Mixer });
    }

    /// <summary>
    /// Stops playback without blocking and returns the exact frame to park the playhead on: the last frame handed to
    /// the presenter, or the start frame when none was. The processes are killed in the background; await
    /// <see cref="StopAsync"/> to wait for that.
    /// </summary>
    /// <returns>The parked frame.</returns>
    public long Stop()
    {
        lock (_gate)
        {
            var run = _run;
            if (run is not null)
            {
                Interlocked.Exchange(ref _parked, run.ParkedFrame);
                _run = null;
                Retire(run);
            }

            return Interlocked.Read(ref _parked);
        }
    }

    /// <summary>Stops playback and waits until every process of the session is gone.</summary>
    /// <returns>The parked frame.</returns>
    public async Task<long> StopAsync()
    {
        var parked = Stop();
        Task teardown;
        lock (_gate)
        {
            teardown = _teardown;
        }

        await teardown.ConfigureAwait(false);
        return parked;
    }

    /// <summary>
    /// For the presenter (UI thread): the newest queued frame at or before the clock frame. See
    /// <see cref="TryTakeFrame(long, out VideoFrame?, out int)"/>.
    /// </summary>
    /// <param name="frame">The frame; the caller owns it and must call <see cref="VideoFrame.Return"/>.</param>
    /// <returns>True when a new frame is due.</returns>
    public bool TryTakeFrame(out VideoFrame? frame) => TryTakeFrame(Position, out frame, out _);

    /// <summary>
    /// For the presenter (UI thread, TECH_BRIEF 8.2): returns the newest queued frame at or before
    /// <paramref name="target"/>. Older queued frames are returned to the pool and counted as dropped. Returns false
    /// when no new frame is due (an underrun holds the last frame).
    /// </summary>
    /// <param name="target">The clock frame (normally <see cref="Position"/>).</param>
    /// <param name="frame">The frame; the caller owns it and must call <see cref="VideoFrame.Return"/>.</param>
    /// <param name="dropped">How many frames this call skipped.</param>
    /// <returns>True when a new frame is due.</returns>
    public bool TryTakeFrame(long target, out VideoFrame? frame, out int dropped)
    {
        var run = Volatile.Read(ref _run);
        if (run is null)
        {
            frame = null;
            dropped = 0;
            return false;
        }

        return run.TryTake(target, out frame, out dropped);
    }

    /// <summary>
    /// Applies a mixer change live (TECH_BRIEF 8.2): the next mixed block uses it, so it is heard within the queued
    /// audio (about 128 ms). After a fall back to Mixed audio the change restarts playback at the current frame.
    /// </summary>
    /// <param name="state">The new state.</param>
    public void SetMixer(MixerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            Volatile.Write(ref _mixer, state);
            if (_options is { } options)
            {
                Volatile.Write(ref _options, options with { Mixer = state });
            }

            var run = _run;
            if (run is null)
            {
                return;
            }

            run.Mixer.SetState(state);
            if (run.MixedAudioFallback && !run.HasEnded && !run.HasFailed)
            {
                // Mixed graphs bake mute, solo and gain in; a change restarts there (TECH_BRIEF 7.6).
                StartLocked(run.Options with { StartFrame = run.Clock.Frame, Mixer = state });
            }
        }
    }

    /// <summary>The per-track and master peaks since the last call (silence when stopped).</summary>
    /// <returns>The peaks.</returns>
    public MixerPeaks ReadAndResetPeaks() => Volatile.Read(ref _run)?.Mixer.ReadAndResetPeaks() ?? MixerPeaks.Silent;

    /// <summary>Stops playback and waits for the teardown. The audio output is left to its owner.</summary>
    /// <returns>A task that completes when every process is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
    }

    // Call under the lock.
    private void StartLocked(PlaybackOptions options)
    {
        if (_run is { } old)
        {
            Interlocked.Exchange(ref _parked, old.ParkedFrame);
            Retire(old);
        }

        Volatile.Write(ref _options, options);
        var run = new PlaybackRun(
            _services,
            options,
            _teardown,
            () => Mixer,
            OnPositionChanged,
            OnReachedEnd,
            OnFailed,
            OnAudioLost);
        _run = run;
        run.Begin();
    }

    // Call under the lock.
    private void Retire(PlaybackRun run)
    {
        var previous = _teardown;
        var stop = run.StopAsync();
        _teardown = Task.WhenAll(previous, stop);
    }

    private void OnPositionChanged(PlaybackRun run, long frame)
    {
        if (!ReferenceEquals(Volatile.Read(ref _run), run))
        {
            return;
        }

        try
        {
            PositionChanged?.Invoke(frame);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A failing handler must not stop the feeder.
        }
    }

    private void OnReachedEnd(PlaybackRun run)
    {
        var raise = false;
        lock (_gate)
        {
            if (!ReferenceEquals(_run, run))
            {
                return;
            }

            if (run.Options.Loop)
            {
                // Loop In to Out: restart at the loop start (DESIGN 6).
                StartLocked(run.Options with { StartFrame = run.Options.LoopStartFrame ?? run.Options.StartFrame, Mixer = Mixer });
            }
            else
            {
                raise = true;
            }
        }

        if (raise)
        {
            try
            {
                ReachedEnd?.Invoke();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A failing handler must not stop the feeder.
            }
        }
    }

    private void OnAudioLost(PlaybackRun run)
    {
        if (!ReferenceEquals(Volatile.Read(ref _run), run))
        {
            return;
        }

        try
        {
            AudioUnavailable?.Invoke();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A failing handler must not stop the feeder.
        }
    }

    private void OnFailed(PlaybackRun run, string message)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_run, run))
            {
                return;
            }

            // Keep the run as the current one so Stop still returns its parked frame, but release its processes now.
            _ = run.StopAsync();
        }

        try
        {
            Failed?.Invoke(message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A failing handler must not stop the worker.
        }
    }
}
