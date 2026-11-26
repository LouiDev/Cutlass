using Cutlass.Compiler.Capabilities;
using Cutlass.Core.Editing;
using Cutlass.Media.Cache;
using Cutlass.Media.Export;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;
using Cutlass.Media.Preview;
using Cutlass.Media.Preview.Audio;
using Cutlass.Media.Projects;
using Cutlass.Media.Services;
using Cutlass.Media.Settings;

namespace Cutlass.Agent.Hosting;

/// <summary>
/// The UI-free part of the App's startup (AGENT_INTEGRATION 2.5): paths, settings, the orphan sweep, the work
/// directory and fonts, ffmpeg discovery and the capability probe, then the media cache, probe, frame index, posters,
/// catalog, importer, waveforms, export, the frame cache, <see cref="StillRenderer"/> and <see cref="RangeAudioReader"/>.
/// It creates no OpenAL output, playback, FrameServer, thumbnails, proxies, job scheduler or media pipeline (the pipeline
/// needs thumbnails and proxies). This repeats about 60 lines of <c>AppServices.StartCoreAsync</c> and <c>Apply</c> so the
/// tested <c>AppServices</c> stays untouched; keep both in step.
/// </summary>
public sealed class HeadlessEngine : IAgentServices, IAsyncDisposable
{
    private bool _disposed;

    private HeadlessEngine(AppPaths paths, ChildProcessRegistry registry, ProcessRunner runner, string appDirectory)
    {
        Paths = paths;
        Registry = registry;
        Runner = runner;
        Work = WorkDirectory.For(paths);
        Fonts = new FontLocator(appDirectory, Work, runner);
        SettingsStore = new SettingsStore(paths.SettingsFile);
        Projects = new ProjectStore();
        Ids = RandomIdGenerator.Shared;
        Fingerprints = new Fingerprinter(Path.Combine(paths.CacheRoot, Fingerprinter.MemoFileName));
        Log = new AppLog(paths.LogsDirectory);
    }

    /// <inheritdoc />
    public AppPaths Paths { get; }

    /// <inheritdoc />
    public AppLog? Log { get; }

    /// <inheritdoc />
    public IIdGenerator Ids { get; }

    /// <inheritdoc />
    public ProjectStore Projects { get; }

    /// <inheritdoc />
    public AppSettings Settings { get; private set; } = new();

    /// <summary>Tracks every child process.</summary>
    public ChildProcessRegistry Registry { get; }

    /// <summary>Starts every ffmpeg and ffprobe process.</summary>
    public ProcessRunner Runner { get; }

    /// <summary>The work directory.</summary>
    public WorkDirectory Work { get; }

    /// <summary>Title fonts.</summary>
    public FontLocator Fonts { get; }

    /// <summary>Loads <c>settings.json</c>.</summary>
    public SettingsStore SettingsStore { get; }

    /// <summary>Content fingerprints with their memo.</summary>
    public Fingerprinter Fingerprints { get; }

    /// <inheritdoc />
    public bool IsMediaReady => Catalog is not null && Capabilities is { CanRender: true };

    /// <inheritdoc />
    public string? NotReadyReason { get; private set; } = "ffmpeg has not been looked for yet.";

    /// <inheritdoc />
    public string? FfmpegVersion => Toolset?.Version.DisplayText;

    /// <summary>The validated ffmpeg, or null.</summary>
    public FfmpegToolset? Toolset { get; private set; }

    /// <inheritdoc />
    public FfmpegCapabilities? Capabilities { get; private set; }

    /// <summary>The per-media artifact cache, or null.</summary>
    public MediaCache? MediaCache { get; private set; }

    /// <summary>ffprobe, or null.</summary>
    public ProbeService? Probe { get; private set; }

    /// <summary>Frame indexes, or null.</summary>
    public FrameIndexService? FrameIndex { get; private set; }

    /// <summary>Posters, or null.</summary>
    public PosterService? Posters { get; private set; }

    /// <inheritdoc />
    public MediaCatalog? Catalog { get; private set; }

    /// <summary>Imports media, or null.</summary>
    public MediaImporter? Importer { get; private set; }

    /// <inheritdoc />
    public WaveformService? Waveforms { get; private set; }

    /// <inheritdoc />
    public ExportService? Export { get; private set; }

    /// <summary>The frame cache shared by the stills renderer, or null.</summary>
    public FrameCache? FrameCache { get; private set; }

    /// <inheritdoc />
    public StillRenderer? Stills { get; private set; }

    /// <inheritdoc />
    public RangeAudioReader? AudioReader { get; private set; }

    /// <summary>Starts the engine. Never throws for an ffmpeg problem; <see cref="NotReadyReason"/> says what was found.</summary>
    /// <param name="paths">The paths, or null for this machine's (<see cref="AppPaths.ForCurrentPlatform"/>).</param>
    /// <param name="ffmpegPath">An ffmpeg to prefer, or null for the settings and the search.</param>
    /// <param name="appDirectory">The folder with <c>fonts/</c>, or null for the executable's folder.</param>
    /// <param name="installShutdownHooks">True in a process of its own (the MCP exe); false in tests.</param>
    /// <param name="ct">Cancels startup.</param>
    /// <returns>The engine.</returns>
    public static async Task<HeadlessEngine> StartAsync(
        AppPaths? paths = null,
        string? ffmpegPath = null,
        string? appDirectory = null,
        bool installShutdownHooks = true,
        CancellationToken ct = default)
    {
        var p = paths ?? AppPaths.ForCurrentPlatform();
        var registry = ChildProcessRegistry.ForCurrentPlatform(p);
        if (installShutdownHooks)
        {
            registry.InstallShutdownHooks();
        }

        var runner = ProcessRunner.CreateDefault(registry);
        var engine = new HeadlessEngine(p, registry, runner, appDirectory ?? AppContext.BaseDirectory);
        await Task.Run(() => engine.StartCoreAsync(ffmpegPath, ct), ct).ConfigureAwait(false);
        return engine;
    }

    /// <summary>Cancels exports, then kills any child process left.</summary>
    /// <returns>A task that completes when everything stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Export is { } export)
        {
            await export.DisposeAsync().ConfigureAwait(false);
        }

        Catalog?.Dispose();
        Registry.KillAll();
        Registry.Dispose();
    }

    // Mirrors AppServices.StartCoreAsync and Apply (UI-free part); keep both in step.
    private async Task StartCoreAsync(string? pickedFfmpeg, CancellationToken ct)
    {
        try
        {
            Paths.EnsureDirectories();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NotReadyReason = "The cache folder is unavailable: " + ex.Message;
            return;
        }

        Settings = await SettingsStore.LoadAsync(ct).ConfigureAwait(false);
        Registry.SweepOrphans();
        Work.EnsureCreated();
        Work.DeleteStale(DateTime.UtcNow);
        Fonts.EnsureBundledFonts();

        FfmpegToolset? toolset = null;
        if (pickedFfmpeg is not null)
        {
            try
            {
                toolset = await FfmpegToolset.ValidateAsync(pickedFfmpeg, Runner, Work.Path, ct).ConfigureAwait(false);
            }
            catch (FfmpegException ex)
            {
                NotReadyReason = ex.Message;
            }
        }

        toolset ??= await FfmpegLocator.ForCurrentPlatform()
            .FindAsync(pickedFfmpeg ?? Settings.FfmpegPath, Runner, Work.Path, ct)
            .ConfigureAwait(false);
        if (toolset is null)
        {
            NotReadyReason = "ffmpeg was not found. Cutlass needs ffmpeg and ffprobe for import, preview and export.";
            return;
        }

        Toolset = toolset;
        FfmpegCapabilities caps;
        try
        {
            caps = await new CapabilityProbe(Runner, Work, Paths.CapabilitiesFile).ProbeAsync(toolset, ct).ConfigureAwait(false);
        }
        catch (FfmpegException ex)
        {
            NotReadyReason = "ffmpeg is unusable: " + ex.Message;
            return;
        }

        Capabilities = caps;
        if (!caps.CanRender)
        {
            NotReadyReason = "This ffmpeg build lacks required filters: " + string.Join(", ", caps.MissingRequiredFilters) + ".";
            return;
        }

        MediaCache = new MediaCache(Paths.CacheRoot, toolset.Version.BuildId, Math.Max(1, Settings.CacheLimitGb) * MediaCache.BytesPerGb);
        Probe = new ProbeService(Runner, toolset, Work, MediaCache, Fingerprints);
        FrameIndex = new FrameIndexService(Runner, toolset, Work, MediaCache);
        Posters = new PosterService(Runner, toolset, Paths, MediaCache);
        Catalog = new MediaCatalog(Probe, FrameIndex);
        Importer = new MediaImporter(Probe, Posters, FrameIndex, Catalog, Ids, Fingerprints);
        Waveforms = new WaveformService(Runner, toolset, Work, MediaCache);
        Export = new ExportService(Runner, toolset, caps, Work, Fonts);
        FrameCache = new FrameCache();
        Stills = new StillRenderer(Runner, toolset, caps, Work, Catalog, Fonts, FrameCache);
        AudioReader = new RangeAudioReader(Runner, toolset, caps, Work, Catalog);
        NotReadyReason = null;
    }
}
