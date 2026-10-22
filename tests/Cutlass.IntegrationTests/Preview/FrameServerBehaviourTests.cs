using System.Collections.Concurrent;
using System.Diagnostics;
using Cutlass.Compiler;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Export;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Preview;
using static Cutlass.IntegrationTests.Export.TimelineKit;

// Not ".Preview": that namespace would hide TimelineKit.Preview(...) in the Export tests, which import it statically.
namespace Cutlass.IntegrationTests.PreviewTests;

/// <summary>
/// The frame server protocol (TECH_BRIEF 8.1): latest wins, cache hits, failures reported without stopping the
/// worker, and dispose kills a running scrub so no ffmpeg is left behind.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class FrameServerBehaviourTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public FrameServerBehaviourTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task AFloodOfRequestsPresentsTheLastRequestLast()
    {
        await using var kit = await PreviewKit.CreateAsync();
        var (project, _) = await OracleProjectAsync(kit, _media.Oracle25LongGop);
        var sink = new RecordingSink();
        var server = kit.Server(sink);
        var failures = new ConcurrentQueue<string>();
        server.RenderFailed += (id, message) => failures.Enqueue($"{id}: {message}");

        for (var id = 1; id <= 20; id++)
        {
            server.Request(new FrameRequest(project, id * 11, new PreviewSize(320, 180), false, id));
        }

        await sink.WaitAsync(20, Timeout);
        await Task.Delay(500);

        Assert.Empty(failures);
        var ids = sink.Frames.Select(f => f.RequestId).ToList();
        Assert.Equal(20, ids[^1]);
        Assert.Equal(ids.OrderBy(i => i).Distinct(), ids);
        Assert.True(ids.Count < 20, $"Every request rendered ({ids.Count}); older waiting requests should be dropped.");
        Assert.Equal(220, sink.Frames[^1].TimelineFrame);
    }

    [FfmpegFact]
    public async Task ARepeatedFrameComesFromTheCache()
    {
        await using var kit = await PreviewKit.CreateAsync();
        var (project, _) = await OracleProjectAsync(kit, _media.Oracle25Intra);
        var sink = new RecordingSink();
        var server = kit.Server(sink);

        server.Request(new FrameRequest(project, 40, new PreviewSize(320, 180), false, 1));
        var first = await sink.WaitAsync(1, Timeout);
        server.Request(new FrameRequest(project, 40, new PreviewSize(320, 180), false, 2));
        var second = await sink.WaitAsync(2, Timeout);

        Assert.True(first.Pooled);
        Assert.False(second.Pooled);
        Assert.Equal(first.Pixels, second.Pixels);
        Assert.Equal(1, server.CachedFrameCount);
        Assert.Equal(0, kit.Harness.Registry.Count);
    }

    [FfmpegFact]
    public async Task AFailedRenderIsReportedAndTheNextRequestStillWorks()
    {
        await using var kit = await PreviewKit.CreateAsync();
        var (project, asset) = await OracleProjectAsync(kit, _media.Oracle25Intra);
        var broken = Path.Combine(kit.Harness.Root, "not-media.mkv");
        await File.WriteAllTextAsync(broken, "this is not a video");
        var catalog = new SwitchableCatalog(new MediaRenderInfo(asset, new RenderSource(broken, 0, null), null));
        var sink = new RecordingSink();
        var server = kit.Server(sink, catalog);
        var failed = new TaskCompletionSource<(long Id, string Message)>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.RenderFailed += (id, message) => failed.TrySetResult((id, message));

        server.Request(new FrameRequest(project, 10, new PreviewSize(320, 180), false, 7));
        var (failedId, failedMessage) = await failed.Task.WaitAsync(Timeout);
        Assert.Equal(7, failedId);
        Assert.False(string.IsNullOrWhiteSpace(failedMessage));

        // An odd size is reported too, without throwing from Request.
        var oddFailed = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.RenderFailed += (id, _) => { if (id == 8) { oddFailed.TrySetResult(id); } };
        server.Request(new FrameRequest(project, 10, new PreviewSize(321, 180), false, 8));
        Assert.Equal(8, await oddFailed.Task.WaitAsync(Timeout));

        catalog.Info = kit.Catalog.Get(asset.Id)!;
        server.Request(new FrameRequest(project, 10, new PreviewSize(320, 180), false, 9));
        var presented = await sink.WaitAsync(9, Timeout);
        Assert.Equal(TestMediaFixture.ExpectedOracleValue(10 * 25 / 30), OracleReader.DecodeBgra(presented.Pixels, 320, 180));
        Assert.DoesNotContain(sink.Frames, f => f.RequestId is 7 or 8);
    }

    [FfmpegFact]
    public async Task DisposeWhileRenderingLeavesNoFfmpeg()
    {
        await using var kit = await PreviewKit.CreateAsync();
        var imported = await kit.Importer.ImportAsync(_media.Oracle25LongGop, CancellationToken.None);
        var asset = imported.Asset;

        // Eight stacked 4K layers of a long-GOP clip decoded deep into a GOP: slow enough to be caught running.
        var tracks = Enumerable.Range(0, 8)
            .Select(t => Track($"tr_v{t}", TrackKind.Video, [V($"c_{t}", 0, 200, asset.Id, Micros(9_960_000 + (t * 40_000)))]))
            .ToArray();
        var project = Project(3840, 2160, FrameRate.Fps30, [asset], tracks);
        var sink = new RecordingSink();
        var server = kit.Server(sink);
        var failures = new ConcurrentQueue<string>();
        server.RenderFailed += (id, message) => failures.Enqueue($"{id}: {message}");

        server.Request(new FrameRequest(project, 150, new PreviewSize(3840, 2160), false, 1));
        var (pid, startTicks) = WaitForChild(kit, TimeSpan.FromSeconds(30));

        var stopwatch = Stopwatch.StartNew();
        await server.DisposeAsync();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(6), $"Dispose took {stopwatch.Elapsed}.");
        await ProcessAssert.GoneWithinAsync(pid, startTicks, TimeSpan.FromSeconds(2));
        Assert.Equal(0, kit.Harness.Registry.Count);
        Assert.Empty(failures);

        // Requests after dispose are ignored without throwing.
        server.Request(new FrameRequest(project, 151, new PreviewSize(3840, 2160), false, 2));
        Assert.DoesNotContain(sink.Frames, f => f.RequestId == 2);
    }

    private static (int Pid, long StartTicks) WaitForChild(PreviewKit kit, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            var children = kit.Harness.Registry.Snapshot();
            if (children.Count > 0)
            {
                return (children[0].Pid, children[0].StartTimeUtcTicks);
            }

            Thread.Yield();
        }

        throw new TimeoutException("The scrub process did not start.");
    }

    private static async Task<(Project Project, MediaAsset Asset)> OracleProjectAsync(PreviewKit kit, string file)
    {
        var imported = await kit.Importer.ImportAsync(file, CancellationToken.None);
        var asset = imported.Asset;
        var project = Project(320, 180, FrameRate.Fps30, [asset], Track("tr_v1", TrackKind.Video, [V("c_o", 0, 400, asset.Id, 0)]));
        return (project, asset);
    }

    private sealed class SwitchableCatalog(MediaRenderInfo info) : IMediaCatalog
    {
        public MediaRenderInfo Info { get; set; } = info;

        public MediaRenderInfo? Get(string mediaId) => mediaId == Info.Asset.Id ? Info : null;
    }
}
