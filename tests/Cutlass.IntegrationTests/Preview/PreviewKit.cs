using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Core.Editing;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;
using Cutlass.Media.Preview;
using Cutlass.Media.Services;

// Not ".Preview": that namespace would hide TimelineKit.Preview(...) in the Export tests, which import it statically.
namespace Cutlass.IntegrationTests.PreviewTests;

/// <summary>
/// The product's preview stack wired up against the test ffmpeg: probe, frame index, catalog, posters, importer and
/// a frame server presenting into a <see cref="RecordingSink"/>, all inside a private temp cache.
/// </summary>
internal sealed class PreviewKit : IAsyncDisposable
{
    private readonly TempDirectory _cache = new("cutlass-preview");
    private readonly List<FrameServer> _servers = [];

    private PreviewKit(MediaHarness harness, FfmpegCapabilities capabilities)
    {
        Harness = harness;
        Capabilities = capabilities;
        Paths = new AppPaths(_cache.Combine("settings.json"), _cache.Combine("data"), _cache.Combine("cache"), OperatingSystem.IsWindows());
        Probe = new ProbeService(harness.Runner, harness.Toolset, harness.Work);
        FrameIndex = new FrameIndexService(harness.Runner, harness.Toolset, harness.Work);
        Catalog = new MediaCatalog(Probe, FrameIndex);
        Posters = new PosterService(harness.Runner, harness.Toolset, Paths);
        Importer = new MediaImporter(Probe, Posters, FrameIndex, Catalog, RandomIdGenerator.Shared);
        Fonts = new FontLocator(AppContext.BaseDirectory, harness.Work, harness.Runner);
    }

    /// <summary>The process layer.</summary>
    public MediaHarness Harness { get; }

    /// <summary>The detected capabilities.</summary>
    public FfmpegCapabilities Capabilities { get; }

    /// <summary>App paths rooted in a temp folder.</summary>
    public AppPaths Paths { get; }

    /// <summary>The probe service.</summary>
    public ProbeService Probe { get; }

    /// <summary>The frame index service.</summary>
    public FrameIndexService FrameIndex { get; }

    /// <summary>The media catalog.</summary>
    public MediaCatalog Catalog { get; }

    /// <summary>The poster service.</summary>
    public PosterService Posters { get; }

    /// <summary>The importer.</summary>
    public MediaImporter Importer { get; }

    /// <summary>The font locator.</summary>
    public FontLocator Fonts { get; }

    /// <summary>Creates the kit.</summary>
    public static async Task<PreviewKit> CreateAsync()
    {
        var harness = await MediaHarness.CreateAsync();
        var capabilities = await new CapabilityProbe(harness.Runner, harness.Work, cacheFile: null)
            .ProbeAsync(harness.Toolset, CancellationToken.None);
        return new PreviewKit(harness, capabilities);
    }

    /// <summary>Creates a frame server presenting into <paramref name="sink"/>; the kit disposes it.</summary>
    public FrameServer Server(IVideoFrameSink sink, IMediaCatalog? catalog = null)
    {
        var server = new FrameServer(Harness.Runner, Harness.Toolset, Capabilities, Harness.Work, catalog ?? Catalog, Fonts, sink);
        _servers.Add(server);
        return server;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var server in _servers)
        {
            await server.DisposeAsync();
        }

        Catalog.Dispose();
        await Harness.DisposeAsync();
        _cache.Dispose();
    }
}
