using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services;
using Cutlass.App.Services.Dialogs;
using Cutlass.App.ViewModels.Agent;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Media.Projects;
using Cutlass.Media.Services;

namespace Cutlass.App.ViewModels.MediaBin;

/// <summary>
/// The media panel (DESIGN 4.2): import by dialog, by dropping files or folders, grid and list views, search and
/// sort, poster cards with badges, double click to insert at the playhead and remove with confirmation when used.
/// The item list mirrors <see cref="Project.Media"/> of the session.
/// </summary>
public sealed partial class MediaBinViewModel : ObservableObject
{
    /// <summary>The extensions a dropped folder imports (two levels deep); single files are always tried.</summary>
    public static IReadOnlySet<string> SupportedExtensions { get; } = new[]
    {
        ".mp4", ".mov", ".m4v", ".mkv", ".webm", ".avi", ".mts", ".m2ts", ".ts", ".mxf", ".wmv", ".flv", ".mpg",
        ".mpeg", ".3gp", ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".opus", ".wma", ".aif", ".aiff", ".png",
        ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly ProjectSession _session;
    private readonly TimelineViewModel _timeline;
    private readonly IDialogService _dialogs;
    private readonly Action<string> _toast;
    private readonly List<MediaItemViewModel> _importing = [];
    private readonly HashSet<string> _fresh = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MediaLinkState> _linkStates = new(StringComparer.Ordinal);

    // Cancelled when the session's project is replaced (New, Open, Recover): a running import then stops and drops
    // what it still had, so nothing lands in the project that became current. Replaced, never disposed, because a
    // running importer may still hold its token.
    private CancellationTokenSource _importCancel = new();

    /// <summary>Creates the bin.</summary>
    /// <param name="session">The project session.</param>
    /// <param name="timeline">The timeline (double click inserts at its playhead).</param>
    /// <param name="dialogs">File pickers and confirmations.</param>
    /// <param name="toast">Shows a short message in the window.</param>
    public MediaBinViewModel(ProjectSession session, TimelineViewModel timeline, IDialogService dialogs, Action<string> toast)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(toast);
        _session = session;
        _timeline = timeline;
        _dialogs = dialogs;
        _toast = toast;
        SearchText = string.Empty;
        ImportUnavailableReason = "Looking for ffmpeg...";
        session.Changed += OnSessionChanged;
        SyncFromProject(session.Current);
    }

    /// <summary>Every item: project media plus files being imported.</summary>
    public ObservableCollection<MediaItemViewModel> Items { get; } = [];

    /// <summary>The items shown: filtered by <see cref="SearchText"/> and sorted by <see cref="SortOrder"/>.</summary>
    public ObservableCollection<MediaItemViewModel> VisibleItems { get; } = [];

    /// <summary>The sort choices.</summary>
    public IReadOnlyList<MediaSortOrder> SortOrders { get; } = Enum.GetValues<MediaSortOrder>();

    /// <summary>Imports files; null while ffmpeg is unavailable. Setting it also sets <see cref="ImportFile"/>.</summary>
    public MediaImporter? Importer
    {
        get;
        set
        {
            field = value;
            ImportFile = value is null ? null : value.ImportAsync;
        }
    }

    /// <summary>
    /// Imports one file (probe, register, poster); null while ffmpeg is unavailable. <see cref="Importer"/> sets it,
    /// and tests replace it with a fake.
    /// </summary>
    public Func<string, CancellationToken, Task<MediaImportResult>>? ImportFile
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(CanImport));
            ImportCommand.NotifyCanExecuteChanged();
            UpdateCanImportNow();
        }
    }

    /// <summary>Renders posters for media of opened projects; null while ffmpeg is unavailable.</summary>
    public PosterService? Posters { get; set; }

    /// <summary>
    /// True for media whose file must not be touched (DESIGN 11: offline, or on an unconfirmed remote volume, when the
    /// project opened). Such an item stays offline without a file check or poster render until it is relinked or its
    /// location confirmed. Null checks every file.
    /// </summary>
    public Func<string, bool>? SkipFileCheck { get; set; }

    /// <summary>
    /// Called with every newly imported asset after it joined the project (the composition root queues its index,
    /// thumbnail, waveform and proxy jobs), or null.
    /// </summary>
    public Action<MediaAsset>? AssetImported { get; set; }

    /// <summary>Thumbnail sheets for the hover skim, or null while ffmpeg is unavailable.</summary>
    public IMediaArtifactSource? Artifacts { get; set; }

    /// <summary>Follows a proxy job (UI thread): the progress badge while it runs.</summary>
    /// <param name="mediaId">The media id.</param>
    /// <param name="active">True while the job is queued or running.</param>
    /// <param name="fraction">Its progress, 0 to 1.</param>
    public void SetProxyProgress(string mediaId, bool active, double fraction)
    {
        if (FindItem(mediaId) is { } item)
        {
            item.IsProxyBuilding = active;
            item.ProxyFraction = active ? Math.Clamp(fraction, 0, 1) : 0;
        }
    }

    /// <summary>Shows or hides the <c>P</c> chip of a media item (UI thread).</summary>
    /// <param name="mediaId">The media id.</param>
    /// <param name="hasProxy">True when a proxy is attached.</param>
    public void SetProxyReady(string mediaId, bool hasProxy)
    {
        if (FindItem(mediaId) is { } item)
        {
            item.HasProxy = hasProxy;
        }
    }

    /// <summary>Why import is disabled (shown as a hint), for example "ffmpeg not found".</summary>
    [ObservableProperty]
    public partial string ImportUnavailableReason { get; set; }

    /// <summary>The search text (filters by name as you type).</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; }

    /// <summary>The sort order.</summary>
    [ObservableProperty]
    public partial MediaSortOrder SortOrder { get; set; }

    /// <summary>List view instead of the grid.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGridView))]
    public partial bool IsListView { get; set; }

    /// <summary>The selected item (the target of <c>,</c> and <c>.</c> until Source mode exists).</summary>
    [ObservableProperty]
    public partial MediaItemViewModel? SelectedItem { get; set; }

    /// <summary>True while files are being imported.</summary>
    [ObservableProperty]
    public partial bool IsImporting { get; set; }

    /// <summary>Grid view (the default).</summary>
    public bool IsGridView => !IsListView;

    /// <summary>The empty bin's hint; on Linux it points at Ctrl+I and the + button (OS file drops do not arrive).</summary>
    public static string EmptyHint => PlatformFeatures.OsFileDrops
        ? "Drop media here or press Ctrl+I to import"
        : "Press Ctrl+I or the + button to import media";

    /// <summary>True when import can run (ffmpeg found).</summary>
    public bool CanImport => ImportFile is not null;

    /// <summary>True when the bin has no items (the drop hint shows).</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// Expands dropped paths: files are kept as they are, folders contribute their supported files up to two levels
    /// deep (DESIGN 4.2). Order is kept, folder contents sorted by name, duplicates dropped.
    /// </summary>
    /// <param name="paths">The dropped or picked paths.</param>
    /// <param name="directoryExists">True for folders.</param>
    /// <param name="listFiles">Lists the files of a folder.</param>
    /// <param name="listDirectories">Lists the subfolders of a folder.</param>
    /// <returns>The files to import.</returns>
    public static IReadOnlyList<string> ExpandPaths(
        IEnumerable<string> paths,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> listFiles,
        Func<string, IEnumerable<string>> listDirectories)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var result = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        void AddFolder(string folder, int depth)
        {
            foreach (var file in listFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (SupportedExtensions.Contains(System.IO.Path.GetExtension(file)) && seen.Add(file))
                {
                    result.Add(file);
                }
            }

            if (depth < 2)
            {
                foreach (var sub in listDirectories(folder).Order(StringComparer.OrdinalIgnoreCase))
                {
                    AddFolder(sub, depth + 1);
                }
            }
        }

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (directoryExists(path))
            {
                AddFolder(path, 1);
            }
            else if (seen.Add(path))
            {
                result.Add(path);
            }
        }

        return result;
    }

    /// <summary>
    /// Imports files (folders expand two levels deep). Each file is probed, registered and added to the project as
    /// its own undo step; files already in the project are skipped. Unsupported files are listed in one toast. When
    /// another project becomes current meanwhile, the import stops, drops the rest and returns nothing.
    /// </summary>
    /// <param name="paths">The paths.</param>
    /// <returns>The imported assets, in order.</returns>
    public Task<IReadOnlyList<MediaAsset>> ImportPathsAsync(IEnumerable<string> paths) => ImportPathsAsync(paths, null);

    /// <summary>
    /// Imports files like <see cref="ImportPathsAsync(IEnumerable{string})"/>; with a <paramref name="report"/> (an
    /// agent's import, AGENT_INTEGRATION 2.2) each commit runs as the report's writer after its commit check, and the
    /// failures, warnings and files already in the project go to the report instead of a toast.
    /// </summary>
    /// <param name="paths">The paths.</param>
    /// <param name="report">The agent import's options and results, or null for the user's import.</param>
    /// <returns>The imported assets, in order.</returns>
    public async Task<IReadOnlyList<MediaAsset>> ImportPathsAsync(IEnumerable<string> paths, MediaImportReport? report)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (ImportFile is not { } import)
        {
            if (report is null)
            {
                _toast(ImportUnavailableReason);
            }
            else
            {
                report.Warnings.Add(ImportUnavailableReason);
            }

            return [];
        }

        var cancel = _importCancel.Token;
        var requested = paths.ToList();
        IReadOnlyList<string> files;
        try
        {
            files = await Task.Run(() => ExpandPaths(requested, Directory.Exists, Directory.EnumerateFiles, Directory.EnumerateDirectories));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _toast("Could not read the dropped folder: " + ex.Message);
            return [];
        }

        if (cancel.IsCancellationRequested)
        {
            return [];
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var existing = _session.Current.Media.Select(m => m.Path).ToList();
        bool IsPresent(string f) => existing.Any(e => string.Equals(System.IO.Path.GetFullPath(e), System.IO.Path.GetFullPath(f), comparison));
        report?.AlreadyPresent.AddRange(files.Where(IsPresent));
        files = [.. files.Where(f => !IsPresent(f))];
        if (files.Count == 0)
        {
            return [];
        }

        var placeholders = files.Select(f => new MediaItemViewModel(f)).ToList();
        _importing.AddRange(placeholders);
        foreach (var placeholder in placeholders)
        {
            Items.Add(placeholder);
        }

        RefreshVisible();
        IsImporting = true;
        var imported = new List<MediaAsset>();
        var failed = new List<string>();
        var warnings = new List<string>();
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                if (cancel.IsCancellationRequested)
                {
                    break;
                }

                var placeholder = placeholders[i];
                try
                {
                    var result = await import(files[i], cancel);
                    if (cancel.IsCancellationRequested)
                    {
                        // The project was replaced while this file was probed: the result belonged to the old one.
                        break;
                    }

                    if (report?.MayCommit is { } mayCommit && !mayCommit())
                    {
                        // The agent lost its lock while the file was probed: nothing more lands in the project.
                        report.Stopped = true;
                        for (var j = i; j < placeholders.Count; j++)
                        {
                            _importing.Remove(placeholders[j]);
                            Items.Remove(placeholders[j]);
                        }

                        break;
                    }

                    imported.Add(result.Asset);
                    _importing.Remove(placeholder);
                    Items.Remove(placeholder);
                    // The importer already rendered the poster, so the sync must not render it again.
                    _fresh.Add(result.Asset.Id);
                    using (_session.WriteAs(report?.Writer ?? SessionWriter.User))
                    {
                        _session.Execute(p => TimelineEdits.ImportMedia(p, [result.Asset]));
                    }

                    _fresh.Remove(result.Asset.Id);
                    AssetImported?.Invoke(result.Asset);
                    if (result.Warning is { Length: > 0 } warning)
                    {
                        warnings.Add(placeholder.Name + ": " + warning);
                    }

                    if (FindItem(result.Asset.Id) is { } item && result.PosterPath is { } poster)
                    {
                        item.IsBusy = true;
                        _ = LoadPosterAsync(item, poster);
                    }
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed.Add(placeholder.Name);
                    report?.Failed.Add((files[i], ex.Message));
                    _importing.Remove(placeholder);
                    Items.Remove(placeholder);
                }
            }
        }
        finally
        {
            IsImporting = _importing.Count > 0;
            RefreshVisible();
        }

        if (cancel.IsCancellationRequested)
        {
            // AbandonImports already removed the placeholders; toasts about the old project would only confuse.
            return [];
        }

        if (report is not null)
        {
            report.Warnings.AddRange(warnings);
        }
        else if (failed.Count > 0)
        {
            _toast((failed.Count == 1 ? "Unsupported file: " : "Unsupported files: ") + string.Join(", ", failed));
        }
        else if (warnings.Count > 0)
        {
            _toast(string.Join("\n", warnings));
        }

        if (imported.Count > 0 && SelectedItem is null)
        {
            SelectedItem = FindItem(imported[0].Id);
        }

        return imported;
    }

    /// <summary>Rebuilds the items from a project (open, new, undo of an import, remove).</summary>
    /// <param name="project">The project.</param>
    public void SyncFromProject(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var byId = Items.Where(i => i.Asset is not null).ToDictionary(i => i.Id, StringComparer.Ordinal);
        var rebuilt = new List<MediaItemViewModel>();
        var order = 0;
        foreach (var asset in project.Media)
        {
            if (byId.TryGetValue(asset.Id, out var item))
            {
                var relinked = item.Asset is { } previous && previous.Path != asset.Path;
                item.Asset = asset;
                if (relinked)
                {
                    // A relink points the item at another file: check it and load its poster again.
                    item.Poster = null;
                    _ = LoadExistingAsync(item);
                }

                item.Order = order;
            }
            else
            {
                item = new MediaItemViewModel(asset, order);
                if (!_fresh.Contains(asset.Id))
                {
                    _ = LoadExistingAsync(item);
                }
            }

            rebuilt.Add(item);
            order++;
        }

        if (!Items.Where(i => i.Asset is not null).SequenceEqual(rebuilt))
        {
            Items.Clear();
            foreach (var item in rebuilt.Concat(_importing))
            {
                Items.Add(item);
            }
        }

        if (SelectedItem is { } selected && !Items.Contains(selected))
        {
            SelectedItem = null;
        }

        RefreshVisible();
    }

    /// <summary>Renders the posters of items that have none yet (after ffmpeg became available).</summary>
    public void LoadMissingPosters()
    {
        foreach (var item in Items.Where(i => i.Asset is not null && i.Poster is null && !i.IsBusy).ToList())
        {
            _ = LoadExistingAsync(item);
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshVisible();

    partial void OnSortOrderChanged(MediaSortOrder value) => RefreshVisible();

    private void OnSessionChanged(object? sender, ProjectChangedEventArgs e)
    {
        if (e.Kind == ChangeKind.Replace)
        {
            AbandonImports();
        }

        if (e.Old.Media != e.New.Media)
        {
            SyncFromProject(e.New);
        }
    }

    // New, Open or Recover replaced the project: running imports stop and their placeholders go.
    private void AbandonImports()
    {
        var running = _importCancel;
        _importCancel = new CancellationTokenSource();
        running.Cancel();
        if (_importing.Count == 0)
        {
            return;
        }

        foreach (var placeholder in _importing)
        {
            Items.Remove(placeholder);
        }

        _importing.Clear();
        IsImporting = false;
        RefreshVisible();
    }

    private MediaItemViewModel? FindItem(string id) => Items.FirstOrDefault(i => i.Id == id);

    private void RefreshVisible()
    {
        var search = SearchText.Trim();
        IEnumerable<MediaItemViewModel> items = Items.Where(i => search.Length == 0 || i.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        items = SortOrder switch
        {
            MediaSortOrder.Name => items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase),
            MediaSortOrder.Duration => items.OrderBy(i => i.Asset?.Info.DurationMicros ?? long.MaxValue),
            _ => items.OrderBy(i => i.Asset is null ? int.MaxValue : i.Order),
        };
        var list = items.ToList();
        if (!list.SequenceEqual(VisibleItems))
        {
            VisibleItems.Clear();
            foreach (var item in list)
            {
                VisibleItems.Add(item);
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task LoadExistingAsync(MediaItemViewModel item)
    {
        if (item.Asset is not { } asset)
        {
            return;
        }

        if (SkipFileCheck?.Invoke(asset.Id) == true)
        {
            SetLinkState(asset.Id, MediaLinkState.Offline);
            return;
        }

        var exists = await Task.Run(() => File.Exists(asset.Path));
        if (item.Asset != asset)
        {
            // Relinked meanwhile; the newer check decides.
            return;
        }

        SetLinkState(asset.Id, exists ? (_linkStates.GetValueOrDefault(asset.Id) == MediaLinkState.Changed ? MediaLinkState.Changed : MediaLinkState.Online) : MediaLinkState.Offline);
        if (!exists || Posters is not { } posters || asset.Info.Video is null)
        {
            return;
        }

        item.IsBusy = true;
        try
        {
            if (await posters.GetOrCreateAsync(asset, CancellationToken.None) is { } path)
            {
                await LoadPosterAsync(item, path);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missing poster only means a plain card.
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private static async Task LoadPosterAsync(MediaItemViewModel item, string path)
    {
        try
        {
            item.Poster = await Task.Run(() => new Bitmap(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // An unreadable poster only means a plain card.
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private bool IsUsed(string mediaId) =>
        _session.Current.Sequence.Tracks.Any(t => t.Clips.OfType<MediaClip>().Any(c => c.MediaId == mediaId));

    /// <summary>
    /// True while an agent holds or pauses the project (AGENT_INTEGRATION 3.7): import, remove, relink, insert and
    /// overwrite are disabled and file drops on the bin are refused. Opening media in Source, the views, search, proxies
    /// and copying paths stay. The agent's own imports run through <see cref="ImportPathsAsync(IEnumerable{string}, MediaImportReport)"/>, which this does not gate.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImportNow))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand), nameof(InsertAtPlayheadCommand), nameof(OverwriteAtPlayheadCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(RelinkCommand))]
    public partial bool IsReadOnly { get; set; }

    /// <summary>True when the Import button works now: ffmpeg is ready and no agent holds the project.</summary>
    public bool CanImportNow => CanImport && !IsReadOnly;

    private bool CanEdit() => !IsReadOnly;

    private void UpdateCanImportNow() => OnPropertyChanged(nameof(CanImportNow));

    [RelayCommand(CanExecute = nameof(CanImportNow))]
    private async Task ImportAsync()
    {
        var files = await _dialogs.PickMediaFilesAsync();
        if (files.Count > 0)
        {
            await ImportPathsAsync(files);
        }
    }

    /// <summary>
    /// The content of the EFFECTS tab (DESIGN 4.2: transitions and title templates), set by the composition root from
    /// <see cref="MainWindowViewModel.Effects"/>, or null while there is none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEffects))]
    public partial object? Effects { get; set; }

    /// <summary>True when the EFFECTS tab has content.</summary>
    public bool HasEffects => Effects is not null;

    /// <summary>True while the EFFECTS tab shows instead of the media.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMediaTab))]
    public partial bool IsEffectsTab { get; set; }

    /// <summary>True while the AGENT tab shows (AGENT_INTEGRATION 6.3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMediaTab))]
    public partial bool IsAgentTab { get; set; }

    /// <summary>True while the MEDIA tab shows.</summary>
    public bool IsMediaTab => !IsEffectsTab && !IsAgentTab;

    /// <summary>The AGENT tab content (the agent panel), set by the main window, or null.</summary>
    public AgentViewModel? Agent { get; set; }

    /// <summary>True while the AGENT tab header shows: agent connections are allowed (or an agent still holds the project).</summary>
    [ObservableProperty]
    public partial bool ShowAgentTab { get; set; }

    partial void OnShowAgentTabChanged(bool value)
    {
        if (!value && IsAgentTab)
        {
            IsAgentTab = false;
        }
    }

    /// <summary>The status bar badge and View > Agent panel: shows the AGENT tab.</summary>
    [RelayCommand]
    private void ShowAgentTabPage()
    {
        if (Agent is not null)
        {
            ShowAgentTab = true;
            IsEffectsTab = false;
            IsAgentTab = true;
        }
    }

    /// <summary>"Open in Source" and double-click (DESIGN 4.2): set by the composition root to show the media in Source.</summary>
    public Action<MediaAsset>? OpenInSourceRequested { get; set; }

    /// <summary>Double-click or "Open in Source" (DESIGN 4.2): opens the item in the Source monitor.</summary>
    [RelayCommand]
    private void OpenInSource(MediaItemViewModel? item)
    {
        if ((item ?? SelectedItem) is { Asset: { } asset })
        {
            OpenInSourceRequested?.Invoke(asset);
        }
    }

    /// <summary>"Insert at playhead": places the whole item at the timeline In or playhead.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void InsertAtPlayhead(MediaItemViewModel? item)
    {
        if ((item ?? SelectedItem) is { Asset: not null } target)
        {
            _timeline.InsertMediaAtPlayhead(target.Id);
        }
    }

    /// <summary>"Overwrite at playhead": overwrites with the whole item at the timeline In or playhead.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OverwriteAtPlayhead(MediaItemViewModel? item)
    {
        if ((item ?? SelectedItem) is { Asset: not null } target)
        {
            _timeline.OverwriteMediaAtPlayhead(target.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task RemoveAsync(MediaItemViewModel? item)
    {
        if ((item ?? SelectedItem) is not { Asset: not null } target)
        {
            return;
        }

        if (IsUsed(target.Id) &&
            !await _dialogs.ConfirmAsync(
                "Remove media",
                $"{target.Name} is used on the timeline. Removing it also removes its clips.",
                "Remove"))
        {
            return;
        }

        var id = target.Id;
        _session.Execute(p => TimelineEdits.RemoveMedia(p, [id]));
    }

    /// <summary>"Create proxy" (DESIGN 4.2): queues a proxy whatever the policy. Set by the composition root.</summary>
    public Action<MediaAsset>? CreateProxyRequested { get; set; }

    /// <summary>"Delete proxy" (DESIGN 4.2): detaches and deletes the proxy. Set by the composition root.</summary>
    public Func<MediaAsset, Task>? DeleteProxyRequested { get; set; }

    [RelayCommand]
    private void CreateProxy(MediaItemViewModel? item)
    {
        if ((item ?? SelectedItem) is { Asset: { Info.Video: not null, Info.IsStill: false } asset, HasProxy: false })
        {
            CreateProxyRequested?.Invoke(asset);
        }
    }

    [RelayCommand]
    private async Task DeleteProxyAsync(MediaItemViewModel? item)
    {
        if ((item ?? SelectedItem) is { Asset: { } asset, HasProxy: true } target && DeleteProxyRequested is { } delete)
        {
            try
            {
                await delete(asset);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _toast("The proxy of " + target.Name + " could not be deleted: " + ex.Message);
            }
        }
    }

    [RelayCommand]
    private Task RevealAsync(MediaItemViewModel? item) =>
        (item ?? SelectedItem) is { } target ? _dialogs.RevealInFileManagerAsync(target.Path) : Task.CompletedTask;

    [RelayCommand]
    private Task CopyPathAsync(MediaItemViewModel? item) =>
        (item ?? SelectedItem) is { } target ? _dialogs.CopyToClipboardAsync(target.Path) : Task.CompletedTask;

    /// <summary>
    /// Raised on the UI thread when the offline or changed state of any media changed, so the timeline can redraw
    /// its hatched offline clips (<see cref="IsMediaOffline"/>).
    /// </summary>
    public event EventHandler? LinkStatesChanged;

    /// <summary>"Relink..." (DESIGN 4.2): opens the Relink dialog for the item. Set by the composition root.</summary>
    public Func<MediaItemViewModel?, Task>? RelinkRequested { get; set; }

    /// <summary>The ids of the media whose files are missing, in bin order.</summary>
    public IReadOnlyList<string> OfflineMediaIds =>
        [.. Items.Where(i => i.Asset is not null && i.IsOffline).Select(i => i.Id)];

    /// <summary>
    /// True when the media's file is missing (DESIGN 4.2 offline badge). The timeline uses it to hatch offline clips;
    /// the clips themselves stay.
    /// </summary>
    /// <param name="mediaId">The media id.</param>
    /// <returns>True when offline.</returns>
    public bool IsMediaOffline(string mediaId) =>
        mediaId is not null && _linkStates.TryGetValue(mediaId, out var state) && state == MediaLinkState.Offline;

    /// <summary>The link state of a media item (online when unknown).</summary>
    /// <param name="mediaId">The media id.</param>
    /// <returns>The state.</returns>
    public MediaLinkState LinkStateOf(string mediaId) =>
        mediaId is not null && _linkStates.TryGetValue(mediaId, out var state) ? state : MediaLinkState.Online;

    /// <summary>Records the link state of a media item (UI thread): the offline stripes or the changed badge.</summary>
    /// <param name="mediaId">The media id.</param>
    /// <param name="state">The state.</param>
    public void SetLinkState(string mediaId, MediaLinkState state)
    {
        ArgumentNullException.ThrowIfNull(mediaId);
        var previous = LinkStateOf(mediaId);
        if (state == MediaLinkState.Online)
        {
            _linkStates.Remove(mediaId);
        }
        else
        {
            _linkStates[mediaId] = state;
        }

        if (FindItem(mediaId) is { } item)
        {
            item.IsOffline = state == MediaLinkState.Offline;
            item.IsChanged = state == MediaLinkState.Changed;
        }

        if (previous != state)
        {
            LinkStatesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Replaces every link state (after a project was opened); media not listed count as online.</summary>
    /// <param name="states">The states by media id.</param>
    public void SetLinkStates(IReadOnlyDictionary<string, MediaLinkState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        _linkStates.Clear();
        foreach (var (id, state) in states)
        {
            if (state != MediaLinkState.Online)
            {
                _linkStates[id] = state;
            }
        }

        foreach (var item in Items.Where(i => i.Asset is not null))
        {
            var state = LinkStateOf(item.Id);
            item.IsOffline = state == MediaLinkState.Offline;
            item.IsChanged = state == MediaLinkState.Changed;
        }

        LinkStatesChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task RelinkAsync(MediaItemViewModel? item) =>
        (item ?? SelectedItem) is { Asset: not null } target && RelinkRequested is { } relink ? relink(target) : Task.CompletedTask;

    [RelayCommand]
    private void ShowMediaTab()
    {
        IsEffectsTab = false;
        IsAgentTab = false;
    }

    [RelayCommand]
    private void ShowEffectsTab()
    {
        if (HasEffects)
        {
            IsAgentTab = false;
            IsEffectsTab = true;
        }
    }

    partial void OnEffectsChanged(object? value)
    {
        if (value is null)
        {
            IsEffectsTab = false;
        }
    }

    [RelayCommand]
    private void ShowGrid() => IsListView = false;

    [RelayCommand]
    private void ShowList() => IsListView = true;
}
