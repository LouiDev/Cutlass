using System.Globalization;
using Cutlass.App.Services;
using Cutlass.App.Services.Ffmpeg;
using Cutlass.Compiler.Capabilities;
using Cutlass.Core.Editing;
using Cutlass.Media.Audio;
using Cutlass.Media.Cache;
using Cutlass.Media.Export;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Jobs;
using Cutlass.Media.Platform;
using Cutlass.Media.Preview;
using Cutlass.Media.Preview.Audio;
using Cutlass.Media.Preview.Playback;
using Cutlass.Media.Projects;
using Cutlass.Media.Services;
using Cutlass.Media.Settings;

namespace Cutlass.App;

/// <summary>
/// The hand-written composition root (TECH_BRIEF 10.1, no DI container). <see cref="CreateDefault"/> builds the
/// parts that need no I/O on the UI thread: paths, the child process registry with its shutdown hooks and the
/// process runner with its Job Object. <see cref="StartAsync"/> then does the rest off the UI thread (TECH_BRIEF 5):
/// settings, the orphan sweep, work directory cleanup, bundled fonts, ffmpeg discovery and validation and the
/// capability probe. <see cref="Apply"/> creates the ffmpeg services on the UI thread from the result.
/// </summary>
public sealed partial class AppServices : IAsyncDisposable
{
    private readonly object _audioGate = new();
    private int _started;
    private bool _disposed;
    private IAudioOutput? _audio;
    private string? _audioFailure;

    /// <summary>Creates the service set from its parts.</summary>
    /// <param name="paths">The platform paths.</param>
    /// <param name="registry">The child process registry.</param>
    /// <param name="runner">The process runner.</param>
    public AppServices(AppPaths paths, ChildProcessRegistry registry, ProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(runner);
        Paths = paths;
        Registry = registry;
        Runner = runner;
        Work = WorkDirectory.For(paths);
        Fonts = FontLocator.ForCurrentApp(Work, runner);
        SettingsStore = new SettingsStore(paths.SettingsFile);
        Projects = new ProjectStore();
        RecentProjects = new RecentProjectsStore(Path.Combine(paths.DataDirectory, "recent.json"), paths.PathComparer);
        Ids = RandomIdGenerator.Shared;
        Settings = new AppSettings();
        Fingerprints = new Fingerprinter(Path.Combine(paths.CacheRoot, Fingerprinter.MemoFileName));
    }

    /// <summary>Content fingerprints with the <c>fingerprints.json</c> memo (TECH_BRIEF 6.2), shared by import,
    /// probe and relink so the memo has one writer.</summary>
    public Fingerprinter Fingerprints { get; }

    /// <summary>Settings, data and cache locations for this machine.</summary>
    public AppPaths Paths { get; }

    /// <summary>Tracks every child process so none outlives the app (TECH_BRIEF 5.4).</summary>
    public ChildProcessRegistry Registry { get; }

    /// <summary>Starts every ffmpeg and ffprobe process.</summary>
    public ProcessRunner Runner { get; }

    /// <summary>The work directory every ffmpeg process runs in.</summary>
    public WorkDirectory Work { get; }

    /// <summary>Bundled and system fonts for titles.</summary>
    public FontLocator Fonts { get; }

    /// <summary>Loads and saves <c>settings.json</c>.</summary>
    public SettingsStore SettingsStore { get; }

    /// <summary>Loads and saves projects and their sidecars.</summary>
    public ProjectStore Projects { get; }

    /// <summary>The recent projects list.</summary>
    public RecentProjectsStore RecentProjects { get; }

    /// <summary>Creates model ids.</summary>
    public IIdGenerator Ids { get; }

    /// <summary>The settings (defaults until <see cref="Apply"/>).</summary>
    public AppSettings Settings { get; private set; }

    /// <summary>The ffmpeg state.</summary>
    public FfmpegState FfmpegState { get; private set; } = FfmpegState.Checking;

    /// <summary>The validated ffmpeg, or null.</summary>
    public FfmpegToolset? Toolset { get; private set; }

    /// <summary>The capabilities of <see cref="Toolset"/>, or null.</summary>
    public FfmpegCapabilities? Capabilities { get; private set; }

    /// <summary>Runs ffprobe, or null while ffmpeg is unavailable.</summary>
    public ProbeService? Probe { get; private set; }

    /// <summary>Builds frame indexes, or null while ffmpeg is unavailable.</summary>
    public FrameIndexService? FrameIndex { get; private set; }

    /// <summary>Renders media posters, or null while ffmpeg is unavailable.</summary>
    public PosterService? Posters { get; private set; }

    /// <summary>The media the compiler renders from, or null while ffmpeg is unavailable.</summary>
    public MediaCatalog? Catalog { get; private set; }

    /// <summary>Imports media files, or null while ffmpeg is unavailable.</summary>
    public MediaImporter? Importer { get; private set; }

    /// <summary>Runs exports, or null while ffmpeg cannot render.</summary>
    public ExportService? Export { get; private set; }

    /// <summary>Renders scrub frames, or null while ffmpeg cannot render.</summary>
    public FrameServer? FrameServer { get; private set; }

    /// <summary>The per-media artifact cache (TECH_BRIEF 6.1), or null while ffmpeg is unavailable.</summary>
    public MediaCache? MediaCache { get; private set; }

    /// <summary>Renders filmstrip sheets, or null while ffmpeg is unavailable.</summary>
    public ThumbnailService? Thumbnails { get; private set; }

    /// <summary>Builds waveform peaks, or null while ffmpeg is unavailable.</summary>
    public WaveformService? Waveforms { get; private set; }

    /// <summary>Builds proxies, or null while ffmpeg cannot render.</summary>
    public ProxyService? Proxies { get; private set; }

    /// <summary>Runs the background media jobs, or null while ffmpeg is unavailable.</summary>
    public JobScheduler? Scheduler { get; private set; }

    /// <summary>Turns media into scheduler jobs, or null while ffmpeg is unavailable.</summary>
    public MediaPipeline? Pipeline { get; private set; }

    /// <summary>Existing thumbnails and peaks for drawing, or null while ffmpeg is unavailable.</summary>
    public IMediaArtifactSource? Artifacts { get; private set; }

    /// <summary>The frame cache shared by the frame server and playback, or null while ffmpeg cannot render.</summary>
    public FrameCache? FrameCache { get; private set; }

    /// <summary>The preview audio output: OpenAL, or the silent clock when no device opened. Null until startup ran.</summary>
    public IAudioOutput? Audio => _audio;

    /// <summary>Why OpenAL could not open, or null when a device is in use.</summary>
    public string? AudioFailure => _audioFailure;

    /// <summary>Plays the timeline, or null while ffmpeg cannot render.</summary>
    public PlaybackSession? Playback { get; private set; }

    /// <summary>
    /// Renders single timeline frames for agents (AGENT_INTEGRATION 2.6), sharing <see cref="FrameCache"/> but never the
    /// viewer's frame server, or null while ffmpeg cannot render.
    /// </summary>
    public StillRenderer? Stills { get; private set; }

    /// <summary>Reads mixed timeline audio for agent level and silence analysis, or null while ffmpeg cannot render.</summary>
    public RangeAudioReader? AudioReader { get; private set; }

    /// <summary>
    /// Builds the services for the current platform. Performs no file I/O, so it is safe on the UI thread: the
    /// registry is created with its shutdown hooks and the runner with its Job Object.
    /// </summary>
    /// <returns>The services.</returns>
    public static AppServices CreateDefault()
    {
        var paths = AppPaths.ForCurrentPlatform();
        var registry = ChildProcessRegistry.ForCurrentPlatform(paths);
        registry.InstallShutdownHooks();
        var runner = ProcessRunner.CreateDefault(registry);
        return new AppServices(paths, registry, runner);
    }

    /// <summary>Formats a byte count for the status bar, for example "3.1 GB".</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text.</returns>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>
    /// Runs the startup work off the UI thread: settings, orphan sweep, stale work files, bundled fonts, recent
    /// projects, ffmpeg discovery and validation, the capability probe and the cache size. Never throws for an
    /// ffmpeg problem; the result says what was found.
    /// </summary>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>The result to <see cref="Apply"/> on the UI thread.</returns>
    public Task<StartupResult> StartAsync(CancellationToken cancellationToken) =>
        Task.Run(() => StartCoreAsync(null, cancellationToken), cancellationToken);

    /// <summary>Detects ffmpeg again, preferring a path the user picked ("Locate ffmpeg...").</summary>
    /// <param name="ffmpegPath">The picked ffmpeg, or null to search again.</param>
    /// <param name="cancellationToken">Cancels the detection.</param>
    /// <returns>The result to <see cref="Apply"/> on the UI thread.</returns>
    public Task<StartupResult> RedetectAsync(string? ffmpegPath, CancellationToken cancellationToken) =>
        Task.Run(() => StartCoreAsync(ffmpegPath, cancellationToken), cancellationToken);

    /// <summary>
    /// Takes over a startup result on the UI thread and, when ffmpeg is ready and the services do not exist yet,
    /// creates the media cache, probe, frame index, posters, catalog, importer, thumbnails, waveforms, proxies, the job
    /// scheduler and pipeline, the export service, the shared frame cache, the frame server and the playback session.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="sink">Receives preview frames (the frame presenter).</param>
    public void Apply(StartupResult result, IVideoFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sink);
        Settings = result.Settings;
        if (FfmpegState == FfmpegState.Ready)
        {
            // Services already run on a working ffmpeg; a new path takes effect on the next start.
            return;
        }

        FfmpegState = result.State;
        Toolset = result.Toolset;
        Capabilities = result.Capabilities;
        if (result.State != FfmpegState.Ready || result.Toolset is not { } toolset || result.Capabilities is not { } caps)
        {
            return;
        }

        var settings = result.Settings;
        MediaCache = new MediaCache(Paths.CacheRoot, toolset.Version.BuildId, Math.Max(1, settings.CacheLimitGb) * MediaCache.BytesPerGb);
        // M7 tidy (TECH_BRIEF 6.5): probe.json and poster.jpg live in the media cache.
        Probe = new ProbeService(Runner, toolset, Work, MediaCache, Fingerprints);
        FrameIndex = new FrameIndexService(Runner, toolset, Work, MediaCache);
        Posters = new PosterService(Runner, toolset, Paths, MediaCache);
        Catalog = new MediaCatalog(Probe, FrameIndex);
        Importer = new MediaImporter(Probe, Posters, FrameIndex, Catalog, Ids, Fingerprints);
        Thumbnails = new ThumbnailService(Runner, toolset, Work, MediaCache, Catalog);
        Waveforms = new WaveformService(Runner, toolset, Work, MediaCache);
        Proxies = new ProxyService(Runner, toolset, caps, Work, MediaCache, FrameIndex, Probe, Catalog);

        // ArtifactReady is posted to the UI thread (Apply runs there).
        Scheduler = new JobScheduler(JobScheduler.DefaultConcurrency, SynchronizationContext.Current);
        Pipeline = new MediaPipeline(Scheduler, MediaCache, Catalog, FrameIndex, Thumbnails, Waveforms, Proxies)
        {
            ProxyPolicy = settings.ProxyPolicy,
        };
        Artifacts = new MediaArtifactSource(Thumbnails, Waveforms, Scheduler);
        Export = new ExportService(Runner, toolset, caps, Work, Fonts);

        // One frame cache serves scrub, prefetch and playback (TECH_BRIEF 8.1).
        FrameCache = new FrameCache();
        FrameServer = new FrameServer(Runner, toolset, caps, Work, Catalog, Fonts, sink, FrameCache);
        Playback = new PlaybackSession(Runner, toolset, caps, Work, Catalog, Fonts, EnsureAudio(settings), FrameCache);

        // AGENT_INTEGRATION 2.4: agent frames and audio next to the frame server, sharing its frame cache. The headless
        // host (Cutlass.Agent HeadlessEngine) repeats this UI-free part of Apply.
        Stills = new StillRenderer(Runner, toolset, caps, Work, Catalog, Fonts, FrameCache);
        AudioReader = new RangeAudioReader(Runner, toolset, caps, Work, Catalog);
    }

    /// <summary>Runs the startup cache maintenance off the UI thread (stale temps, size measure, LRU).</summary>
    /// <returns>A task that completes when done (never faults).</returns>
    public async Task RunCacheMaintenanceAsync()
    {
        if (Pipeline is not { } pipeline)
        {
            return;
        }

        try
        {
            await pipeline.RunStartupMaintenanceAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The LRU runs again after the next job.
        }
    }

    /// <summary>Saves the settings (off the UI thread by the store's async I/O).</summary>
    /// <returns>A task that completes when the file is written.</returns>
    public Task SaveSettingsAsync() => SettingsStore.SaveAsync(Settings);

    /// <summary>Stops the frame server and cancels exports, then kills any child process left.</summary>
    /// <returns>A task that completes when everything stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Playback is { } playback)
        {
            await playback.DisposeAsync().ConfigureAwait(false);
        }

        Pipeline?.Dispose();
        if (Scheduler is { } scheduler)
        {
            await scheduler.DisposeAsync().ConfigureAwait(false);
        }

        if (FrameServer is { } server)
        {
            await server.DisposeAsync().ConfigureAwait(false);
        }

        if (Export is { } export)
        {
            await export.DisposeAsync().ConfigureAwait(false);
        }

        lock (_audioGate)
        {
            _audio?.Dispose();
        }

        Catalog?.Dispose();
        Registry.KillAll();
        Registry.Dispose();
    }

    private IAudioOutput EnsureAudio(AppSettings settings)
    {
        lock (_audioGate)
        {
            if (_audio is null)
            {
                _audio = OpenAlAudioOutput.CreateOrFallback(settings.AudioDevice, out var failure);
                _audioFailure = _audio.IsAvailable ? null : failure ?? "No audio device";
            }

            return _audio;
        }
    }

    private static long DirectorySize(string root)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        long total = 0;
        try
        {
            foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                total += file.Length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A partial total is fine for a status bar summary.
        }

        return total;
    }

    private async Task<StartupResult> StartCoreAsync(string? pickedFfmpeg, CancellationToken cancellationToken)
    {
        AppSettings settings;
        IReadOnlyList<string> recent;
        try
        {
            Paths.EnsureDirectories();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StartupResult(new AppSettings(), [], FfmpegState.NotFound, "Cache folder unavailable", ex.Message, null, null, 0);
        }

        settings = await SettingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        recent = await RecentProjects.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            Registry.SweepOrphans();
            Work.EnsureCreated();
            Work.DeleteStale(DateTime.UtcNow);
            Fonts.EnsureBundledFonts();

            // Opening the OpenAL device can take a moment, so it happens here, off the UI thread (TECH_BRIEF 5.6).
            EnsureAudio(settings);
        }

        var cacheBytes = DirectorySize(Paths.CacheRoot);
        FfmpegToolset? toolset = null;
        string? failure = null;
        if (pickedFfmpeg is not null)
        {
            try
            {
                toolset = await FfmpegToolset.ValidateAsync(pickedFfmpeg, Runner, Work.Path, cancellationToken).ConfigureAwait(false);
            }
            catch (FfmpegException ex)
            {
                failure = ex.Message;
            }
        }

        toolset ??= await FfmpegLocator.ForCurrentPlatform()
            .FindAsync(pickedFfmpeg ?? settings.FfmpegPath, Runner, Work.Path, cancellationToken)
            .ConfigureAwait(false);
        if (toolset is null)
        {
            return new StartupResult(
                settings,
                recent,
                FfmpegState.NotFound,
                "ffmpeg not found",
                failure ?? (settings.FfmpegPath is { Length: > 0 } configured
                    // M7: a wrong path in the settings is named, so the user knows what to fix.
                    ? "The ffmpeg set in Settings (" + configured + ") was not found or does not work, and no other ffmpeg was found. Cutlass needs ffmpeg and ffprobe for import, preview and export."
                    : "Cutlass needs ffmpeg and ffprobe for import, preview and export."),
                null,
                null,
                cacheBytes);
        }

        FfmpegCapabilities caps;
        try
        {
            caps = await new CapabilityProbe(Runner, Work, Paths.CapabilitiesFile)
                .ProbeAsync(toolset, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FfmpegException ex)
        {
            return new StartupResult(settings, recent, FfmpegState.Unusable, "ffmpeg unusable", ex.Message, toolset, null, cacheBytes);
        }

        if (!caps.CanRender)
        {
            var missing = string.Join(", ", caps.MissingRequiredFilters);
            return new StartupResult(
                settings,
                recent,
                FfmpegState.Unusable,
                "ffmpeg " + toolset.Version.DisplayText + " cannot render",
                "This ffmpeg build lacks required filters: " + missing + ".",
                toolset,
                caps,
                cacheBytes);
        }

        var detail = toolset.FfmpegPath + (caps.HasDrawtext ? string.Empty : "\nNo drawtext filter: titles are disabled.");
        return new StartupResult(settings, recent, FfmpegState.Ready, "ffmpeg " + toolset.Version.DisplayText, detail, toolset, caps, cacheBytes);
    }
}
