using System.Diagnostics;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Export;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Preview;
using Xunit.Abstractions;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Playback;

/// <summary>
/// Real playback through ffmpeg (TECH_BRIEF 8.2 and 11.2), headless: a <see cref="RecordingAudioOutput"/> (the silent
/// Stopwatch output plus a recorder) and a <see cref="FakePresenter"/>. Checks A/V sync on the sync clip, gapless window
/// seams, the 2x and 4x frame-skip pattern, live mute and solo, and that stopping parks on the shown frame and leaves
/// no ffmpeg behind.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class PlaybackSessionTests
{
    private static readonly PreviewSize Size = new(320, 180);
    private readonly TestMediaFixture _media;
    private readonly ITestOutputHelper _output;

    /// <summary>Receives the shared media fixture and the test output.</summary>
    /// <param name="media">The fixture.</param>
    /// <param name="output">Receives the drift report of the 2 minute run.</param>
    public PlaybackSessionTests(TestMediaFixture media, ITestOutputHelper output)
    {
        _media = media;
        _output = output;
    }

    [FfmpegFact]
    public async Task TwentySecondsOfTheSyncClipStayInSyncAcrossWindowSeams()
    {
        await using var kit = await PlaybackKit.CreateAsync();
        var project = await kit.RepeatedAvProjectAsync(_media.Sync, copies: 2);
        var audio = new RecordingAudioOutput();
        var session = kit.Session(audio);

        // 3.7 s windows put seams at frames 111, 222, 333, 444 and 555, all inside clips and between impulses, besides
        // the clip join at 300. (A seam exactly on an impulse would test the mkv seek precision, not playback.)
        var options = PlaybackOptions.ForSequence(project, 0, 1, Size, useProxies: false) with { WindowSeconds = 3.7 };
        var (presented, dropped, failure) = await PlayToEndAsync(session, options, PlaybackAssert.Flash, TimeSpan.FromSeconds(60));

        Assert.Null(failure);
        PlaybackAssert.SyncPlayback(presented, dropped, audio.Recorded, seconds: 20);
        var parked = await session.StopAsync();
        Assert.Equal(599, parked);
        Assert.Equal(0, kit.Preview.Harness.Registry.Count);
    }

    [FfmpegFact]
    public async Task TwoMinutesOfTheSyncClipPlayWithoutDrift()
    {
        await using var kit = await PlaybackKit.CreateAsync();
        var project = await kit.RepeatedAvProjectAsync(_media.Sync, copies: 12);
        var audio = new RecordingAudioOutput();
        var session = kit.Session(audio);
        var options = PlaybackOptions.ForSequence(project, 0, 1, Size, useProxies: false);

        var stopwatch = Stopwatch.StartNew();
        var (presented, dropped, failure) = await PlayToEndAsync(session, options, PlaybackAssert.Flash, TimeSpan.FromSeconds(200));
        stopwatch.Stop();

        Assert.Null(failure);
        ReportDrift(presented, dropped, audio.Recorded, stopwatch.Elapsed);
        PlaybackAssert.SyncPlayback(presented, dropped, audio.Recorded, seconds: 120);

        // The clock is real time: 3600 frames take 120 s plus the start-up, never less.
        Assert.InRange(stopwatch.Elapsed.TotalSeconds, 119.9, 125.0);
        Assert.Equal(12, session.Stats.WindowsStarted);
        await session.StopAsync();
        Assert.Equal(0, kit.Preview.Harness.Registry.Count);
    }

    [FfmpegTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task WindowSeamsAreGaplessAndFasterSpeedsSkipFrames(int speed)
    {
        await using var kit = await PlaybackKit.CreateAsync();
        var project = await kit.OracleProjectAsync(_media.Oracle25Intra);
        var audio = new RecordingAudioOutput();
        var session = kit.Session(audio);
        const long start = 3;

        // 2 s windows: 50, 100 or 200 timeline frames, so a 20 s range crosses 9, 4 or 2 seams.
        var options = PlaybackOptions.ForSequence(project, start, speed, Size, useProxies: false) with { WindowSeconds = 2 };
        var (presented, dropped, failure) = await PlayToEndAsync(
            session,
            options,
            (buffer, w, h) => OracleReader.DecodeBgra(buffer, w, h),
            TimeSpan.FromSeconds(60));

        Assert.Null(failure);
        var expectedFrames = (500 - start + speed - 1) / speed;
        Assert.Equal(expectedFrames, presented.Count + dropped);
        Assert.True(dropped <= expectedFrames / 20, $"{dropped} of {expectedFrames} frames were dropped.");
        for (var i = 0; i < presented.Count; i++)
        {
            var p = presented[i];

            // The 8.2 skip pattern: output frame k is start + k * speed, and its picture is that timeline frame.
            Assert.Equal(0, (p.Frame - start) % speed);
            Assert.Equal(TestMediaFixture.ExpectedOracleValue(p.Frame), p.Value);
            if (i > 0)
            {
                Assert.True(p.Frame > presented[i - 1].Frame);
            }
        }

        Assert.Equal(start + ((expectedFrames - 1) * speed), presented[^1].Frame);
        if (speed == 1)
        {
            // The audio track is empty: its window audio is exact-length silence.
            var recorded = audio.Recorded;
            Assert.Equal((TimeMath.SamplesAt(500, FrameRate.Fps25) - TimeMath.SamplesAt(start, FrameRate.Fps25)) * 2, recorded.LongLength);
            Assert.All(recorded, s => Assert.Equal(0, s));
        }
        else
        {
            // Audio is muted above 1x: no audio process, the output is never opened.
            Assert.Equal(0, audio.Opens);
        }

        await session.StopAsync();
        Assert.Equal(0, kit.Preview.Harness.Registry.Count);
    }

    [FfmpegFact]
    public async Task StopParksOnTheShownFrameAndKillsFfmpeg()
    {
        await using var kit = await PlaybackKit.CreateAsync();
        var project = await kit.RepeatedAvProjectAsync(_media.Sync, copies: 2);
        var audio = new RecordingAudioOutput();
        var session = kit.Session(audio);
        var presenter = new FakePresenter(session, PlaybackAssert.Flash);
        session.Start(PlaybackOptions.ForSequence(project, 45, 1, Size, useProxies: false));
        await PlaybackKit.WaitUntilAsync(() => presenter.Presented.Count >= 40, TimeSpan.FromSeconds(30), "40 presented frames");
        var children = kit.Preview.Harness.Registry.Snapshot();
        Assert.NotEmpty(children);
        Assert.True(session.IsPlaying);

        presenter.Dispose();
        var shown = presenter.Presented[^1].Frame;
        var stopwatch = Stopwatch.StartNew();
        var parked = session.Stop();
        var stopTook = stopwatch.Elapsed;

        Assert.Equal(shown, parked);
        Assert.True(stopTook < TimeSpan.FromMilliseconds(100), $"Stop blocked for {stopTook}.");
        Assert.False(session.IsPlaying);
        Assert.Equal(parked, session.Position);
        Assert.False(session.TryTakeFrame(parked + 1000, out _, out _));
        foreach (var child in children)
        {
            await ProcessAssert.GoneWithinAsync(child.Pid, child.StartTimeUtcTicks, TimeSpan.FromSeconds(1));
        }

        Assert.Equal(parked, await session.StopAsync());
        Assert.Equal(0, kit.Preview.Harness.Registry.Count);
        Assert.False(audio.IsStarted);
    }

    [FfmpegFact]
    public async Task MuteAndSoloApplyLiveWithoutARestart()
    {
        await using var kit = await PlaybackKit.CreateAsync();
        var sync = (await kit.Preview.Importer.ImportAsync(_media.Sync, CancellationToken.None)).Asset;
        var tone = (await kit.Preview.Importer.ImportAsync(_media.Tone, CancellationToken.None)).Asset;
        var project = Project(
            320,
            180,
            FrameRate.Fps30,
            [sync, tone],
            Track("tr_v1", TrackKind.Video, [V("c_v", 0, 300, sync.Id, 0)]),
            Track("tr_a1", TrackKind.Audio, [A("c_imp", 0, 300, sync.Id, 0)]),
            Track("tr_a2", TrackKind.Audio, [A("c_tone", 0, 300, tone.Id, 0)]));
        var audio = new RecordingAudioOutput();
        var session = kit.Session(audio);
        var options = PlaybackOptions.ForSequence(project, 0, 1, Size, useProxies: false);
        var mixer = options.Mixer;
        using var presenter = new FakePresenter(session, PlaybackAssert.Flash);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.ReachedEnd += () => ended.TrySetResult();
        string? failure = null;
        session.Failed += message => failure = message;

        session.Start(options);
        await PlaybackKit.WaitUntilAsync(() => audio.EnqueuedFrames > 96_000, TimeSpan.FromSeconds(30), "2 s of audio");
        var muteAt = audio.EnqueuedFrames;
        session.SetMixer(mixer.WithTrack(new MixerTrack("tr_a2", Muted: true, Solo: false, GainDb: 0)));
        await PlaybackKit.WaitUntilAsync(() => audio.EnqueuedFrames > muteAt + 96_000, TimeSpan.FromSeconds(30), "2 more seconds");
        var soloAt = audio.EnqueuedFrames;
        session.SetMixer(mixer.WithTrack(new MixerTrack("tr_a2", Muted: false, Solo: true, GainDb: -6)));
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Null(failure);
        Assert.Equal(1, audio.Opens);
        Assert.Equal(1, session.Stats.WindowsStarted);
        var recorded = audio.Recorded;
        Assert.Equal(480_000L * 2, recorded.LongLength);

        // A change reaches the next mixed block: allow one block in flight on each side.
        const int Margin = 1024;
        var before = Analyse(recorded, 0, muteAt);
        var muted = Analyse(recorded, muteAt + Margin, soloAt);
        var soloed = Analyse(recorded, soloAt + Margin, 480_000);

        Assert.True(before.ToneRms > 2000, $"Tone RMS before the mute was {before.ToneRms}.");
        Assert.True(before.Impulses >= 2);
        Assert.True(muted.ToneRms < 1, $"Tone RMS while muted was {muted.ToneRms}.");
        Assert.True(muted.Impulses >= 1);
        Assert.Equal(0, soloed.Impulses);
        var expectedRms = before.ToneRms * Math.Pow(10, -6.0 / 20);
        Assert.InRange(soloed.ToneRms, expectedRms * 0.95, expectedRms * 1.05);
        await session.StopAsync();
        Assert.Equal(0, kit.Preview.Harness.Registry.Count);
    }

    // The M5 Verify report (TECH_BRIEF 12): how far the presented frames drifted from the wall clock, how far each frame
    // lagged the audio clock it was taken for, and how far the heard impulses sat from their exact samples.
    private void ReportDrift(IReadOnlyList<FakePresenter.Presentation> presented, long dropped, short[] recorded, TimeSpan elapsed)
    {
        if (presented.Count < 3)
        {
            return;
        }

        // The start frame is shown as soon as it is decoded, before the audio clock runs (TECH_BRIEF 8.2 Start), so the
        // drift is measured from the next frame on; the start-up latency is reported on its own.
        var startLatency = presented[1].At - presented[0].At - TimeSpan.FromSeconds((presented[1].Frame - presented[0].Frame) / 30.0);
        var first = presented[1];
        var maxWallDrift = 0.0;
        foreach (var p in presented.Skip(1))
        {
            var drift = p.Frame - (first.Frame + ((p.At - first.At).TotalSeconds * 30));
            maxWallDrift = Math.Abs(drift) > Math.Abs(maxWallDrift) ? drift : maxWallDrift;
        }

        var last = presented[^1];
        var finalWallDrift = last.Frame - (first.Frame + ((last.At - first.At).TotalSeconds * 30));
        var maxLag = presented.Max(p => p.Target - p.Frame);
        var impulses = PlaybackAssert.ImpulsePositions(recorded);
        var maxImpulseOffset = impulses
            .Select((position, k) => Math.Abs(position - TimeMath.SamplesAt(30L * k, FrameRate.Fps30)))
            .DefaultIfEmpty(0)
            .Max();
        _output.WriteLine(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"2 min playback: {presented.Count} presented, {dropped} dropped, {elapsed.TotalSeconds:0.000} s wall, start-up {startLatency.TotalMilliseconds:0} ms; drift against the wall clock max {maxWallDrift:+0.00;-0.00;0.00} frames, final {finalWallDrift:+0.00;-0.00;0.00} frames; A/V lag max {maxLag} frames; impulse offset max {maxImpulseOffset} samples ({maxImpulseOffset / 1600.0:0.000} frames)"));
    }

    // The tone's RMS over the region, ignoring the impulse samples, and the number of impulses.
    private static (double ToneRms, int Impulses) Analyse(short[] stereo, long from, long to)
    {
        double sum = 0;
        long count = 0;
        var impulses = 0;
        for (var i = from; i < to; i++)
        {
            var s = stereo[2 * i];
            if (Math.Abs((int)s) > PlaybackAssert.ImpulseThreshold)
            {
                impulses++;
                continue;
            }

            sum += (double)s * s;
            count++;
        }

        return (Math.Sqrt(sum / Math.Max(1, count)), impulses);
    }

    private static async Task<(IReadOnlyList<FakePresenter.Presentation> Presented, long Dropped, string? Failure)> PlayToEndAsync(
        PlaybackSession session,
        PlaybackOptions options,
        Func<byte[], int, int, int> analyse,
        TimeSpan timeout)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? failure = null;
        session.ReachedEnd += () => ended.TrySetResult();
        session.Failed += message =>
        {
            failure = message;
            ended.TrySetResult();
        };

        using var presenter = new FakePresenter(session, analyse);
        session.Start(options);
        await ended.Task.WaitAsync(timeout);

        // The presenter takes the last frame on its next poll.
        await Task.Delay(200);
        presenter.Dispose();
        return (presenter.Presented, presenter.Dropped, failure);
    }
}
