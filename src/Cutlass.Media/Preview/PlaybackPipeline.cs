using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Emit;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.Media.Preview;

/// <summary>
/// Starts and owns the ffmpeg processes of one playback run (TECH_BRIEF 8.2): per window one video process
/// (PreviewVideo, <c>FrameStep = Speed</c>, rawvideo BGRA) and, at speed 1, one audio process (PreviewAudio, PerTrack
/// s16le with 2N channels). The readers ask for window <c>i + 1</c> when they have 3 s left in window <c>i</c>; the
/// request is idempotent, so whichever reader gets there first starts both processes. If a PerTrack audio window fails
/// before it delivers anything, the rest of the run falls back to a Mixed graph (TECH_BRIEF 7.6). Disposing kills every
/// process that is still running. Thread-safe.
/// </summary>
internal sealed class PlaybackPipeline : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ProcessRunner _runner;
    private readonly FfmpegToolset _toolset;
    private readonly FfmpegCapabilities _capabilities;
    private readonly WorkDirectory _work;
    private readonly Project _snapshot;
    private readonly IMediaCatalog _catalog;
    private readonly PlaybackOptions _options;
    private readonly Dictionary<int, WindowProcesses> _windows = [];
    private readonly HashSet<RunningProcess> _alive = [];
    private bool _disposed;
    private bool _mixedAudio;

    /// <summary>Creates the pipeline; nothing starts until <see cref="EnsureStarted"/>.</summary>
    /// <param name="runner">Starts ffmpeg.</param>
    /// <param name="toolset">The ffmpeg toolset.</param>
    /// <param name="capabilities">The capabilities.</param>
    /// <param name="work">The work directory.</param>
    /// <param name="catalog">The font-resolved catalog.</param>
    /// <param name="options">The playback options.</param>
    public PlaybackPipeline(
        ProcessRunner runner,
        FfmpegToolset toolset,
        FfmpegCapabilities capabilities,
        WorkDirectory work,
        IMediaCatalog catalog,
        PlaybackOptions options)
    {
        _runner = runner;
        _toolset = toolset;
        _capabilities = capabilities;
        _work = work;
        _catalog = catalog;
        _options = options;
        _snapshot = options.Snapshot;
        Rate = _snapshot.Sequence.Settings.Rate;
        PrefetchOutputFrames = PlaybackWindow.PrefetchOutputFrames(Rate, options.PrefetchSeconds);
        PrefetchSamples = (long)Math.Ceiling(options.PrefetchSeconds * TimeMath.SampleRate);
    }

    /// <summary>The sequence rate.</summary>
    public FrameRate Rate { get; }

    /// <summary>The speed.</summary>
    public int Speed => _options.Speed;

    /// <summary>True when the run plays audio (speed 1 only).</summary>
    public bool HasAudio => _options.Speed == 1;

    /// <summary>The video reader starts the next window when this many output frames are left.</summary>
    public long PrefetchOutputFrames { get; }

    /// <summary>The audio reader starts the next window when this many samples are left.</summary>
    public long PrefetchSamples { get; }

    /// <summary>How many windows were started.</summary>
    public int WindowsStarted
    {
        get
        {
            lock (_gate)
            {
                return _windows.Count;
            }
        }
    }

    /// <summary>True once PerTrack audio failed and the run uses Mixed audio graphs (mixer changes then restart).</summary>
    public bool MixedAudio
    {
        get
        {
            lock (_gate)
            {
                return _mixedAudio;
            }
        }
    }

    /// <summary>The planned window <paramref name="index"/>, or null past the end.</summary>
    /// <param name="index">The window number.</param>
    /// <returns>The window.</returns>
    public PlaybackWindow? Window(int index) =>
        PlaybackWindow.At(index, _options.StartFrame, _options.EndFrame, _options.Speed, Rate, _options.WindowSeconds);

    /// <summary>
    /// Starts the processes of window <paramref name="index"/> unless they already run. Does nothing past the end or
    /// after dispose.
    /// </summary>
    /// <param name="index">The window number.</param>
    public void EnsureStarted(int index) => GetOrStart(index);

    /// <summary>The video process of a window (started on demand) and its window.</summary>
    /// <param name="index">The window number.</param>
    /// <returns>The window and its process, or null past the end or after dispose.</returns>
    public (PlaybackWindow Window, RunningProcess Process, long Frames)? Video(int index) =>
        GetOrStart(index) is { } w ? (w.Window, w.Video, w.VideoFrames) : null;

    /// <summary>The audio process of a window (started on demand), its channel count and whether it is premixed.</summary>
    /// <param name="index">The window number.</param>
    /// <returns>The window, its process, the interleaved channels and the Mixed flag; null past the end, after dispose
    /// or at speeds above 1.</returns>
    public (PlaybackWindow Window, RunningProcess Process, int Channels, bool Premixed)? Audio(int index)
    {
        var w = GetOrStart(index);
        lock (_gate)
        {
            return w?.Audio is { } audio ? (w.Window, audio, w.AudioChannels, w.AudioPremixed) : null;
        }
    }

    /// <summary>
    /// Replaces a failed PerTrack audio window with a Mixed one (TECH_BRIEF 7.6) and switches the rest of the run to
    /// Mixed audio. Mute, solo and gain come from <paramref name="mixer"/> and are applied in the graph.
    /// </summary>
    /// <param name="index">The window number.</param>
    /// <param name="mixer">The mixer state to bake into the graph.</param>
    /// <returns>The new process and its channel count (2), or null after dispose.</returns>
    public (RunningProcess Process, int Channels)? RestartAudioMixed(int index, MixerState mixer)
    {
        lock (_gate)
        {
            if (_disposed || !_windows.TryGetValue(index, out var w))
            {
                return null;
            }

            _mixedAudio = true;
            if (w.Audio is { } failed)
            {
                _alive.Remove(failed);
                _ = failed.DisposeAsync().AsTask();
            }

            var (process, channels) = StartAudio(w.Window, AudioLayout.Mixed, mixer);
            w.Audio = process;
            w.AudioChannels = channels;
            w.AudioPremixed = true;

            // A next window that already started PerTrack is restarted Mixed as well, so the run is consistent.
            if (_windows.TryGetValue(index + 1, out var next) && next.Audio is { } early && !next.AudioPremixed)
            {
                _alive.Remove(early);
                _ = early.DisposeAsync().AsTask();
                var (nextProcess, nextChannels) = StartAudio(next.Window, AudioLayout.Mixed, mixer);
                next.Audio = nextProcess;
                next.AudioChannels = nextChannels;
                next.AudioPremixed = true;
            }

            return (process, channels);
        }
    }

    /// <summary>Releases a process whose output was read completely.</summary>
    /// <param name="process">The process.</param>
    /// <returns>A task that completes once it exited (or was killed).</returns>
    public async Task ReleaseAsync(RunningProcess process)
    {
        lock (_gate)
        {
            _alive.Remove(process);
        }

        // Its output is complete, so ffmpeg exits by itself; a short wait spares the kill.
        await Task.WhenAny(process.Exited, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        await process.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Kills every running process of the run.</summary>
    /// <returns>A task that completes once they are gone.</returns>
    public async ValueTask DisposeAsync()
    {
        List<RunningProcess> alive;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            alive = [.. _alive];
            _alive.Clear();
        }

        await Task.WhenAll(alive.Select(p => p.DisposeAsync().AsTask())).ConfigureAwait(false);
    }

    private WindowProcesses? GetOrStart(int index)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            if (_windows.TryGetValue(index, out var existing))
            {
                return existing;
            }

            if (Window(index) is not { } window)
            {
                return null;
            }

            var video = TimelineCompiler.Compile(
                _snapshot,
                _catalog,
                PreviewRender.Video(window.StartFrame, window.EndFrame, _options.Size, _options.UseProxies, window.Speed));
            if (video.FrameCount != window.OutputFrames)
            {
                throw new InvalidOperationException(
                    $"The compiled window [{window.StartFrame}, {window.EndFrame}) outputs {video.FrameCount} frames, expected {window.OutputFrames}.");
            }

            var videoSpec = PreviewRender.Spec(video, RenderPurpose.PreviewVideo, video: true, frames: null, _toolset, _capabilities, _work);
            var videoProcess = _runner.Start(videoSpec);
            _alive.Add(videoProcess);
            var entry = new WindowProcesses(window, videoProcess, video.FrameCount);
            _windows[index] = entry;

            if (HasAudio)
            {
                var layout = _mixedAudio ? AudioLayout.Mixed : AudioLayout.PerTrack;
                var (audioProcess, channels) = StartAudio(window, layout, _options.Mixer);
                entry.Audio = audioProcess;
                entry.AudioChannels = channels;
                entry.AudioPremixed = _mixedAudio;
            }

            return entry;
        }
    }

    // Call under the lock.
    private (RunningProcess Process, int Channels) StartAudio(PlaybackWindow window, AudioLayout layout, MixerState mixer)
    {
        var snapshot = layout == AudioLayout.Mixed ? mixer.ApplyTo(_snapshot) : _snapshot;
        var audio = TimelineCompiler.Compile(
            snapshot,
            _catalog,
            PreviewRender.Audio(window.StartFrame, window.EndFrame, _options.Size, _options.UseProxies, layout));
        if (audio.SampleCount != window.SampleCount)
        {
            throw new InvalidOperationException(
                $"The compiled audio window [{window.StartFrame}, {window.EndFrame}) outputs {audio.SampleCount} samples, expected {window.SampleCount}.");
        }

        var spec = PreviewRender.Spec(audio, RenderPurpose.PreviewAudio, video: false, frames: null, _toolset, _capabilities, _work);
        var process = _runner.Start(spec);
        _alive.Add(process);
        return (process, audio.AudioChannels);
    }

    private sealed class WindowProcesses(PlaybackWindow window, RunningProcess video, long videoFrames)
    {
        public PlaybackWindow Window { get; } = window;

        public RunningProcess Video { get; } = video;

        public long VideoFrames { get; } = videoFrames;

        public RunningProcess? Audio { get; set; }

        public int AudioChannels { get; set; }

        public bool AudioPremixed { get; set; }
    }
}
