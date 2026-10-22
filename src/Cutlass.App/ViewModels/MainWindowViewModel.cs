using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Serialization;
using Cutlass.Core.Session;
using Cutlass.Core.Time;
using Cutlass.Media.Export;
using Cutlass.Media.Jobs;
using Cutlass.Media.Settings;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The main window (DESIGN 3): the project session and its file commands (New, Open, Save, Save As with the
/// <c>.cutlass.user</c> sidecar and the dirty prompt), the panels' view models, the keyboard commands, the startup
/// sequence (ffmpeg discovery off the UI thread, then the command line) and the status bar.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private const string UntitledName = "Untitled";

    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly Action<Action> _post;
    private IReadOnlyList<string> _recent = [];
    private long _toastVersion;
    private bool _closing;
    private bool _catalogHooked;
    private bool _mediaHooked;
    private bool _restoringSettings;

    /// <summary>Creates the main window state with no project open (the welcome overlay shows).</summary>
    /// <param name="services">The composition root.</param>
    /// <param name="dialogs">The dialog service.</param>
    /// <param name="post">Runs an action on the UI thread; defaults to the Avalonia dispatcher.</param>
    public MainWindowViewModel(AppServices services, IDialogService dialogs, Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dialogs);
        _services = services;
        _dialogs = dialogs;
        _post = post ?? (action => Dispatcher.UIThread.Post(action));

        // A real clock for undo coalescing (TECH_BRIEF 3.4).
        Session = new ProjectSession(TimelineEdits.NewProject(services.Ids), () => Environment.TickCount64);
        Timeline = new TimelineViewModel(Session, services.Ids);
        Presenter = new FramePresenter();
        Viewer = new ViewerViewModel(Timeline, Presenter, _post);
        MediaBin = new MediaBinViewModel(Session, Timeline, dialogs, ShowToast)
        {
            SkipFileCheck = id => Volatile.Read(ref _untouchedMedia).Contains(id),
        };
        Inspector = new InspectorViewModel(Session, Timeline);
        EffectsTab = new EffectsViewModel(Timeline);
        Jobs = new JobsViewModel(dialogs, _post);
        Welcome = new WelcomeViewModel(NewProjectAsync, OpenProjectAsync, OpenRecentAsync, RemoveRecentAsync, LocateFfmpegAsync, DropOnWelcomeAsync)
        {
            ImportMediaRequested = ImportOnWelcomeAsync,
        };
        Commands = new CommandRegistry();
        Keys = new KeyBindingService(Commands, DefaultKeymap.Build(), () => Session.InGesture || Timeline.IsDragging, OperatingSystem.IsMacOS());
        Agent = new AgentViewModel(() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), dialogs);
        RegisterCommands();
        RegisterSettingsCommands();
        InitializeDataServices();
        InitializeAgent();

        ProjectName = UntitledName;
        FfmpegStatusText = "Looking for ffmpeg...";
        FfmpegToolTip = string.Empty;
        CacheSummary = "Cache";
        PlayheadText = Timecode.Format(0, Session.Current.Sequence.Settings.Rate);
        ToastText = string.Empty;
        TopRowFraction = 0.58;
        MediaColumnFraction = 0.22;
        InspectorColumnFraction = 0.22;

        Session.Changed += OnSessionChanged;
        Timeline.PropertyChanged += OnTimelinePropertyChanged;
        Viewer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewerViewModel.IsAudioUnavailable))
            {
                UpdateAudioStatus();
            }
        };
        // A write the agent lock's gate refused explains itself with its own throttled toast (MainWindowViewModel.Agent).
        Timeline.WarningRaised += (_, warning) =>
        {
            if (warning != ProjectSession.RefusedMessage)
            {
                ShowToast(warning);
            }
        };
        Timeline.ImportFiles = ImportForTimelineAsync;
        Timeline.ConfirmAsync = (title, message) => _dialogs.ConfirmAsync(title, message, "Remove");
        Timeline.MatchFrameRequested += OnMatchFrameRequested;
        MediaBin.OpenInSourceRequested = asset => Viewer.OpenSource(asset);
        Inspector.ShowSpeedDialog = dialogs.ShowSpeedDialogAsync;
        Inspector.ShowSequenceSettingsDialog = dialogs.ShowSequenceSettingsDialogAsync;
        Effects = EffectsTab;
        Timeline.Effects = EffectsTab;
        EffectsTab.DefaultTransitionChanged += OnDefaultTransitionChanged;
    }

    /// <summary>Raised when the window should close (Ctrl+Q, File > Exit); the view calls Close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised when the saved panel layout was loaded; the view applies the fractions.</summary>
    public event EventHandler? LayoutLoaded;

    /// <summary>The project session (UI thread only).</summary>
    public ProjectSession Session { get; }

    /// <summary>The timeline panel.</summary>
    public TimelineViewModel Timeline { get; }

    /// <summary>The viewer.</summary>
    public ViewerViewModel Viewer { get; }

    /// <summary>The frame presenter (preview frames to the viewer surface).</summary>
    public FramePresenter Presenter { get; }

    /// <summary>The media panel.</summary>
    public MediaBinViewModel MediaBin { get; }

    /// <summary>The inspector.</summary>
    public InspectorViewModel Inspector { get; }

    /// <summary>The jobs popover.</summary>
    public JobsViewModel Jobs { get; }

    /// <summary>The welcome overlay.</summary>
    public WelcomeViewModel Welcome { get; }

    /// <summary>
    /// The Effects tab content (DESIGN 4.2: transitions and title templates), shown in the media panel's EFFECTS tab
    /// through a ContentControl, or null until the effects view model is mounted.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEffects))]
    public partial object? Effects { get; set; }

    /// <summary>True when the Effects tab has content.</summary>
    public bool HasEffects => Effects is not null;

    /// <summary>The Effects tab (transitions, titles, fade presets; Ctrl+D, Ctrl+Shift+D and Ctrl+T run through it).</summary>
    public EffectsViewModel EffectsTab { get; }

    /// <summary>Every keyboard command by id.</summary>
    public CommandRegistry Commands { get; }

    /// <summary>Routes key presses to <see cref="Commands"/>.</summary>
    public KeyBindingService Keys { get; }

    /// <summary>True while a project is open (otherwise the welcome overlay shows).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWelcome))]
    [NotifyPropertyChangedFor(nameof(CanEditProject))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    public partial bool HasProject { get; set; }

    /// <summary>The project name shown in the menu row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleText))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial string ProjectName { get; set; }

    /// <summary>The project file, or null while untitled.</summary>
    [ObservableProperty]
    public partial string? ProjectPath { get; set; }

    /// <summary>True when the project has unsaved changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleText))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial bool IsDirty { get; set; }

    /// <summary>The ffmpeg part of the status bar.</summary>
    [ObservableProperty]
    public partial string FfmpegStatusText { get; set; }

    /// <summary>The ffmpeg tooltip (path, or why it is unusable).</summary>
    [ObservableProperty]
    public partial string FfmpegToolTip { get; set; }

    /// <summary>True when ffmpeg is ready (green dot).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    public partial bool IsFfmpegOk { get; set; }

    /// <summary>True when ffmpeg is missing or unusable (red dot, settings hint).</summary>
    [ObservableProperty]
    public partial bool IsFfmpegError { get; set; }

    /// <summary>The cache part of the status bar.</summary>
    [ObservableProperty]
    public partial string CacheSummary { get; set; }

    /// <summary>True when OpenAL could not open a device (amber "Audio unavailable" in the status bar, DESIGN 6).</summary>
    [ObservableProperty]
    public partial bool IsAudioUnavailable { get; set; }

    /// <summary>The audio device, or why it is unavailable.</summary>
    [ObservableProperty]
    public partial string AudioToolTip { get; set; } = string.Empty;

    /// <summary>The editable playhead timecode of the timeline toolbar.</summary>
    [ObservableProperty]
    public partial string PlayheadText { get; set; }

    /// <summary>A short message shown over the window for a few seconds.</summary>
    [ObservableProperty]
    public partial string ToastText { get; set; }

    /// <summary>True while the toast shows.</summary>
    [ObservableProperty]
    public partial bool IsToastVisible { get; set; }

    /// <summary>The top row's share of the height (saved in settings).</summary>
    [ObservableProperty]
    public partial double TopRowFraction { get; set; }

    /// <summary>The media column's share of the width (saved in settings).</summary>
    [ObservableProperty]
    public partial double MediaColumnFraction { get; set; }

    /// <summary>The inspector column's share of the width (saved in settings).</summary>
    [ObservableProperty]
    public partial double InspectorColumnFraction { get; set; }

    /// <summary>True once the services were stopped (after the close prompt or a smoke run); the window then closes
    /// without asking again.</summary>
    public bool HasShutDown { get; private set; }

    /// <summary>The export queued last (the smoke runner waits for it), or null.</summary>
    public ExportJob? LastExport { get; private set; }

    /// <summary>True when the welcome overlay shows.</summary>
    public bool ShowWelcome => !HasProject;

    /// <summary>Project name with <c>*</c> when there are unsaved changes.</summary>
    public string TitleText => IsDirty ? ProjectName + " *" : ProjectName;

    /// <summary>The OS window title.</summary>
    public string WindowTitle => HasProject ? TitleText + " - Cutlass" : "Cutlass";

    /// <summary>The sequence format, for example <c>1920x1080 29.97 48k</c>.</summary>
    public string SequenceFormatText
    {
        get
        {
            var s = Session.Current.Sequence.Settings;
            return string.Create(CultureInfo.InvariantCulture, $"{s.Width}x{s.Height} {s.Rate.DisplayName} {TimeMath.SampleRate / 1000}k");
        }
    }

    /// <summary>
    /// Runs startup: ffmpeg discovery and the capability probe off the UI thread, then the command line (open a
    /// project, import files).
    /// </summary>
    /// <param name="options">The parsed command line.</param>
    /// <returns>A task that completes when startup finished.</returns>
    public async Task InitializeAsync(CommandLineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        StartupResult result;
        try
        {
            result = await _services.StartAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            FfmpegStatusText = "Startup failed";
            FfmpegToolTip = ex.Message;
            IsFfmpegError = true;

            // DESIGN 11: a project can still be edited structurally (and titles added) without the media services,
            // so autosave runs anyway; a failed write only shows a toast. A missing ffmpeg does not land here (it
            // is a normal startup result), only an unusable data or work folder.
            Autosave.Interval = AutosaveService.IntervalFromMinutes(_services.Settings.AutosaveIntervalMinutes);
            Autosave.Start();
            return;
        }

        ApplyStartup(result);
        _recent = result.RecentProjects;
        await RefreshRecentAsync();
        await StartDataServicesAsync(options);

        if (options.ProjectPath is { } project)
        {
            await OpenPathAsync(Path.GetFullPath(project));
        }

        if (options.ImportPaths.Count > 0)
        {
            var paths = options.ImportPaths.Select(Path.GetFullPath).ToList();
            if (HasProject)
            {
                await MediaBin.ImportPathsAsync(paths);
            }
            else
            {
                await DropOnWelcomeAsync(paths);
            }
        }
    }

    /// <summary>
    /// Asks to save a dirty project, saves the view state and the settings, and stops the services. Returns false
    /// when the user cancelled.
    /// </summary>
    /// <returns>True when the window may close.</returns>
    public async Task<bool> PrepareCloseAsync()
    {
        if (_closing)
        {
            return true;
        }

        // AGENT_INTEGRATION 3.5: closing while an agent holds the project takes over first, then the save prompt runs.
        if (Agent.IsLocked)
        {
            var name = Agent.State.AgentName ?? "An agent";
            if (!await _dialogs.ConfirmAsync("Quit Cutlass", name + " is editing. Take over and close?", "Take over and close"))
            {
                return false;
            }

            Agent.TakeOverCommand.Execute(null);
        }

        if (Jobs.HasActiveJobs &&
            !await _dialogs.ConfirmAsync("Quit Cutlass", "An export is still running. Cancel it and quit?", "Cancel export and quit"))
        {
            return false;
        }

        if (!await ConfirmDiscardAsync())
        {
            return false;
        }

        _closing = true;
        await StopAgentRuntimeAsync();
        await StopDataServicesAsync();
        await SaveViewStateAsync();
        _services.Settings.TopRowFraction = TopRowFraction;
        _services.Settings.MediaColumnFraction = MediaColumnFraction;
        _services.Settings.InspectorColumnFraction = InspectorColumnFraction;
        _services.Settings.DefaultPreviewQuality = Viewer.Quality;
        _services.Settings.UseProxies = Viewer.UseProxies;
        try
        {
            await _services.SaveSettingsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the layout is not worth blocking quit.
        }

        Viewer.StopPlayback(park: false, reason: "the window closed");
        await _services.DisposeAsync();
        HasShutDown = true;
        return true;
    }

    /// <summary>
    /// Stops the services without any prompt and without saving the view state or settings (the end of a smoke
    /// run). Afterwards the window closes without asking.
    /// </summary>
    /// <returns>A task that completes when the services stopped.</returns>
    public async Task ShutdownWithoutPromptAsync()
    {
        if (HasShutDown)
        {
            return;
        }

        _closing = true;
        await StopAgentRuntimeAsync();
        await StopDataServicesAsync();
        Viewer.StopPlayback(park: false, reason: "the smoke run ended");
        await _services.DisposeAsync();
        HasShutDown = true;
    }

    /// <summary>Opens a project file and its sidecar (also used by the smoke runner).</summary>
    /// <param name="path">The project file.</param>
    /// <returns>True when the project opened.</returns>
    public Task<bool> OpenProjectFileAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return OpenPathAsync(Path.GetFullPath(path));
    }

    /// <summary>
    /// Creates the export dialog state for the current project, or null while ffmpeg cannot export. Export in the
    /// dialog queues the job on the export service and in the jobs popover.
    /// </summary>
    /// <returns>The dialog view model, or null.</returns>
    public ExportDialogViewModel? CreateExportDialog()
    {
        if (_services.Capabilities is not { } caps || _services.Catalog is not { } catalog || _services.Toolset is not { } toolset)
        {
            return null;
        }

        // DESIGN 10: the export folder and preset from Settings, which every queued export updates.
        var folder = _services.Settings.ExportFolder ?? (ProjectPath is { } path
            ? Path.GetDirectoryName(path) ?? string.Empty
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        if (string.IsNullOrEmpty(folder))
        {
            folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        var fonts = _services.Fonts;
        return new ExportDialogViewModel(
            Session.Current,
            caps,
            ProjectName,
            folder,
            _dialogs,
            catalog,
            (request, token) => ExportCommandPreview.BuildAsync(request, toolset, caps, fonts, token),
            request => StartExport(request) && RememberExportChoices(request),
            _services.Settings.ExportPreset);
    }

    /// <summary>Records the panel layout from the view (GridSplitter positions).</summary>
    /// <param name="topRow">The top row's share of the height.</param>
    /// <param name="mediaColumn">The media column's share of the width.</param>
    /// <param name="inspectorColumn">The inspector column's share of the width.</param>
    public void UpdateLayout(double topRow, double mediaColumn, double inspectorColumn)
    {
        TopRowFraction = ClampFraction(topRow, 0.58);
        MediaColumnFraction = ClampFraction(mediaColumn, 0.22);
        InspectorColumnFraction = ClampFraction(inspectorColumn, 0.22);
    }

    /// <summary>Imports files dropped on the media panel.</summary>
    /// <param name="paths">The dropped paths.</param>
    /// <returns>A task that completes when the import finished.</returns>
    public Task DropOnBinAsync(IReadOnlyList<string> paths)
    {
        if (!HasProject)
        {
            return DropOnWelcomeAsync(paths);
        }

        return MediaBin.ImportPathsAsync(paths);
    }

    /// <summary>Shows a short message for four seconds.</summary>
    /// <param name="message">The message.</param>
    public void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var version = ++_toastVersion;
        ToastText = message;
        IsToastVisible = true;
        _ = HideToastLaterAsync(version);
    }

    // The agent server stops before the services it uses (endpoint.json is deleted on a clean shutdown).
    private async Task StopAgentRuntimeAsync()
    {
        if (AgentRuntime is { } runtime)
        {
            AgentRuntime = null;
            await runtime.DisposeAsync();
        }
    }

    private static double ClampFraction(double value, double fallback) =>
        double.IsFinite(value) && value > 0.05 && value < 0.95 ? value : fallback;

    private static PreviewQuality ParseQuality(string value) => value switch
    {
        "full" => PreviewQuality.Full,
        "half" => PreviewQuality.Half,
        "quarter" => PreviewQuality.Quarter,
        _ => PreviewQuality.Auto,
    };

    private static string FormatQuality(PreviewQuality value) => value switch
    {
        PreviewQuality.Full => "full",
        PreviewQuality.Half => "half",
        PreviewQuality.Quarter => "quarter",
        _ => "auto",
    };

    private async Task HideToastLaterAsync(long version)
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (version == _toastVersion)
        {
            IsToastVisible = false;
        }
    }

    private void ApplyStartup(StartupResult result)
    {
        _services.Apply(result, Presenter);
        var ready = _services.FfmpegState == FfmpegState.Ready;
        IsFfmpegOk = ready;
        IsFfmpegError = !ready;
        FfmpegStatusText = ready ? result.StatusText : result.StatusText + " (Locate ffmpeg on the welcome screen)";
        FfmpegToolTip = result.Detail ?? string.Empty;
        Welcome.IsFfmpegOk = ready;
        Welcome.IsFfmpegMissing = !ready;
        Welcome.FfmpegStatusText = ready ? "Found " + result.StatusText : result.StatusText + ". " + result.Detail;

        MediaBin.Importer = _services.Importer;
        MediaBin.Posters = _services.Posters;
        MediaBin.Artifacts = _services.Artifacts;
        MediaBin.ImportUnavailableReason = ready
            ? string.Empty
            : "Import needs ffmpeg. " + WelcomeViewModel.InstallHint;
        Viewer.AttachFrameServer(_services.FrameServer);
        Timeline.Artifacts = _services.Artifacts;
        Viewer.Artifacts = _services.Artifacts;
        HookMediaServices();
        if (_services.Catalog is { } catalog)
        {
            if (!_catalogHooked)
            {
                // A frame rendered before the media's index existed used the CFR fallback (TECH_BRIEF 8.1 M4 scope):
                // ask again once the index is ready. IndexReady is raised on a worker thread.
                _catalogHooked = true;
                catalog.IndexReady += _ => _post(Viewer.Refresh);

                // A proxy attached or removed: the bin shows the P chip and the viewer renders from the new source.
                catalog.ProxyChanged += id => _post(() =>
                {
                    MediaBin.SetProxyReady(id, catalog.Get(id)?.Proxy is not null);
                    if (Viewer.UseProxies)
                    {
                        Viewer.Refresh();
                    }
                });
            }

            // DESIGN 11: media left untouched by the open scan register offline without a file lookup.
            catalog.KnownOffline = id => Volatile.Read(ref _untouchedMedia).Contains(id);
            catalog.Sync(Session.Current);
        }

        if (HasProject)
        {
            EnqueueProjectMedia(Session.Current);
        }

        MediaBin.LoadMissingPosters();
        CacheSummary = "Cache " + AppServices.FormatBytes(result.CacheBytes) + " / " +
            result.Settings.CacheLimitGb.ToString(CultureInfo.InvariantCulture) + " GB";
        UpdateAudioStatus();

        var settings = result.Settings;
        Viewer.Quality = settings.DefaultPreviewQuality;
        Viewer.UseProxies = settings.UseProxies;
        ApplyEditingDefaults(settings);
        _restoringSettings = true;
        try
        {
            EffectsTab.DefaultTransitionKind = EffectsViewModel.ParseDefaultTransition(settings.DefaultTransition);
        }
        finally
        {
            _restoringSettings = false;
        }

        UpdateLayout(settings.TopRowFraction, settings.MediaColumnFraction, settings.InspectorColumnFraction);
        LayoutLoaded?.Invoke(this, EventArgs.Empty);
        ExportCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
    }

    // DESIGN 4.2: the default transition (double-click in the Effects tab) is a user setting, saved when it changes.
    private async void OnDefaultTransitionChanged(object? sender, EventArgs e)
    {
        var name = EffectsTab.DefaultTransitionKind.ToString();
        if (_restoringSettings || _services.Settings.DefaultTransition == name)
        {
            return;
        }

        _services.Settings.DefaultTransition = name;
        try
        {
            await _services.SaveSettingsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("The default transition could not be saved: " + ex.Message);
        }
    }

    // The status-bar badge: OpenAL never opened, or the viewer reports a device lost during a run (re-checked after every
    // play start, since a start may reopen the device).
    private void UpdateAudioStatus()
    {
        IsAudioUnavailable = _services.Audio is { IsAvailable: false } || Viewer.IsAudioUnavailable;
        AudioToolTip = _services.AudioFailure is { } audioFailure
            ? "Playback uses a silent clock. OpenAL: " + audioFailure
            : IsAudioUnavailable
                ? "The audio device was lost. Playback uses a silent clock until the next play start reopens it."
                : "Audio output: " + (_services.Audio?.DeviceName ?? "none");
    }

    // Once ffmpeg works: playback, the jobs popover, proxy progress in the bin, import jobs and the cache summary.
    private void HookMediaServices()
    {
        if (_mediaHooked || _services.Scheduler is not { } scheduler)
        {
            return;
        }

        _mediaHooked = true;
        Viewer.AttachPlayback(_services.Playback is { } playback ? new PlaybackEngine(playback) : null, scheduler);
        Jobs.AttachScheduler(scheduler);
        Jobs.MediaJobChanged += (_, info) =>
        {
            if (info.Kind == JobKind.Proxy)
            {
                MediaBin.SetProxyProgress(info.MediaId, !info.IsFinished, info.Fraction);
            }
        };

        // New media gets its index, thumbnails, waveform and (per the proxy policy) proxy jobs, visible first.
        MediaBin.AssetImported = asset => _services.Pipeline?.Enqueue(asset, JobPriority.Visible);
        MediaBin.CreateProxyRequested = asset => _services.Pipeline?.EnqueueProxy(asset, JobPriority.Visible);
        MediaBin.DeleteProxyRequested = asset => _services.Proxies?.DeleteAsync(asset) ?? Task.CompletedTask;
        if (_services.MediaCache is { } cache)
        {
            cache.UsageChanged += () => _post(UpdateCacheSummary);
            _ = MaintainCacheAsync();
        }
    }

    private async Task MaintainCacheAsync()
    {
        await _services.RunCacheMaintenanceAsync();
        UpdateCacheSummary();
    }

    private void UpdateCacheSummary()
    {
        if (_services.MediaCache is { } cache)
        {
            CacheSummary = "Cache " + AppServices.FormatBytes(cache.UsedBytes) + " / " + AppServices.FormatBytes(cache.LimitBytes);
        }
    }

    private void UpdateExportActive()
    {
        if (_services.Scheduler is { } scheduler)
        {
            // TECH_BRIEF 6.7: an export cancels a running proxy, re-queues it and drops the jobs to one process.
            scheduler.IsExportActive = Jobs.Jobs.Any(j => j.Kind == "Export" && j.IsActive);
        }
    }

    private void OnSessionChanged(object? sender, ProjectChangedEventArgs e)
    {
        IsDirty = HasProject && Session.IsDirty;
        if (e.Old.Media != e.New.Media && _services.Catalog is { } catalog)
        {
            catalog.Sync(e.New);
        }

        if (e.Old.Sequence.Settings != e.New.Sequence.Settings)
        {
            OnPropertyChanged(nameof(SequenceFormatText));
            PlayheadText = Timecode.Format(Timeline.Playhead, e.New.Sequence.Settings.Rate);
        }
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.Playhead))
        {
            PlayheadText = Timecode.Format(Timeline.Playhead, Session.Current.Sequence.Settings.Rate);
        }
    }

    private void StartProject(Project project, string? path)
    {
        Viewer.StopPlayback(park: false, reason: "another project was opened");
        Viewer.Source.Reset();
        Viewer.ShowTimeline();
        Presenter.Clear();

        // Before the session changes: the bin and the catalog react to the replace and must already skip the media
        // the open scan left untouched (DESIGN 11).
        SetUntouchedMedia(_pendingScan);

        // A recovered autosave (DESIGN 11) starts dirty: its changes are not in the project file yet.
        Session.Replace(project, markSaved: !_openingRecovered);
        EnqueueProjectMedia(project);
        ProjectPath = path;
        ProjectName = path is null ? UntitledName : Path.GetFileNameWithoutExtension(path);
        Timeline.Select([], additive: false);
        Timeline.Playhead = 0;
        HasProject = true;
        IsDirty = Session.IsDirty;
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(SequenceFormatText));
        MediaBin.SyncFromProject(project);
        Viewer.Refresh();
        AfterProjectStarted();
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!HasProject || !Session.IsDirty)
        {
            return true;
        }

        return await _dialogs.AskSaveChangesAsync(ProjectName) switch
        {
            SaveChangesChoice.Save => await SaveCoreAsync(forceDialog: false),
            SaveChangesChoice.Discard => true,
            _ => false,
        };
    }

    private async Task NewProjectAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        await SaveViewStateAsync();
        StartProject(TimelineEdits.NewProject(_services.Ids), null);
    }

    private async Task OpenProjectAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        if (await _dialogs.PickProjectToOpenAsync() is { } path)
        {
            await OpenPathAsync(path);
        }
    }

    private async Task OpenRecentAsync(string path)
    {
        if (await ConfirmDiscardAsync())
        {
            await OpenPathAsync(path);
        }
    }

    private async Task<bool> OpenPathAsync(string path)
    {
        Project project;
        ViewState view;
        try
        {
            // The project or its newer autosave (DESIGN 11), with moved, changed and missing media checked.
            project = await LoadForOpenAsync(path);
            view = await _services.Projects.LoadViewStateAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            // A damaged file or a newer schema: the InvalidDataException message says which, in plain words.
            await _dialogs.ShowErrorAsync(new ErrorDialogViewModel(
                "Open project",
                $"{Path.GetFileName(path)} could not be opened. {ex.Message}",
                null,
                null));
            return false;
        }

        await SaveViewStateAsync();
        StartProject(project, path);
        ApplyViewState(view);
        await AddRecentAsync(path);
        return true;
    }

    private void ApplyViewState(ViewState view)
    {
        var state = view.Sanitize();
        var viewState = Timeline.ViewState;
        foreach (var (trackId, height) in state.TrackHeights)
        {
            viewState.SetTrackHeight(trackId, height);
        }

        viewState.SetZoom(state.PixelsPerFrame, 0);
        viewState.ScrollToFrame(state.ScrollFrame);
        viewState.SetScrollY(state.ScrollY);
        Timeline.Playhead = state.Playhead;
        Viewer.Quality = ParseQuality(state.PreviewQuality);
        Viewer.UseProxies = state.UseProxies;
        MediaBin.IsListView = state.BinView == "list";
    }

    private ViewState CaptureViewState()
    {
        var viewState = Timeline.ViewState;
        var heights = ImmutableDictionary.CreateBuilder<string, double>(StringComparer.Ordinal);
        foreach (var track in Session.Current.Sequence.Tracks)
        {
            heights[track.Id] = viewState.TrackHeight(track.Id, track.Kind);
        }

        return new ViewState(
            viewState.PixelsPerFrame,
            (long)Math.Floor(viewState.ScrollFrame),
            viewState.ScrollY,
            Timeline.Playhead,
            heights.ToImmutable(),
            FormatQuality(Viewer.Quality),
            Viewer.UseProxies,
            MediaBin.IsListView ? "list" : "grid");
    }

    private async Task SaveViewStateAsync()
    {
        if (!HasProject || ProjectPath is not { } path)
        {
            return;
        }

        try
        {
            await _services.Projects.SaveViewStateAsync(CaptureViewState(), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The sidecar only holds navigation; failing to write it never blocks the user.
        }
    }

    private async Task<bool> SaveCoreAsync(bool forceDialog)
    {
        var path = ProjectPath;
        if (forceDialog || path is null)
        {
            path = await _dialogs.PickProjectSavePathAsync(ProjectName, ProjectPath is null ? null : Path.GetDirectoryName(ProjectPath));
            if (path is null)
            {
                return false;
            }

            if (!path.EndsWith(ProjectSerializer.ProjectExtension, StringComparison.OrdinalIgnoreCase))
            {
                path += ProjectSerializer.ProjectExtension;
            }
        }

        return await SaveToPathAsync(path);
    }

    /// <summary>Saves the project and its sidecar to a path, marks it clean and adds it to the recent list (also
    /// used by the smoke runner).</summary>
    /// <param name="path">The project file.</param>
    /// <returns>True when the project was written.</returns>
    public async Task<bool> SaveToPathAsync(string path)
    {
        if (await SaveToPathCoreAsync(path) is { } error)
        {
            await _dialogs.ShowMessageAsync("Save project", $"The project could not be saved.\n\n{error}");
            return false;
        }

        return true;
    }

    // Save without a dialog: null on success, else why the file could not be written (the agent's save reports it).
    private async Task<string?> SaveToPathCoreAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = Path.GetFullPath(path);
        var snapshot = Session.Current;
        try
        {
            await _services.Projects.SaveAsync(snapshot, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        // Edits made while the file was written stay dirty.
        if (ReferenceEquals(Session.Current, snapshot))
        {
            Session.MarkSaved();
        }

        ProjectPath = path;
        ProjectName = Path.GetFileNameWithoutExtension(path);
        IsDirty = Session.IsDirty;
        OnPropertyChanged(nameof(WindowTitle));
        // DESIGN 11: a clean save deletes the autosave (and an untitled project's one after Save As).
        await Autosave.ClearAsync(includeProjectAutosave: true);
        await SaveViewStateAsync();
        await AddRecentAsync(path);
        ShowToast("Saved " + Path.GetFileName(path));
        return null;
    }

    private async Task AddRecentAsync(string path)
    {
        _recent = RecentProjectsStore.Touch(_recent, Path.GetFullPath(path), _services.Paths.PathComparer);
        try
        {
            await _services.RecentProjects.SaveAsync(_recent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The recent list is a convenience.
        }

        await RefreshRecentAsync();
    }

    private async Task RemoveRecentAsync(string path)
    {
        _recent = [.. _recent.Where(p => !_services.Paths.PathComparer.Equals(p, path))];
        try
        {
            await _services.RecentProjects.SaveAsync(_recent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The recent list is a convenience.
        }

        await RefreshRecentAsync();
    }

    private async Task RefreshRecentAsync()
    {
        var paths = _recent.ToList();
        var items = await Task.Run(() => paths.Select(p => new RecentProjectItem(p, File.Exists(p))).ToList());
        Welcome.SetRecent(items);
    }

    [RelayCommand]
    private async Task LocateFfmpegAsync()
    {
        if (await _dialogs.PickFfmpegAsync() is not { } path)
        {
            return;
        }

        FfmpegStatusText = "Checking " + path;
        var result = await _services.RedetectAsync(path, CancellationToken.None);
        if (result.State == FfmpegState.Ready && result.Toolset is { } toolset)
        {
            _services.Settings.FfmpegPath = toolset.FfmpegPath;
            result = result with { Settings = _services.Settings };
            try
            {
                await _services.SaveSettingsAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowToast("The ffmpeg location could not be saved: " + ex.Message);
            }
        }

        ApplyStartup(result);
        if (result.State != FfmpegState.Ready)
        {
            await _dialogs.ShowMessageAsync("Locate ffmpeg", result.StatusText + "\n\n" + result.Detail);
        }
    }

    private async Task DropOnWelcomeAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        if (!HasProject)
        {
            StartProject(TimelineEdits.NewProject(_services.Ids), null);
        }

        var generation = Timeline.ProjectGeneration;
        var assets = await MediaBin.ImportPathsAsync(paths);
        if (generation != Timeline.ProjectGeneration)
        {
            // Another project became current during the import; the drop belonged to the old one.
            return;
        }

        // DESIGN 4.1: dropped media is placed end to end on V1/A1.
        foreach (var asset in assets)
        {
            Timeline.Playhead = Timeline.SequenceEnd;
            Timeline.OverwriteMediaAtPlayhead(asset.Id);
        }

        if (assets.Count > 0)
        {
            Timeline.Playhead = 0;
            Timeline.ZoomToFitCommand.Execute(null);
        }
    }

    // "Import media..." on the welcome overlay: the file dialog, then the same as a drop (the way in on Linux, where
    // OS file drops do not arrive).
    private async Task ImportOnWelcomeAsync()
    {
        var files = await _dialogs.PickMediaFilesAsync();
        if (files.Count > 0)
        {
            await DropOnWelcomeAsync(files);
        }
    }

    // A file drop onto the timeline: imports what is new and answers the media ids of every dropped file in order,
    // including files the project already holds (they are placed again, not imported twice).
    private async Task<IReadOnlyList<string>> ImportForTimelineAsync(IReadOnlyList<string> paths)
    {
        if (!HasProject)
        {
            return [];
        }

        var generation = Timeline.ProjectGeneration;
        var imported = await MediaBin.ImportPathsAsync(paths);
        if (generation != Timeline.ProjectGeneration)
        {
            return [];
        }

        var comparer = _services.Paths.PathComparer;
        var ids = new List<string>();
        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);
            if (Session.Current.Media.FirstOrDefault(m => comparer.Equals(Path.GetFullPath(m.Path), full)) is { } media &&
                !ids.Contains(media.Id))
            {
                ids.Add(media.Id);
            }
        }

        // Dropped folders contribute the files they imported.
        ids.AddRange(imported.Select(a => a.Id).Where(id => !ids.Contains(id)));
        return ids;
    }

    private bool CanUseProject() => HasProject;

    private bool CanUseFfmpeg() => HasProject && IsFfmpegOk;

    private bool CanImportMedia() => CanUseFfmpeg() && !Agent.IsLocked;

    [RelayCommand(CanExecute = nameof(CanReplaceProject))]
    private Task NewAsync() => NewProjectAsync();

    [RelayCommand(CanExecute = nameof(CanReplaceProject))]
    private Task OpenAsync() => OpenProjectAsync();

    [RelayCommand(CanExecute = nameof(CanUseProject))]
    private Task SaveAsync() => SaveCoreAsync(forceDialog: false);

    [RelayCommand(CanExecute = nameof(CanUseProject))]
    private Task SaveAsAsync() => SaveCoreAsync(forceDialog: true);

    [RelayCommand(CanExecute = nameof(CanImportMedia))]
    private async Task ImportAsync()
    {
        var files = await _dialogs.PickMediaFilesAsync();
        if (files.Count > 0)
        {
            await MediaBin.ImportPathsAsync(files);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseFfmpeg))]
    private async Task ExportAsync()
    {
        if (CreateExportDialog() is not { } dialog)
        {
            await _dialogs.ShowMessageAsync("Export", "Export needs ffmpeg. " + WelcomeViewModel.InstallHint);
            return;
        }

        if (TimelineQuery.Duration(Session.Current) == 0)
        {
            ShowToast("Nothing to export: the timeline is empty.");
            return;
        }

        await _dialogs.ShowExportDialogAsync(dialog);
    }

    private bool StartExport(ExportRequest request) => StartExportCore(request, byAgent: false) is not null;

    // Queues an export in the Jobs popover. The user's exports block agent acquisition until they finish
    // (AGENT_INTEGRATION 3.2); an agent's never do.
    private ExportJob? StartExportCore(ExportRequest request, bool byAgent)
    {
        if (_services.Export is not { } export)
        {
            return null;
        }

        var job = export.Enqueue(request);
        LastExport = job;
        if (!byAgent)
        {
            _userExports.Add(job);
            RaiseAgentUserStateChanged();
        }

        Jobs.AddExport(job);
        UpdateExportActive();
        job.Completion.ContinueWith(
            _ => _post(() =>
            {
                UpdateExportActive();
                ForgetUserExport(job);
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        ShowToast((byAgent ? "Agent export queued: " : "Export queued: ") + Path.GetFileName(job.OutputPath));
        return job;
    }

    [RelayCommand]
    private void Quit() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void CommitPlayheadText()
    {
        var rate = Session.Current.Sequence.Settings.Rate;
        if (TimecodeEntry.TryResolve(PlayheadText, Timeline.Playhead, rate, out var frame))
        {
            Timeline.Playhead = frame;
        }

        PlayheadText = Timecode.Format(Timeline.Playhead, rate);
    }

    /// <summary>
    /// Runs a keymap command by id (<see cref="CommandIds"/>). The menus use it, so a menu item and its shortcut always
    /// do the same thing, including the routing to the Source monitor.
    /// </summary>
    /// <param name="id">The command id.</param>
    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private void RunCommand(string? id)
    {
        if (id is not null)
        {
            Commands.TryExecute(id);
        }
    }

    partial void OnEffectsChanged(object? value) => MediaBin.Effects = value;

    // F: open the clip under the playhead in Source at the matching frame (DESIGN 5.5).
    private void OnMatchFrameRequested(object? sender, MatchFrameEventArgs e)
    {
        if (TimelineQuery.FindMedia(Session.Current, e.AssetId) is { } asset)
        {
            Viewer.OpenSource(asset, e.SourceFlicks);
        }
    }

    private void RegisterCommands()
    {
        var c = Commands;

        // Transport (DESIGN 6). Without a project the keys are still claimed, so Space never clicks the focused button.
        // The viewer routes them to the monitor shown (Timeline or Source, DESIGN 4.3).
        c.Register(CommandIds.PlayPause, () => { if (HasProject) { Viewer.PlayPauseCommand.Execute(null); } });
        c.Register(CommandIds.PlayInToOut, () => { if (HasProject) { Viewer.PlayInToOutCommand.Execute(null); } });
        c.Register(CommandIds.ShuttleForward, () => { if (HasProject) { Viewer.ShuttleForwardCommand.Execute(null); } });
        c.Register(CommandIds.Pause, () => { if (HasProject) { Viewer.PauseCommand.Execute(null); } });

        // Navigation and marks follow the monitor shown: in Source mode they move the Source playhead and set the
        // Source In/Out (DESIGN 4.3), else the timeline's.
        RegisterRouted(CommandIds.ShuttleReverse, () => Viewer.Source.Step(-1), Timeline.StepBackwardCommand);
        RegisterRouted(CommandIds.StepBackward, () => Viewer.Source.Step(-1), Timeline.StepBackwardCommand);
        RegisterRouted(CommandIds.StepForward, () => Viewer.Source.Step(1), Timeline.StepForwardCommand);
        RegisterRouted(CommandIds.StepBackward10, () => Viewer.Source.Step(-10), new RelayCommand(() => Timeline.Playhead = Math.Max(0, Timeline.Playhead - 10)));
        RegisterRouted(CommandIds.StepForward10, () => Viewer.Source.Step(10), new RelayCommand(() => Timeline.Playhead += 10));
        // The Source media has no edit points: Up and Down do nothing there rather than move the hidden timeline playhead.
        RegisterRouted(CommandIds.PreviousEdit, () => { }, Timeline.PreviousEditCommand);
        RegisterRouted(CommandIds.NextEdit, () => { }, Timeline.NextEditCommand);
        RegisterRouted(CommandIds.GoToStart, Viewer.Source.GoToStart, Timeline.GoToStartCommand);
        RegisterRouted(CommandIds.GoToEnd, Viewer.Source.GoToEnd, Timeline.GoToEndCommand);
        RegisterRouted(CommandIds.GoToIn, Viewer.Source.GoToIn, Timeline.GoToInCommand);
        RegisterRouted(CommandIds.GoToOut, Viewer.Source.GoToOut, Timeline.GoToOutCommand);
        RegisterRouted(CommandIds.MarkIn, Viewer.Source.MarkIn, Timeline.SetInCommand);
        RegisterRouted(CommandIds.MarkOut, Viewer.Source.MarkOut, Timeline.SetOutCommand);
        RegisterRouted(CommandIds.ClearInOut, Viewer.Source.ClearInOut, Timeline.ClearInOutCommand);
        RegisterRouted(CommandIds.MarkClip, Viewer.Source.MarkAll, new RelayCommand(MarkClipUnderPlayhead));
        RegisterTimeline(CommandIds.AddMarker, Timeline.AddMarkerCommand);
        RegisterProject(CommandIds.NextMarker, () => JumpToMarker(forward: true));
        RegisterProject(CommandIds.PreviousMarker, () => JumpToMarker(forward: false));

        RegisterTimeline(CommandIds.ToolSelect, Timeline.SelectToolCommand, EditTool.Select);
        RegisterTimeline(CommandIds.ToolRipple, Timeline.SelectToolCommand, EditTool.Ripple);
        RegisterTimeline(CommandIds.ToolRoll, Timeline.SelectToolCommand, EditTool.Roll);
        RegisterTimeline(CommandIds.ToolSlip, Timeline.SelectToolCommand, EditTool.Slip);
        RegisterTimeline(CommandIds.ToolRazor, Timeline.SelectToolCommand, EditTool.Razor);
        RegisterTimeline(CommandIds.ToggleSnap, Timeline.ToggleSnapCommand);

        RegisterTimeline(CommandIds.Split, Timeline.SplitAtPlayheadCommand);
        RegisterProject(CommandIds.SplitAll, SplitAllUnlocked);
        RegisterTimeline(CommandIds.Lift, Timeline.DeleteSelectionCommand);
        RegisterProject(CommandIds.RippleDelete, RippleDelete);
        RegisterTimeline(CommandIds.RippleTrimStart, Timeline.TrimStartToPlayheadCommand);
        RegisterTimeline(CommandIds.RippleTrimEnd, Timeline.TrimEndToPlayheadCommand);
        RegisterProject(CommandIds.NudgeLeft, () => NudgeSelection(-1));
        RegisterProject(CommandIds.NudgeRight, () => NudgeSelection(1));

        // Comma and period place the Source range (DESIGN 5.5). With nothing open in Source they fall back to the
        // whole media selected in the bin, the M4 behaviour.
        RegisterProject(CommandIds.Insert, () => EditFromSourceOrBin(insert: true));
        RegisterProject(CommandIds.Overwrite, () => EditFromSourceOrBin(insert: false));
        RegisterTimeline(CommandIds.MatchFrame, Timeline.MatchFrameCommand);
        RegisterTimeline(CommandIds.LinkUnlink, Timeline.ToggleLinkCommand);
        RegisterProject(CommandIds.ToggleEnabled, ToggleEnabledSelection);
        RegisterTimeline(CommandIds.Copy, Timeline.CopyCommand);
        RegisterTimeline(CommandIds.Cut, Timeline.CutCommand);
        RegisterTimeline(CommandIds.Paste, Timeline.PasteCommand);
        RegisterTimeline(CommandIds.PasteInsert, Timeline.PasteInsertCommand);
        RegisterTimeline(CommandIds.SelectAll, Timeline.SelectAllCommand);
        RegisterTimeline(CommandIds.AddTransition, EffectsTab.AddDefaultTransitionCommand);
        RegisterTimeline(CommandIds.AddAudioCrossfade, EffectsTab.AddAudioCrossfadeCommand);
        RegisterTimeline(CommandIds.NewTitle, EffectsTab.NewTitleCommand);
        RegisterTimeline(CommandIds.Speed, Inspector.OpenSpeedDialogCommand);
        RegisterTimeline(CommandIds.SequenceSettings, Inspector.OpenSequenceSettingsCommand);

        // Esc leaves a click-to-type timecode first, then cancels a running drag, else deselects (DESIGN 5.3).
        RegisterProject(CommandIds.Escape, () =>
        {
            if (!Viewer.CancelTimecodeEdit())
            {
                Timeline.EscapeCommand.Execute(null);
            }
        });
        RegisterTimeline(CommandIds.Undo, Timeline.UndoCommand);
        RegisterTimeline(CommandIds.Redo, Timeline.RedoCommand);

        RegisterTimeline(CommandIds.ZoomIn, Timeline.ZoomInCommand);
        RegisterTimeline(CommandIds.ZoomOut, Timeline.ZoomOutCommand);
        RegisterTimeline(CommandIds.ZoomToFit, Timeline.ZoomToFitCommand);
        RegisterTimeline(CommandIds.ToggleGuides, Viewer.ToggleGuidesCommand);
        RegisterTimeline(CommandIds.ToggleProxies, Viewer.ToggleProxiesCommand);
        RegisterTimeline(CommandIds.ToggleLoop, Viewer.ToggleLoopCommand);

        // Menu-only commands (no DESIGN 7 shortcut).
        RegisterTimeline(CommandIds.MoveIntoSync, Timeline.MoveIntoSyncCommand);
        RegisterTimeline(CommandIds.AddVideoTrack, Timeline.AddVideoTrackCommand);
        RegisterTimeline(CommandIds.AddAudioTrack, Timeline.AddAudioTrackCommand);
        RegisterTimeline(CommandIds.RemoveSelectedTrack, Timeline.RemoveSelectedTrackCommand);
        RegisterTimeline(CommandIds.SelectInOutRange, Timeline.SelectInOutRangeCommand);
        RegisterTimeline(CommandIds.RippleDeleteInOutRange, Timeline.RippleDeleteInOutRangeCommand);
        RegisterTimeline(CommandIds.CloseGap, Timeline.CloseGapCommand);
        RegisterProject(CommandIds.ShowTimelineMonitor, Viewer.ShowTimeline);
        RegisterProject(CommandIds.ShowSourceMonitor, () => Viewer.SetModeCommand.Execute(ViewerMode.Source));

        c.Register(CommandIds.NewProject, NewCommand);
        c.Register(CommandIds.OpenProject, OpenCommand);
        c.Register(CommandIds.Save, SaveCommand);
        c.Register(CommandIds.SaveAs, SaveAsCommand);
        c.Register(CommandIds.Import, ImportCommand);
        c.Register(CommandIds.Export, ExportCommand);
        c.Register(CommandIds.Quit, QuitCommand);
    }

    // A command whose target follows the monitor shown (DESIGN 4.3): the Source action while Source shows media.
    private void RegisterRouted(string id, Action source, System.Windows.Input.ICommand timeline) =>
        Commands.Register(id, new RelayCommand(
            () =>
            {
                if (Viewer.IsSourceActive)
                {
                    source();
                }
                else
                {
                    timeline.Execute(null);
                }
            },
            () => HasProject && (Viewer.IsSourceActive || timeline.CanExecute(null))));

    private void EditFromSourceOrBin(bool insert)
    {
        if (Viewer.EditFromSource(insert))
        {
            return;
        }

        if (insert)
        {
            MediaBin.InsertAtPlayheadCommand.Execute(null);
        }
        else
        {
            MediaBin.OverwriteAtPlayheadCommand.Execute(null);
        }
    }

    // Shift+Delete: ripple deletes the selection (or closes the selected gap); with nothing selected it ripple deletes
    // the timeline In/Out range, so a marked range can be taken out without selecting clips first.
    private void RippleDelete()
    {
        var nothingSelected = Timeline.SelectedClipIds.Count == 0 && Timeline.SelectedGap is null && Timeline.SelectedTransitionId is null;
        if (nothingSelected && (Timeline.InPoint is not null || Timeline.OutPoint is not null))
        {
            Timeline.RippleDeleteInOutRangeCommand.Execute(null);
        }
        else
        {
            Timeline.RippleDeleteSelectionCommand.Execute(null);
        }
    }

    private void RegisterTimeline(string id, System.Windows.Input.ICommand command, object? parameter = null) =>
        Commands.Register(id, new RelayCommand(() => command.Execute(parameter), () => HasProject && command.CanExecute(parameter)));

    private void RegisterProject(string id, Action action) => Commands.Register(id, action, () => HasProject);

    private void SplitAllUnlocked()
    {
        var project = Session.Current;
        var tracks = project.Sequence.Tracks.Where(t => !t.Locked).Select(t => t.Id).ToList();
        var frame = Timeline.Playhead;
        Timeline.Run(p => TimelineEdits.Split(_services.Ids, p, tracks, frame));
    }

    private void NudgeSelection(long delta)
    {
        var ids = Timeline.SelectedClipIds.ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var key = "nudge:" + string.Join(',', ids.Order(StringComparer.Ordinal));
        Timeline.Run(p => TimelineEdits.Nudge(_services.Ids, p, ids, delta), key);
    }

    private void ToggleEnabledSelection()
    {
        var project = Session.Current;
        var ids = TimelineQuery.ExistingIds(project, Timeline.SelectedClipIds);
        if (ids.Length == 0 || TimelineQuery.FindClip(project, ids[0]) is not { } first)
        {
            return;
        }

        var enable = !first.Clip.Enabled;
        Timeline.Run(p => TimelineEdits.SetEnabled(p, ids, enable));
    }

    private void MarkClipUnderPlayhead()
    {
        var project = Session.Current;
        var at = TimelineQuery.ClipsAt(project, Timeline.Playhead);
        if (at.IsEmpty)
        {
            return;
        }

        // Unspecified which clip X marks when several overlap: the topmost video clip, else the first audio clip.
        var pick = at.Where(x => x.Track.Kind == TrackKind.Video).OrderByDescending(x => TimelineQuery.KindIndex(project, x.Track.Id)).Select(x => x.Clip).FirstOrDefault()
            ?? at[0].Clip;
        // One edit, so X is one undo step and never passes through a transient Out-before-In state.
        Timeline.Run(p => TimelineEdits.SetInOut(p, pick.Start, pick.End));
    }

    private void JumpToMarker(bool forward)
    {
        var playhead = Timeline.Playhead;
        var frames = Session.Current.Sequence.Markers.Select(m => m.Frame);
        long? target = forward
            ? frames.Where(f => f > playhead).Select(f => (long?)f).Min()
            : frames.Where(f => f < playhead).Select(f => (long?)f).Max();
        if (target is { } frame)
        {
            Timeline.Playhead = frame;
        }
    }
}
