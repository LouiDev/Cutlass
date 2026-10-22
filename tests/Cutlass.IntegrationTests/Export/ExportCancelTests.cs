using System.Diagnostics;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Export;
using Cutlass.Media.Ffmpeg;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// Cancel and failure leave nothing behind (TECH_BRIEF 9 and 11.2): no <c>.partial</c> file, no final file and no
/// ffmpeg process; queued jobs never start; one export runs at a time.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class ExportCancelTests
{
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public ExportCancelTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task CancelLeavesNoPartialFileAndNoProcess()
    {
        await using var kit = await ExportKit.CreateAsync();
        var (project, catalog) = await LongTimelineAsync(kit);
        var preset = kit.Capabilities.HasEncoder("libx265") ? ExportPresets.H265 : ExportPresets.H264;
        var job = kit.Exports.Enqueue(kit.Request(project, catalog, preset, "cancelled", 0, 600, 1920, 1080, ExportQuality.Best));

        var (pid, startTicks) = await WaitForEncodingAsync(kit, job);
        Assert.True(File.Exists(job.PartialPath), "ffmpeg should be writing the partial file.");

        var stopwatch = Stopwatch.StartNew();
        job.Cancel();
        await job.Completion.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(ExportJobState.Canceled, job.State);
        Assert.Null(job.Error);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Cancel took {stopwatch.Elapsed}.");
        Assert.False(File.Exists(job.PartialPath), "The partial file was left behind.");
        Assert.False(File.Exists(job.OutputPath), "A cancelled export must not produce the final file.");
        await ProcessAssert.GoneWithinAsync(pid, startTicks, TimeSpan.FromSeconds(5));
        Assert.Equal(0, kit.Harness.Registry.Count);
    }

    [FfmpegFact]
    public async Task QueuedJobsRunOneAtATimeAndCancelBeforeStarting()
    {
        await using var kit = await ExportKit.CreateAsync();
        var (project, catalog) = await LongTimelineAsync(kit);
        var first = kit.Exports.Enqueue(kit.Request(project, catalog, ExportPresets.H264, "first", 0, 600, 1920, 1080, ExportQuality.Best));
        var second = kit.Exports.Enqueue(kit.Request(project, catalog, ExportPresets.H264, "second", 0, 30));

        await WaitForEncodingAsync(kit, first);
        Assert.Equal(ExportJobState.Queued, second.State);
        Assert.Single(kit.Harness.Registry.Snapshot());

        second.Cancel();
        await second.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ExportJobState.Canceled, second.State);
        Assert.Null(second.DisplayCommand);
        Assert.Equal(ExportJobState.Running, first.State);

        first.Cancel();
        await first.Completion.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(ExportJobState.Canceled, first.State);
        Assert.False(File.Exists(first.PartialPath));
        Assert.Equal(2, kit.Exports.Jobs.Count);
    }

    [FfmpegFact]
    public async Task FailureDeletesThePartialFileAndReportsTheLog()
    {
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_cfr", _media.Cfr25Av);

        // The catalog points at a file that is not media, so ffmpeg fails after the compile succeeded.
        var broken = Path.Combine(kit.OutputDirectory, "not-media.mp4");
        await File.WriteAllTextAsync(broken, "this is not a video");
        var catalog = new TestRenderCatalog().Add(new Cutlass.Compiler.MediaRenderInfo(asset, new Cutlass.Compiler.RenderSource(broken, 0, null), null));
        var project = Project(320, 180, FrameRate.Fps30, [asset], Track("tr_v1", TrackKind.Video, [V("c_v", 0, 30, "m_cfr", 0)]));

        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.H264, "failed", 0, 30));

        Assert.Equal(ExportJobState.Failed, job.State);
        var error = Assert.IsType<FfmpegException>(job.Error);
        Assert.NotEqual(0, error.ExitCode);
        Assert.NotEmpty(error.StderrTail);
        Assert.InRange(error.StderrTail.Count, 1, ProcessResult.ExceptionTailLines);
        Assert.Contains("not-media.mp4", error.DisplayCommand, StringComparison.Ordinal);
        Assert.False(File.Exists(job.PartialPath));
        Assert.False(File.Exists(job.OutputPath));
        Assert.Equal(0, kit.Harness.Registry.Count);
    }

    // 20 s of the intra oracle on a 30 fps timeline: 600 frames, slow enough at 1080p to cancel mid-way.
    private async Task<(Project Project, TestRenderCatalog Catalog)> LongTimelineAsync(ExportKit kit)
    {
        var asset = await kit.AssetAsync("m_o", _media.Oracle25Intra);
        var tone = await kit.AssetAsync("m_t", _media.Impulse);
        MediaAsset[] media = [asset, tone];
        var project = Project(
            1920,
            1080,
            FrameRate.Fps30,
            media,
            Track("tr_v1", TrackKind.Video, [V("c_v", 0, 600, "m_o", 0)]),
            Track("tr_a1", TrackKind.Audio, [A("c_a", 0, 600, "m_t", 0)]));
        return (project, await kit.CatalogAsync(media));
    }

    private static async Task<(int Pid, long StartTicks)> WaitForEncodingAsync(ExportKit kit, ExportJob job)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(60))
        {
            Assert.False(job.Completion.IsCompleted, $"The export finished before it could be cancelled: {job.State} {job.Error}");
            var children = kit.Harness.Registry.Snapshot();
            if (job.Progress.OutTimeMicros > 0 && children.Count == 1)
            {
                return (children[0].Pid, children[0].StartTimeUtcTicks);
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("The export did not start encoding within 60 s.");
    }
}
