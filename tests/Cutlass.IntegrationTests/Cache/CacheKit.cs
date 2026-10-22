using Cutlass.Core.Model;
using Cutlass.IntegrationTests.PreviewTests;
using Cutlass.Media.Cache;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Jobs;
using Cutlass.Media.Services;

namespace Cutlass.IntegrationTests.Cache;

/// <summary>
/// The preview stack of <see cref="PreviewKit"/> plus the M5 cache services on a private media cache: frame index
/// with <c>frames.idx</c>, thumbnails, waveforms, proxies, the scheduler and the pipeline.
/// </summary>
internal sealed class CacheKit : IAsyncDisposable
{
    private CacheKit(PreviewKit preview)
    {
        Preview = preview;
        var h = preview.Harness;
        Cache = new MediaCache(preview.Paths.CacheRoot, h.Toolset.Version.BuildId);
        CachedIndex = new FrameIndexService(h.Runner, h.Toolset, h.Work, Cache);
        Thumbnails = new ThumbnailService(h.Runner, h.Toolset, h.Work, Cache, preview.Catalog);
        Waveforms = new WaveformService(h.Runner, h.Toolset, h.Work, Cache);
        Proxies = new ProxyService(h.Runner, h.Toolset, preview.Capabilities, h.Work, Cache, preview.FrameIndex, preview.Probe, preview.Catalog);
    }

    /// <summary>The preview stack (importer, catalog, frame servers).</summary>
    public PreviewKit Preview { get; }

    /// <summary>The media cache.</summary>
    public MediaCache Cache { get; }

    /// <summary>A frame index service backed by the cache.</summary>
    public FrameIndexService CachedIndex { get; }

    /// <summary>The thumbnail service.</summary>
    public ThumbnailService Thumbnails { get; }

    /// <summary>The waveform service.</summary>
    public WaveformService Waveforms { get; }

    /// <summary>The proxy service (attaches proxies to <see cref="PreviewKit.Catalog"/>).</summary>
    public ProxyService Proxies { get; }

    /// <summary>A toolset whose executables do not exist: any process start fails, which proves cache hits.</summary>
    public FfmpegToolset BrokenToolset => Preview.Harness.Toolset with
    {
        FfmpegPath = Path.Combine(Preview.Harness.Root, "missing", "ffmpeg.exe"),
        FfprobePath = Path.Combine(Preview.Harness.Root, "missing", "ffprobe.exe"),
    };

    /// <summary>Creates the kit.</summary>
    public static async Task<CacheKit> CreateAsync() => new(await PreviewKit.CreateAsync());

    /// <summary>Imports a file through the product importer.</summary>
    public async Task<MediaAsset> ImportAsync(string path) =>
        (await Preview.Importer.ImportAsync(path, CancellationToken.None)).Asset;

    /// <summary>Creates a pipeline on a new scheduler.</summary>
    public (JobScheduler Scheduler, MediaPipeline Pipeline) Pipeline()
    {
        var scheduler = new JobScheduler();
        var pipeline = new MediaPipeline(scheduler, Cache, Preview.Catalog, CachedIndex, Thumbnails, Waveforms, Proxies);
        return (scheduler, pipeline);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Preview.DisposeAsync();
}
