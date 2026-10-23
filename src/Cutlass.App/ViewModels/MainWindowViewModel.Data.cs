using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services;
using Cutlass.App.Services.Commands;
using Cutlass.App.Services.Dialogs;
using Cutlass.App.ViewModels.Dialogs;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Jobs;
using Cutlass.Media.Platform;
using Cutlass.Media.Projects;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The project data side of the main window (M7, DESIGN 11): autosave, recovery of autosaves (on startup, in the
/// welcome overlay and when opening a project with a newer autosave), the media link check on open (moved files are
/// relinked relative to the project, changed files re-probed, missing files offline), the Relink dialog, "Clear
/// cache" and the crash log.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private AutosaveService? _autosave;
    private AppLog? _log;
    private RelinkScan? _pendingScan;
    private bool _openingRecovered;
    private long _openGeneration;

    // DESIGN 11: media that were offline when the project opened, including remote paths the scan did not touch
    // (_unconfirmedMedia). Nothing checks, probes, indexes or renders their paths (catalog, bin, media jobs) until the
    // user relinks them or confirms their location. Replaced, never mutated, so other threads can read it.
    private ImmutableHashSet<string> _untouchedMedia = ImmutableHashSet.Create<string>(StringComparer.Ordinal);
    private ImmutableHashSet<string> _unconfirmedMedia = ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    /// <summary>The media whose paths stay untouched until relinked or confirmed (DESIGN 11).</summary>
    public IReadOnlyCollection<string> UntouchedMediaIds => Volatile.Read(ref _untouchedMedia);

    /// <summary>The media on a remote volume the open scan did not look at (<see cref="RelinkScan.UnconfirmedIds"/>).</summary>
    public IReadOnlyCollection<string> UnconfirmedMediaIds => Volatile.Read(ref _unconfirmedMedia);

    /// <summary>Autosaves the open project while it is dirty (DESIGN 11).</summary>
    public AutosaveService Autosave => _autosave ?? throw new InvalidOperationException("The data services are not initialised.");

    /// <summary>The crash and diagnostics log in <c>&lt;data&gt;/logs</c>.</summary>
    public AppLog Log => _log ?? throw new InvalidOperationException("The data services are not initialised.");

    /// <summary>
    /// Records a failure in the log and autosaves a dirty project, best effort and from any thread. Never throws.
    /// When the process is going down (<paramref name="terminating"/>: an unhandled exception or a termination signal)
    /// the autosave is written synchronously within <paramref name="autosaveTimeout"/>. Otherwise (an unobserved task
    /// exception, after which the app keeps running) it goes through the normal autosave path on the UI thread, so it
    /// is serialised with the timed autosaves and a clean close deletes it.
    /// </summary>
    /// <param name="context">What failed, for example "Unhandled exception".</param>
    /// <param name="exception">The exception, or null (a signal).</param>
    /// <param name="autosaveTimeout">How long the last autosave may take when terminating.</param>
    /// <param name="terminating">True when the process ends after this call.</param>
    public void HandleCrash(string context, Exception? exception, TimeSpan autosaveTimeout, bool terminating = true)
    {
        try
        {
            _log?.WriteException(context, exception);
            if (_autosave is not { } autosave || HasShutDown)
            {
                return;
            }

            if (!terminating)
            {
                _post(() => _ = AutosaveAfterFailureAsync(autosave));
                return;
            }

            if (HasProject)
            {
                var saved = autosave.TrySaveNowBlocking(autosaveTimeout);
                _log?.Write(saved ? "Crash autosave written to " + autosave.TargetPath : "No crash autosave written (nothing unsaved, autosave off or the write failed).");
            }
        }
        catch (Exception)
        {
            // The process may be going down; nothing may escape from here.
        }
    }

    // An unobserved task exception: some work silently failed, so autosave now through the tracked path.
    private async Task AutosaveAfterFailureAsync(AutosaveService autosave)
    {
        try
        {
            if (HasProject && !HasShutDown && autosave.IsEnabled && await autosave.SaveNowAsync())
            {
                _log?.Write("Autosave after the failure written to " + autosave.TargetPath);
            }
        }
        catch (Exception ex)
        {
            // Never let this raise another unobserved exception.
            _log?.WriteException("Autosave after the failure failed", ex);
        }
    }

    /// <summary>Re-reads the recoverable autosaves for the welcome overlay (off the UI thread).</summary>
    /// <returns>A task that completes when the list is updated.</returns>
    public async Task RefreshRecoverableAsync()
    {
        var recent = _recent.ToList();
        var folder = _services.Paths.AutosaveDirectory;
        var items = await Task.Run(() => AutosaveRecovery.FindAll(folder, recent));
        Welcome.SetRecoverable(items);
    }

    /// <summary>
    /// Opens an autosave (DESIGN 11): a project's autosave opens as that project, an untitled one as an untitled project
    /// that keeps autosaving to the same file. The result is unsaved (dirty). Asks to save the open project first.
    /// </summary>
    /// <param name="item">The autosave.</param>
    /// <returns>True when the autosave opened.</returns>
    public async Task<bool> RecoverAsync(RecoverableProject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!await ConfirmDiscardAsync())
        {
            return false;
        }

        Project project;
        try
        {
            project = await _services.Projects.LoadAsync(item.AutosavePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await _dialogs.ShowErrorAsync(new ErrorDialogViewModel(
                "Recover project",
                $"The autosave of {item.Name} could not be read. {ex.Message}",
                null,
                null));
            return false;
        }

        await SaveViewStateAsync();
        if (item.ProjectPath is { } path)
        {
            var view = await _services.Projects.LoadViewStateAsync(path);
            project = await ScanForOpenAsync(project, path);
            _openingRecovered = true;
            StartProject(project, path);
            ApplyViewState(view);
            await AddRecentAsync(path);
        }
        else
        {
            project = await ScanForOpenAsync(project, null);
            _openingRecovered = true;
            StartProject(project, null);
            Autosave.Adopt(item.AutosavePath);
        }

        ShowToast("Recovered " + item.Name + " from the autosave of " + item.SavedText + ". Save to keep the changes.");
        await RefreshRecoverableAsync();
        return true;
    }

    /// <summary>Deletes an autosave ("Discard"), after confirming when <paramref name="confirm"/> is set.</summary>
    /// <param name="item">The autosave.</param>
    /// <param name="confirm">True to ask first.</param>
    /// <returns>True when it was deleted.</returns>
    public async Task<bool> DiscardRecoveryAsync(RecoverableProject item, bool confirm)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (confirm && !await _dialogs.ConfirmAsync(
                "Discard autosave",
                $"Delete the autosave of {item.Name} from {item.SavedText}? Its unsaved changes are lost.",
                "Discard"))
        {
            return false;
        }

        var deleted = true;
        try
        {
            await Task.Run(() => _services.Projects.DeleteAutosave(item.AutosavePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("The autosave could not be deleted: " + ex.Message);
            deleted = false;
        }

        await RefreshRecoverableAsync();
        return deleted;
    }

    /// <summary>
    /// Replaces relinked or re-probed assets in the project as one undo step (<c>TimelineEdits.ImportMedia</c> trims
    /// clips to a shorter source and removes clips whose stream is gone), marks them online and queues their media
    /// jobs. The Relink dialog calls it.
    /// </summary>
    /// <param name="assets">The updated assets (same ids, new files).</param>
    public void ApplyRelink(IReadOnlyList<MediaAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (assets.Count == 0 || !HasProject)
        {
            return;
        }

        // Relinked (or confirmed) media may be touched again. The catalog forgets their offline entries, so even an
        // asset that comes back unchanged (a confirmed location) is registered online.
        ReleaseUntouchedMedia(assets.Select(a => a.Id));
        foreach (var asset in assets)
        {
            _services.Catalog?.Remove(asset.Id);
        }

        var result = Session.Execute(p => TimelineEdits.ImportMedia(p, assets));
        _services.Catalog?.Sync(Session.Current);
        foreach (var asset in assets)
        {
            MediaBin.SetLinkState(asset.Id, MediaLinkState.Online);
            _services.Pipeline?.Enqueue(asset, JobPriority.Visible);
        }

        Viewer.Refresh();
        var message = assets.Count == 1
            ? "Relinked " + MediaRelinker.FileNameOf(assets[0].Path) + "."
            : string.Create(CultureInfo.InvariantCulture, $"Relinked {assets.Count} media files.");
        ShowToast(result.Warnings.IsEmpty ? message : message + " " + string.Join(" ", result.Warnings));
    }

    /// <summary>
    /// "Clear cache" (DESIGN 10) for the settings dialog: deletes every cached artifact except those a running job
    /// uses, then queues the open project's media jobs again so its posters, thumbnails and waveforms come back.
    /// </summary>
    /// <returns>The number of media folders deleted.</returns>
    public async Task<int> ClearMediaCacheAsync()
    {
        if (_services.Pipeline is not { } pipeline)
        {
            return 0;
        }

        IReadOnlyList<string> removed;
        try
        {
            removed = await pipeline.ClearCacheAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("The cache could not be cleared: " + ex.Message);
            return 0;
        }

        if (HasProject)
        {
            EnqueueProjectMedia(Session.Current);
        }

        UpdateCacheSummary();
        return removed.Count;
    }

    // Called once from the constructor.
    private void InitializeDataServices()
    {
        _log = new AppLog(_services.Paths.LogsDirectory);
        _autosave = new AutosaveService(
            _services.Projects,
            _services.Paths.AutosaveDirectory,
            Session,
            () => HasProject ? ProjectPath : null,
            () => Session.InGesture || Timeline.IsDragging);
        _autosave.WriteFailed += ex =>
        {
            _log.WriteException("Autosave failed", ex);
            _post(() => ShowToast("Autosave failed: " + ex.Message));
        };
        MediaBin.RelinkRequested = item => ShowRelinkDialogAsync(item?.Asset is { } asset ? [asset.Id] : null);
        Commands.Register(CommandIds.RelinkMedia, RelinkMediaCommand);
        Timeline.IsMediaOffline = MediaBin.IsMediaOffline;
        MediaBin.LinkStatesChanged += (_, _) => Timeline.NotifyMediaLinkStatesChanged();
        Welcome.RecoverRequested = item => RecoverAsync(item);
        Welcome.DiscardRecoveryRequested = item => DiscardRecoveryAsync(item, confirm: true);
    }

    // InitializeAsync, once the recent list is known: autosave on, the settings problem toast, the recovery offer.
    private async Task StartDataServicesAsync(CommandLineOptions options)
    {
        Autosave.Interval = AutosaveService.IntervalFromMinutes(_services.Settings.AutosaveIntervalMinutes);
        Autosave.Start();
        if (_services.SettingsStore.LastLoadProblem is { } problem)
        {
            ShowToast(problem);
        }

        await RefreshRecoverableAsync();

        // Only an interactive start asks; a command line project or a smoke run decides for itself.
        var interactive = options.ProjectPath is null && options.ImportPaths.Count == 0 && options.SmokeScriptPath is null;
        if (interactive && !HasProject && Welcome.Recoverable.FirstOrDefault() is { } newest)
        {
            switch (await _dialogs.AskRecoveryAsync(newest.Name, newest.SavedText))
            {
                case RecoveryChoice.Recover:
                    await RecoverAsync(newest);
                    break;
                case RecoveryChoice.Discard:
                    await DiscardRecoveryAsync(newest, confirm: false);
                    break;
            }
        }
    }

    // A clean close or the end of a smoke run: no more autosaves, and the ones this session wrote go.
    private async Task StopDataServicesAsync()
    {
        if (_autosave is not { } autosave)
        {
            return;
        }

        autosave.Stop();
        await autosave.ClearAsync(includeProjectAutosave: false);
    }

    // OpenPathAsync: the project, or its newer autosave when the user recovers it (DESIGN 11), checked for moved,
    // changed and missing media. Throws like ProjectStore.LoadAsync when the project itself cannot be read.
    private async Task<Project> LoadForOpenAsync(string path)
    {
        _openingRecovered = false;
        _pendingScan = null;
        var candidate = await Task.Run(() => AutosaveRecovery.ForProject(path));
        if (candidate is not null)
        {
            switch (await _dialogs.AskRecoveryAsync(candidate.Name, candidate.SavedText))
            {
                case RecoveryChoice.Recover:
                    try
                    {
                        var recovered = await _services.Projects.LoadAsync(candidate.AutosavePath);
                        var scanned = await ScanForOpenAsync(recovered, path);
                        _openingRecovered = true;
                        return scanned;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        await _dialogs.ShowErrorAsync(new ErrorDialogViewModel(
                            "Recover project",
                            $"The autosave of {candidate.Name} could not be read, so the saved project opens instead. {ex.Message}",
                            null,
                            null));
                    }

                    break;
                case RecoveryChoice.Discard:
                    try
                    {
                        await Task.Run(() => _services.Projects.DeleteAutosave(candidate.AutosavePath));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        ShowToast("The autosave could not be deleted: " + ex.Message);
                    }

                    break;
            }
        }

        var project = await _services.Projects.LoadAsync(path);
        return await ScanForOpenAsync(project, path);
    }

    // Queues the media jobs of a project, except for media whose paths must not be touched (DESIGN 11).
    private void EnqueueProjectMedia(Project project)
    {
        if (_services.Pipeline is not { } pipeline)
        {
            return;
        }

        var untouched = Volatile.Read(ref _untouchedMedia);
        foreach (var asset in project.Media.Where(m => !untouched.Contains(m.Id)))
        {
            pipeline.Enqueue(asset);
        }
    }

    // StartProject, before the session holds the new project: the open scan's offline media stay untouched.
    private void SetUntouchedMedia(RelinkScan? scan)
    {
        Volatile.Write(ref _untouchedMedia, ImmutableHashSet.CreateRange(StringComparer.Ordinal, scan?.OfflineIds ?? []));
        Volatile.Write(ref _unconfirmedMedia, ImmutableHashSet.CreateRange(StringComparer.Ordinal, scan?.UnconfirmedIds ?? []));
    }

    private void ReleaseUntouchedMedia(IEnumerable<string> ids)
    {
        var list = ids.ToList();
        Volatile.Write(ref _untouchedMedia, _untouchedMedia.Except(list));
        Volatile.Write(ref _unconfirmedMedia, _unconfirmedMedia.Except(list));
    }

    /// <summary>
    /// "Confirm location" in the Relink dialog (DESIGN 11): looks up the recorded paths of media the open scan did not
    /// touch because they are on a remote volume, now that the user confirmed them. A found file is then relinked
    /// through the dialog's Relink button like any other.
    /// </summary>
    /// <param name="mediaIds">The confirmed media.</param>
    /// <returns>The scan of the current project with those media looked up.</returns>
    public async Task<RelinkScan> ConfirmMediaLocationsAsync(IReadOnlyCollection<string> mediaIds)
    {
        ArgumentNullException.ThrowIfNull(mediaIds);
        var scan = await MediaRelinker.ScanAsync(Session.Current, ProjectPath, mediaIds);
        Volatile.Write(ref _unconfirmedMedia, _unconfirmedMedia.Except(mediaIds));
        return scan;
    }

    private async Task<Project> ScanForOpenAsync(Project project, string? path)
    {
        var scan = await MediaRelinker.ScanAsync(project, path);
        _pendingScan = scan;
        return scan.Project;
    }

    // StartProject, after the session holds the new project: show the link states and re-probe changed files.
    private void AfterProjectStarted()
    {
        _openingRecovered = false;
        _openGeneration++;
        var scan = _pendingScan;
        _pendingScan = null;
        MediaBin.SetLinkStates(scan?.States ?? new Dictionary<string, MediaLinkState>(StringComparer.Ordinal));
        if (scan is null)
        {
            return;
        }

        var notes = new List<string>();
        if (scan.MovedIds.Count > 0)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{scan.MovedIds.Count} media file(s) were found relative to the project and relinked."));
        }

        if (scan.OfflineIds.Count > 0)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{scan.OfflineIds.Count} media file(s) are offline. Use Relink media... to find them."));
        }

        if (scan.UnconfirmedIds.Count > 0)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"{scan.UnconfirmedIds.Count} of them are on a network location and were not checked; confirm them in Relink media..."));
        }

        if (notes.Count > 0)
        {
            ShowToast(string.Join(" ", notes));
        }

        if (scan.ChangedIds.Count > 0)
        {
            _ = RefreshChangedMediaAsync(scan.ChangedIds, _openGeneration);
        }
    }

    // DESIGN 11: changed files are re-probed; when the content really changed the asset is replaced (one undo step)
    // and keeps the "changed" badge, a file that was only touched counts as online again.
    private async Task RefreshChangedMediaAsync(IReadOnlyList<string> ids, long generation)
    {
        var relinker = CreateRelinker();
        var updated = new List<MediaAsset>();
        var unchanged = new List<string>();
        foreach (var id in ids)
        {
            if (TimelineQuery.FindMedia(Session.Current, id) is not { } asset)
            {
                continue;
            }

            try
            {
                var fresh = await relinker.RefreshAsync(asset, asset.Path);
                if (fresh.Fingerprint == asset.Fingerprint && fresh.Info == asset.Info)
                {
                    unchanged.Add(id);
                }
                else
                {
                    updated.Add(fresh with { RelativePath = asset.RelativePath });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FfmpegException)
            {
                _log?.Write("Re-probing changed media " + asset.Path + " failed: " + ex.Message);
            }
        }

        if (generation != _openGeneration)
        {
            // Another project was opened meanwhile.
            return;
        }

        foreach (var id in unchanged)
        {
            MediaBin.SetLinkState(id, MediaLinkState.Online);
        }

        if (updated.Count > 0)
        {
            var result = Session.Execute(p => TimelineEdits.ImportMedia(p, updated));
            foreach (var asset in updated)
            {
                MediaBin.SetLinkState(asset.Id, MediaLinkState.Changed);
                _services.Pipeline?.Enqueue(asset, JobPriority.Visible);
            }

            Viewer.Refresh();
            var message = string.Create(CultureInfo.InvariantCulture, $"{updated.Count} media file(s) changed since import and were read again.");
            ShowToast(result.Warnings.IsEmpty ? message : message + " " + string.Join(" ", result.Warnings));
        }
    }

    private MediaRelinker CreateRelinker() => new(_services.Probe, _services.Fingerprints);

    private async Task ShowRelinkDialogAsync(IReadOnlyCollection<string>? onlyIds)
    {
        if (!HasProject)
        {
            return;
        }

        var media = Session.Current.Media;
        var assets = onlyIds is null
            ? media.Where(m => MediaBin.LinkStateOf(m.Id) == MediaLinkState.Offline)
            : media.Where(m => onlyIds.Contains(m.Id));
        var items = assets.Select(a => (a, MediaBin.LinkStateOf(a.Id))).ToList();
        if (items.Count == 0)
        {
            ShowToast("Every media file is online.");
            return;
        }

        var folder = ProjectPath is { } path ? Path.GetDirectoryName(path) : null;
        var dialog = new RelinkDialogViewModel(items, CreateRelinker(), _dialogs, ApplyRelink, folder, UnconfirmedMediaIds, ConfirmMediaLocationsAsync);
        await _dialogs.ShowRelinkDialogAsync(dialog);
    }

    // "Relink media..." (File menu, DESIGN 11): the Relink dialog with every offline media file. Without a project it
    // does nothing (no CanExecute, so HasProject needs no extra notification).
    [RelayCommand]
    private Task RelinkMediaAsync() => ShowRelinkDialogAsync(null);
}
