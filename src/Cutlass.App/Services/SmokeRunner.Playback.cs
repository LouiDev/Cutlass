using System.Diagnostics;
using System.Globalization;
using Cutlass.Media.Jobs;

namespace Cutlass.App.Services;

/// <summary>
/// The M5 smoke commands (TECH_BRIEF 12, M5 Verify): <c>play</c> and <c>stop</c> drive the transport and measure the
/// playback clock against the wall clock and the presented frames, <c>wait-jobs</c> waits for the media jobs.
/// </summary>
public sealed partial class SmokeRunner
{
    /// <summary>How long <c>wait-jobs</c> waits by default.</summary>
    public static readonly TimeSpan DefaultJobsTimeout = TimeSpan.FromMinutes(5);

    /// <summary>How long <c>play</c> waits for the first presented frame.</summary>
    public static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The drift <c>stop</c> accepts by default, in frames.</summary>
    public const double DefaultMaxDriftFrames = 2;

    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(500);

    private readonly List<PlaybackSample> _samples = [];
    private CancellationTokenSource? _sampling;
    private int _playSpeed = 1;

    private async Task PlayAsync(SmokeStep step)
    {
        Expect(_vm.HasProject, "no project is open");
        var playback = _vm.Viewer.Playback;
        Expect(playback.Engine is not null, "playback needs ffmpeg");
        var speed = step.Option("speed") is { } text ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture) : 1;
        Expect(speed is 1 or 2 or 4, "speed must be 1, 2 or 4");
        Expect(!playback.IsPlaying, "playback is already running");

        // The same commands as Space and L.
        _vm.Viewer.PlayPauseCommand.Execute(null);
        Expect(playback.IsPlaying, "playback did not start");
        while (playback.Speed < speed)
        {
            _vm.Viewer.ShuttleForwardCommand.Execute(null);
        }

        _playSpeed = speed;
        var start = _vm.Timeline.Playhead;
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"playing from frame {start} at {playback.Speed}x, audio {(playback.Engine!.HasAudio ? "OpenAL" : "silent clock")}"));
        var waited = Stopwatch.StartNew();
        while (_vm.Presenter.PlaybackFramesShown == 0)
        {
            Expect(playback.IsPlaying, "playback stopped before the first frame: " + (_vm.Viewer.StatusText ?? "no reason given"));
            Expect(waited.Elapsed < FirstFrameTimeout, string.Create(CultureInfo.InvariantCulture, $"no frame was presented within {FirstFrameTimeout.TotalSeconds:0} s"));
            await Task.Delay(20);
        }

        _log.Write(string.Create(CultureInfo.InvariantCulture, $"first frame {_vm.Presenter.LastPlaybackFrame} presented after {waited.Elapsed.TotalMilliseconds:0} ms"));
        _samples.Clear();
        _sampling = new CancellationTokenSource();
        _ = SampleAsync(_sampling.Token);
    }

    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        while (!cancellationToken.IsCancellationRequested)
        {
            var playback = _vm.Viewer.Playback;
            if (!playback.IsPlaying || playback.Engine is not { } engine)
            {
                _log.Write("  playback stopped: " + (playback.LastStopReason ?? "unknown"));
                return;
            }

            var sample = new PlaybackSample(
                clock.Elapsed.TotalSeconds,
                engine.Position,
                _vm.Presenter.LastPlaybackFrame,
                _vm.Presenter.PlaybackFramesShown,
                _vm.Viewer.MeterLeftDb,
                _vm.Viewer.MeterRightDb);
            _samples.Add(sample);
            _log.Write(string.Create(
                CultureInfo.InvariantCulture,
                $"  t={sample.Seconds,6:0.000} s clock {sample.Clock,6} presented {sample.Presented,6} (lag {sample.Clock - sample.Presented}) shown {sample.Shown,5} meter L {sample.LeftDb:0.0} R {sample.RightDb:0.0} dBFS"));
            try
            {
                await Task.Delay(SampleInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void StopPlayback(SmokeStep step)
    {
        var playback = _vm.Viewer.Playback;
        Expect(playback.IsPlaying, "playback is not running (it stopped: " + (playback.LastStopReason ?? "never started") + ")");
        _sampling?.Cancel();
        var stats = playback.Stats();
        var shown = _vm.Presenter.PlaybackFramesShown;
        var lastShown = _vm.Presenter.LastPlaybackFrame;
        _vm.Viewer.PauseCommand.Execute(null);
        Expect(!playback.IsPlaying, "K did not stop playback");
        _log.Write(string.Create(
            CultureInfo.InvariantCulture,
            $"stopped: parked on frame {_vm.Timeline.Playhead} (last presented {lastShown}); {shown} frames shown, {stats.FramesPresented} handed out, {stats.FramesDropped} dropped, {stats.Underruns} underruns, {stats.WindowsStarted} windows"));
        Expect(_vm.Timeline.Playhead == lastShown, string.Create(CultureInfo.InvariantCulture, $"the playhead parked on {_vm.Timeline.Playhead}, not on the last presented frame {lastShown}"));

        // Drift: the clock against the wall clock from the first sample on (the start latency is not drift).
        var rate = _vm.Session.Current.Sequence.Settings.Rate.Value;
        var fps = (double)rate.Num / rate.Den * _playSpeed;
        var moving = _samples.ToList();
        Expect(moving.Count >= 2, "too few samples to measure drift (play for at least 1 s)");
        var first = moving[0];
        var maxDrift = 0.0;
        var maxLag = 0L;
        foreach (var s in moving)
        {
            var expected = first.Clock + ((s.Seconds - first.Seconds) * fps);
            var drift = s.Clock - expected;
            maxDrift = Math.Abs(drift) > Math.Abs(maxDrift) ? drift : maxDrift;
            maxLag = Math.Max(maxLag, s.Clock - s.Presented);
        }

        var last = moving[^1];
        var finalDrift = last.Clock - (first.Clock + ((last.Seconds - first.Seconds) * fps));
        var seconds = last.Seconds - first.Seconds;
        var advanced = last.Clock - first.Clock;
        var meterMax = moving.Max(s => Math.Max(s.LeftDb, s.RightDb));
        _log.Write(string.Create(
            CultureInfo.InvariantCulture,
            $"drift over {seconds:0.00} s ({advanced} frames at {fps:0.###} fps): final {finalDrift:+0.00;-0.00;0.00} frames ({finalDrift / fps * 1000:+0.0;-0.0;0.0} ms), max {maxDrift:+0.00;-0.00;0.00} frames; presenter lag max {maxLag} frames; meter peak {meterMax:0.0} dBFS"));
        var limit = step.Option("max-drift") is { } text ? double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) : DefaultMaxDriftFrames;
        Expect(advanced > 0, "the playback clock did not advance");
        Expect(Math.Abs(maxDrift) <= limit, string.Create(CultureInfo.InvariantCulture, $"the clock drifted {maxDrift:0.00} frames from the wall clock (limit {limit})"));
    }

    private async Task WaitForJobsAsync(TimeSpan timeout)
    {
        var scheduler = _services.Scheduler ?? throw Fail("the media jobs need ffmpeg");
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await scheduler.WhenIdleAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw Fail(string.Create(CultureInfo.InvariantCulture, $"the media jobs were not idle within {timeout.TotalSeconds:0} s ({scheduler.ActiveCount} left)"));
        }

        var finished = scheduler.Jobs.Where(j => j.IsFinished).ToList();
        _log.Write(string.Create(
            CultureInfo.InvariantCulture,
            $"jobs idle: {finished.Count(j => j.State == JobState.Completed)} completed, {finished.Count(j => j.State == JobState.Failed)} failed, {finished.Count(j => j.State == JobState.Canceled)} cancelled"));
        foreach (var failed in finished.Where(j => j.State == JobState.Failed))
        {
            _log.Write("  failed " + failed.Kind + " " + failed.MediaName + ": " + failed.Error);
        }

        if (_services.Artifacts is { } artifacts)
        {
            foreach (var asset in _vm.Session.Current.Media)
            {
                var sheet = artifacts.TryGetSheet(asset);
                var peaks = artifacts.TryGetPeaks(asset);
                var proxy = _services.Catalog?.Get(asset.Id)?.Proxy is not null;
                _log.Write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {Path.GetFileName(asset.Path)}: thumbnails {(sheet is null ? "none" : sheet.Count + " tiles")}, waveform {(peaks is null ? "none" : peaks.LevelCount + " levels")}, proxy {(proxy ? "yes" : "no")}"));
            }
        }
    }

    // The live mute and solo checks in the real app: the master meter follows the mixer within the meter's fall time.
    private void ExpectMeter(SmokeStep step)
    {
        var level = Math.Max(_vm.Viewer.MeterLeftDb, _vm.Viewer.MeterRightDb);
        var min = step.Option("min") is { } lo ? double.Parse(lo, NumberStyles.Float, CultureInfo.InvariantCulture) : double.NegativeInfinity;
        var max = step.Option("max") is { } hi ? double.Parse(hi, NumberStyles.Float, CultureInfo.InvariantCulture) : double.PositiveInfinity;
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"meter L {_vm.Viewer.MeterLeftDb:0.0} R {_vm.Viewer.MeterRightDb:0.0} dBFS"));
        Expect(level >= min, string.Create(CultureInfo.InvariantCulture, $"the meter reads {level:0.0} dBFS, expected at least {min:0.0}"));
        Expect(level <= max, string.Create(CultureInfo.InvariantCulture, $"the meter reads {level:0.0} dBFS, expected at most {max:0.0}"));
    }

    private void ExpectArtifacts(SmokeStep step)
    {
        var asset = Media(Arg(step, 0));
        var artifacts = _services.Artifacts ?? throw Fail("the media artifacts need ffmpeg");
        var name = Path.GetFileName(asset.Path);
        if (step.Flag("thumbs"))
        {
            Expect(artifacts.TryGetSheet(asset) is { Count: > 0 }, "no thumbnail sheets for " + name);
        }

        if (step.Flag("peaks"))
        {
            Expect(artifacts.TryGetPeaks(asset) is not null, "no waveform peaks for " + name);
        }

        if (step.Flag("proxy"))
        {
            Expect(_services.Catalog?.Get(asset.Id)?.Proxy is not null, "no proxy for " + name);
        }

        _log.Write("artifacts present for " + name + ": " + string.Join(' ', step.Args.Skip(1)));
    }

    private void ShowJobs(SmokeStep step)
    {
        var open = Arg(step, 0) switch
        {
            "open" => true,
            "close" => false,
            var other => throw Fail("jobs takes open or close, not " + other),
        };
        _vm.Jobs.IsOpen = open;
        var jobs = _vm.Jobs.Jobs;
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"jobs popover {(open ? "open" : "closed")}: {jobs.Count} rows, summary \"{_vm.Jobs.Summary}\", cache \"{_vm.CacheSummary}\""));
        foreach (var row in jobs)
        {
            _log.Write("  " + row.Kind + " " + row.Title + ": " + row.StatusText);
        }
    }

    /// <summary>One measurement during playback.</summary>
    /// <param name="Seconds">Wall time since sampling started.</param>
    /// <param name="Clock">The playback clock frame.</param>
    /// <param name="Presented">The last presented frame.</param>
    /// <param name="Shown">Frames shown so far.</param>
    /// <param name="LeftDb">The left meter level.</param>
    /// <param name="RightDb">The right meter level.</param>
    private sealed record PlaybackSample(double Seconds, long Clock, long Presented, long Shown, double LeftDb, double RightDb);
}
