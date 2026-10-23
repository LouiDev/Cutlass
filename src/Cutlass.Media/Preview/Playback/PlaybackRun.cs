using System.Diagnostics;
using Cutlass.Compiler;
using Cutlass.Core.Model;
using Cutlass.Media.Preview.Audio;
using Cutlass.Media.Services;

namespace Cutlass.Media.Preview.Playback;

/// <summary>
/// One continuous playback from a start frame (TECH_BRIEF 8.2): a pipeline of window processes, a
/// <see cref="VideoWindowReader"/>, an <see cref="AudioFeeder"/> and the <see cref="PlaybackClock"/>. A
/// <see cref="PlaybackSession"/> creates one per start, loop restart or Mixed-fallback mixer change. The run waits for
/// the previous run's teardown before it touches the shared audio output.
/// </summary>
internal sealed class PlaybackRun
{
    private readonly PlaybackServices _services;
    private readonly Task _previousTeardown;
    private readonly Func<MixerState> _mixerState;
    private readonly Action<PlaybackRun, long> _positionChanged;
    private readonly Action<PlaybackRun> _reachedEnd;
    private readonly Action<PlaybackRun, string> _failed;
    private readonly Action<PlaybackRun> _audioLost;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _takeGate = new();
    private readonly object _stopGate = new();
    private Task _startup = Task.CompletedTask;
    private Task? _stop;
    private PlaybackPipeline? _pipeline;
    private VideoWindowReader? _reader;
    private Task _readerTask = Task.CompletedTask;
    private AudioFeeder? _feeder;
    private VideoFrame? _startFrame;
    private long _lastTaken = long.MinValue;
    private long _presented;
    private long _dropped;
    private long _lagSince;
    private int _ended;
    private int _failedFlag;
    private int _usedAudio;
    private int _underrunBaseline;

    /// <summary>Creates a run; <see cref="Begin"/> starts it.</summary>
    /// <param name="services">The shared services.</param>
    /// <param name="options">What to play.</param>
    /// <param name="previousTeardown">The previous run's teardown, awaited before anything starts.</param>
    /// <param name="mixerState">The session's current mixer state.</param>
    /// <param name="positionChanged">Clock frame changes (feeder thread).</param>
    /// <param name="reachedEnd">The clock reached the end (feeder thread).</param>
    /// <param name="failed">The run failed (any thread).</param>
    /// <param name="audioLost">The audio output became unavailable during the run (feeder thread).</param>
    public PlaybackRun(
        PlaybackServices services,
        PlaybackOptions options,
        Task previousTeardown,
        Func<MixerState> mixerState,
        Action<PlaybackRun, long> positionChanged,
        Action<PlaybackRun> reachedEnd,
        Action<PlaybackRun, string> failed,
        Action<PlaybackRun> audioLost)
    {
        _audioLost = audioLost;
        _services = services;
        Options = options;
        _previousTeardown = previousTeardown;
        _mixerState = mixerState;
        _positionChanged = positionChanged;
        _reachedEnd = reachedEnd;
        _failed = failed;
        var rate = options.Snapshot.Sequence.Settings.Rate;
        Clock = new PlaybackClock(rate, options.StartFrame, options.EndFrame, options.Speed, audioMaster: options.Speed == 1);
        Mixer = new AudioMixer(
            options.Snapshot.Sequence.Tracks.Where(t => t.Kind == TrackKind.Audio).Select(t => t.Id),
            options.Mixer);
    }

    /// <summary>What this run plays.</summary>
    public PlaybackOptions Options { get; }

    /// <summary>The clock.</summary>
    public PlaybackClock Clock { get; }

    /// <summary>The live mixer.</summary>
    public AudioMixer Mixer { get; }

    /// <summary>True once the clock reached the end.</summary>
    public bool HasEnded => Volatile.Read(ref _ended) != 0;

    /// <summary>True once the run failed.</summary>
    public bool HasFailed => Volatile.Read(ref _failedFlag) != 0;

    /// <summary>True when the run's audio fell back to Mixed graphs.</summary>
    public bool MixedAudioFallback => _pipeline?.MixedAudio ?? false;

    /// <summary>The frame to park on when stopping: the last frame handed out, else the start frame.</summary>
    public long ParkedFrame
    {
        get
        {
            var last = Interlocked.Read(ref _lastTaken);
            return last == long.MinValue ? Options.StartFrame : last;
        }
    }

    /// <summary>How many render windows the run started.</summary>
    public int WindowsStarted => _pipeline?.WindowsStarted ?? 0;

    /// <summary>Starts the run in the background.</summary>
    public void Begin() => _startup = Task.Run(StartAsync);

    /// <summary>A snapshot of the run's health.</summary>
    /// <returns>The stats.</returns>
    public PlaybackStats Stats()
    {
        var lagSince = Interlocked.Read(ref _lagSince);
        var lag = lagSince == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(lagSince);
        var underruns = Volatile.Read(ref _usedAudio) != 0 ? Math.Max(0, _services.Audio.Underruns - _underrunBaseline) : 0;
        return new PlaybackStats(
            Interlocked.Read(ref _presented),
            Interlocked.Read(ref _dropped),
            underruns,
            WindowsStarted,
            lag,
            MixedAudioFallback);
    }

    /// <summary>
    /// Hands out the newest queued frame at or before <paramref name="target"/>; older ones are returned and counted
    /// as dropped (TECH_BRIEF 8.2). Frames at or before the last handed-out frame are discarded without counting.
    /// </summary>
    /// <param name="target">The clock frame.</param>
    /// <param name="frame">The frame; the caller owns it and must return it.</param>
    /// <param name="dropped">How many frames were dropped by this call.</param>
    /// <returns>True when a frame was handed out.</returns>
    public bool TryTake(long target, out VideoFrame? frame, out int dropped)
    {
        frame = null;
        dropped = 0;
        lock (_takeGate)
        {
            var last = Interlocked.Read(ref _lastTaken);
            var countable = false;
            if (_startFrame is { } cachedStart && last == long.MinValue)
            {
                // The cached frame at StartFrame is shown at once, before ffmpeg delivers it.
                _startFrame = null;
                frame = cachedStart;
                last = cachedStart.TimelineFrame;
            }

            if (_reader is { } reader)
            {
                while (reader.Frames.TryPeek(out var next))
                {
                    if (next.TimelineFrame <= last)
                    {
                        // Already shown (the cached start frame): never present a frame twice or backwards.
                        if (reader.Frames.TryRead(out var stale))
                        {
                            stale.Return();
                        }

                        continue;
                    }

                    if (next.TimelineFrame > target)
                    {
                        break;
                    }

                    if (!reader.Frames.TryRead(out var taken))
                    {
                        break;
                    }

                    if (frame is not null)
                    {
                        // Superseding the cached start frame is not a drop; skipping a decoded frame is.
                        if (countable)
                        {
                            dropped++;
                        }

                        frame.Return();
                    }

                    frame = taken;
                    countable = true;
                }
            }

            var shown = frame?.TimelineFrame ?? last;
            UpdateLag(dropped > 0 || (Clock.IsRunning && shown != long.MinValue && shown < target - Options.Speed));
            if (frame is null)
            {
                return false;
            }

            Interlocked.Exchange(ref _lastTaken, frame.TimelineFrame);
            Interlocked.Increment(ref _presented);
            Interlocked.Add(ref _dropped, dropped);
            return true;
        }
    }

    /// <summary>
    /// Stops the run: cancels it, kills its processes, waits for the reader and the feeder, stops and flushes the audio
    /// output, and returns every pooled buffer. Idempotent; never throws.
    /// </summary>
    /// <returns>A task that completes when everything is released.</returns>
    public Task StopAsync()
    {
        lock (_stopGate)
        {
            return _stop ??= Task.Run(StopCoreAsync);
        }
    }

    private void UpdateLag(bool late)
    {
        if (!late)
        {
            Interlocked.Exchange(ref _lagSince, 0);
        }
        else if (Interlocked.Read(ref _lagSince) == 0)
        {
            Interlocked.Exchange(ref _lagSince, Stopwatch.GetTimestamp());
        }
    }

    private async Task StartAsync()
    {
        var token = _cts.Token;
        try
        {
            await _previousTeardown.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A failed teardown has already released what it could.
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var options = Options;
            if (options.UseProxies && _services.Catalog is MediaCatalog media)
            {
                // Proxies another instance deleted (TECH_BRIEF 6.1) would fail the windows: play the originals.
                media.DetachMissingProxies();
            }

            var catalog = await _services.Fonts.ResolveAsync(options.Snapshot, token).ConfigureAwait(false);
            LoadCachedStartFrame(options, catalog);

            var pipeline = new PlaybackPipeline(_services.Runner, _services.Toolset, _services.Capabilities, _services.Work, catalog, options);
            _pipeline = pipeline;
            if (token.IsCancellationRequested)
            {
                return;
            }

            pipeline.EnsureStarted(0);
            if (pipeline.HasAudio)
            {
                _services.Audio.Open(Cutlass.Core.Time.TimeMath.SampleRate, 2);
                _underrunBaseline = _services.Audio.Underruns;
                Volatile.Write(ref _usedAudio, 1);
            }

            var reader = new VideoWindowReader(pipeline, options.Size);
            var feeder = new AudioFeeder(
                pipeline,
                _services.Audio,
                Mixer,
                Clock,
                reader.FirstFrame,
                _mixerState,
                frame => _positionChanged(this, frame),
                OnReachedEnd,
                ex => Fail(PreviewRender.Describe(ex)),
                () => _audioLost(this));
            lock (_takeGate)
            {
                _reader = reader;
            }

            _feeder = feeder;
            _readerTask = reader.RunAsync(token);
            _ = _readerTask.ContinueWith(
                t => Fail(PreviewRender.Describe(t.Exception!.GetBaseException())),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            feeder.Start(token);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Stopped during startup.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Compile errors, odd sizes, a missing ffmpeg or an unusable audio output all end the run with a message.
            Fail(PreviewRender.Describe(ex));
        }
    }

    private void LoadCachedStartFrame(PlaybackOptions options, IMediaCatalog catalog)
    {
        var fingerprints = PreviewRender.Fingerprints(options.Snapshot, _services.Catalog);
        var key = PreviewRender.FrameKey(options.Snapshot, catalog, fingerprints, options.StartFrame, options.Size, options.UseProxies);
        if (_services.Cache.TryGet(key, out var pixels))
        {
            var size = options.Size;
            lock (_takeGate)
            {
                _startFrame = new VideoFrame(pixels, size.Width, size.Height, size.Width * 4, options.StartFrame, 0, pooled: false);
            }
        }
    }

    private void OnReachedEnd()
    {
        if (Interlocked.Exchange(ref _ended, 1) == 0)
        {
            _reachedEnd(this);
        }
    }

    private void Fail(string message)
    {
        if (_cts.IsCancellationRequested || Interlocked.Exchange(ref _failedFlag, 1) != 0)
        {
            return;
        }

        _failed(this, message);
    }

    private async Task StopCoreAsync()
    {
        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            await Quietly(_startup).ConfigureAwait(false);
            if (_pipeline is { } pipeline)
            {
                await pipeline.DisposeAsync().ConfigureAwait(false);
            }

            await Quietly(_readerTask).ConfigureAwait(false);
            if (_feeder is { } feeder)
            {
                await Quietly(feeder.Pump).ConfigureAwait(false);
                await Quietly(feeder.Completion).ConfigureAwait(false);
                feeder.DrainBlocks();
            }

            if (Volatile.Read(ref _usedAudio) != 0)
            {
                _services.Audio.Stop();
                _services.Audio.Flush();
            }

            lock (_takeGate)
            {
                if (_reader is { } reader)
                {
                    while (reader.Frames.TryRead(out var frame))
                    {
                        frame.Return();
                    }
                }

                _startFrame = null;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Teardown never throws; everything that could be released was.
        }
    }

    private static async Task Quietly(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Failures were reported when they happened.
        }
    }
}
