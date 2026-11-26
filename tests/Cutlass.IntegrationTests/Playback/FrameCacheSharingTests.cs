using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.IntegrationTests.PreviewTests;
using Cutlass.Media.Preview;
using Cutlass.Media.Preview.Playback;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Playback;

/// <summary>
/// The shared <see cref="FrameCache"/> of TECH_BRIEF 8.1 and 8.2: the frame server's prefetch fills
/// <c>[f-6, f+18)</c> after 150 ms of idle with frames identical to scrub renders, a new request kills a prefetch, and
/// playback shows the cached frame at its start frame at once.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class FrameCacheSharingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly PreviewSize Size = new(320, 180);
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public FrameCacheSharingTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task PrefetchCachesTheNeighboursAsTheyScrub()
    {
        await using var kit = await PlaybackKit.CreateAsync();
        var project = await kit.OracleProjectAsync(_media.Oracle25Intra);
        var sink = new RecordingSink();
        await using var server = new FrameServer(
            kit.Preview.Harness.Runner,
            kit.Preview.Harness.Toolset,
            kit.Preview.Capabilities,
            kit.Preview.Harness.Work,
            kit.Preview.Catalog,
            kit.Preview.Fonts,
            sink,
            kit.Cache)
        {
            PrefetchEnabled = true,
        };

        server.Request(new FrameRequest(project, 100, Size, false, 1));
        await sink.WaitAsync(1, Timeout);
        await PlaybackKit.WaitUntilAsync(() => kit.Cache.Count >= 24, Timeout, "the prefetch window [94, 118)");
        Assert.Equal(1, server.PrefetchRuns);
        await PlaybackKit.WaitUntilAsync(() => kit.Preview.Harness.Registry.Count == 0, Timeout, "the prefetch process to exit");

        // Every neighbour is a hit now, and identical to the oracle frame a scrub shows.
        var id = 2L;
        foreach (var frame in new long[] { 94, 99, 101, 117 })
        {
            server.Request(new FrameRequest(project, frame, Size, false, id));
            var presented = await sink.WaitAsync(id, Timeout);
            Assert.False(presented.Pooled, $"Frame {frame} was rendered instead of coming from the prefetch.");
            Assert.Equal(TestMediaFixture.ExpectedOracleValue(frame), OracleReader.DecodeBgra(presented.Pixels, 320, 180));
            id++;
        }

        // A frame outside the window renders, and its scrub output equals the prefetched bytes of the same frame.
        server.PrefetchEnabled = false;
        kit.Cache.Clear();
        server.Request(new FrameRequest(project, 99, Size, false, id));
        var scrubbed = await sink.WaitAsync(id, Timeout);
        Assert.True(scrubbed.Pooled);
        var prefetched = sink.Frames.Single(f => f.RequestId == 3);
        Assert.Equal(prefetched.Pixels, scrubbed.Pixels);
    }

    [FfmpegFact]
    public async Task ANewRequestKillsARunningPrefetch()
    {
        await using var kit = await PlaybackKit.CreateAsync();
        var asset = (await kit.Preview.Importer.ImportAsync(_media.Oracle25LongGop, CancellationToken.None)).Asset;

        // Eight stacked 4K layers make the 24-frame prefetch slow enough to be caught running.
        var tracks = Enumerable.Range(0, 8)
            .Select(t => Track($"tr_v{t}", TrackKind.Video, [V($"c_{t}", 0, 400, asset.Id, Micros(t * 40_000))]))
            .ToArray();
        var project = Project(3840, 2160, FrameRate.Fps30, [asset], tracks);
        var sink = new RecordingSink();
        await using var server = new FrameServer(
            kit.Preview.Harness.Runner,
            kit.Preview.Harness.Toolset,
            kit.Preview.Capabilities,
            kit.Preview.Harness.Work,
            kit.Preview.Catalog,
            kit.Preview.Fonts,
            sink,
            kit.Cache)
        {
            PrefetchEnabled = true,
        };
        var big = new PreviewSize(3840, 2160);

        server.Request(new FrameRequest(project, 200, big, false, 1));
        await sink.WaitAsync(1, Timeout);
        await PlaybackKit.WaitUntilAsync(() => server.PrefetchRuns == 1 && kit.Preview.Harness.Registry.Count > 0, Timeout, "the prefetch to start");
        var prefetch = kit.Preview.Harness.Registry.Snapshot()[0];

        server.Request(new FrameRequest(project, 300, big, false, 2));
        await ProcessAssert.GoneWithinAsync(prefetch.Pid, prefetch.StartTimeUtcTicks, TimeSpan.FromSeconds(5));
        await sink.WaitAsync(2, Timeout);
        Assert.True(kit.Cache.Count < 25, $"The killed prefetch still cached {kit.Cache.Count} frames.");
    }

    [FfmpegFact]
    public async Task PlaybackShowsTheCachedStartFrameAtOnce()
    {
        await using var kit = await PlaybackKit.CreateAsync();
        var project = await kit.OracleProjectAsync(_media.Oracle25Intra);
        var sink = new RecordingSink();
        await using (var server = new FrameServer(
            kit.Preview.Harness.Runner,
            kit.Preview.Harness.Toolset,
            kit.Preview.Capabilities,
            kit.Preview.Harness.Work,
            kit.Preview.Catalog,
            kit.Preview.Fonts,
            sink,
            kit.Cache))
        {
            server.Request(new FrameRequest(project, 200, Size, false, 1));
            await sink.WaitAsync(1, Timeout);
        }

        var session = kit.Session(new RecordingAudioOutput());
        using var presenter = new FakePresenter(session, (b, w, h) => OracleReader.DecodeBgra(b, w, h));
        session.Start(PlaybackOptions.ForSequence(project, 200, 1, Size, useProxies: false));
        await PlaybackKit.WaitUntilAsync(() => presenter.Presented.Count >= 10, Timeout, "10 presented frames");
        await session.StopAsync();
        presenter.Dispose();

        var presented = presenter.Presented;
        Assert.False(presented[0].Pooled);
        Assert.Equal(200, presented[0].Frame);
        Assert.Equal(TestMediaFixture.ExpectedOracleValue(200), presented[0].Value);

        // The decoded copy of frame 200 is skipped, never shown twice.
        Assert.Equal(presented.Count, presented.Select(p => p.Frame).Distinct().Count());
        Assert.All(presented.Skip(1), p => Assert.True(p.Pooled));
        Assert.All(presented, p => Assert.Equal(TestMediaFixture.ExpectedOracleValue(p.Frame), p.Value));
        Assert.Equal(0, kit.Preview.Harness.Registry.Count);
    }
}
