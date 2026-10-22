using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services;
using Cutlass.Media.Settings;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The main window's Settings side (DESIGN 10): Ctrl+, and the menus open the Settings dialog, Help shows About (the
/// dialog's About page) and opens the log folder, and changes the dialog applies reach the viewer, the timeline, the
/// Effects tab, the proxy jobs and the status bar live through <see cref="AppServices.SettingsChanged"/>.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Creates the Settings dialog state from the settings in effect.</summary>
    /// <param name="page">The page shown first.</param>
    /// <returns>The dialog view model (call <see cref="SettingsViewModel.LoadAsync"/> to fill the cache use and devices).</returns>
    public SettingsViewModel CreateSettingsDialog(SettingsPage page = SettingsPage.General) =>
        new(_services, _dialogs, ClearRecentProjectsAsync, page, ClearCacheForSettingsAsync, AgentServer, Clients);

    private void RegisterSettingsCommands()
    {
        Commands.Register(CommandIds.Settings, OpenSettingsCommand);
        Commands.Register(CommandIds.About, ShowAboutCommand);
        Commands.Register(CommandIds.OpenLogFolder, OpenLogFolderCommand);
        _services.SettingsChanged += OnSettingsChanged;
    }

    [RelayCommand]
    private Task OpenSettingsAsync() => ShowSettingsAsync(SettingsPage.General);

    [RelayCommand]
    private Task ShowAboutAsync() => ShowSettingsAsync(SettingsPage.About);

    // DESIGN 4.5: the red ffmpeg status links to Settings, where the location is chosen.
    [RelayCommand]
    private Task OpenFfmpegSettingsAsync() => ShowSettingsAsync(SettingsPage.Media);

    [RelayCommand]
    private async Task OpenLogFolderAsync()
    {
        var folder = _services.LogFolder;
        try
        {
            await Task.Run(() => Directory.CreateDirectory(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Open log folder", "The log folder " + folder + " cannot be created: " + ex.Message);
            return;
        }

        await _dialogs.RevealInFileManagerAsync(folder);
    }

    private async Task ShowSettingsAsync(SettingsPage page)
    {
        var dialog = CreateSettingsDialog(page);
        _ = dialog.LoadAsync();
        await _dialogs.ShowSettingsDialogAsync(dialog);
    }

    // The export dialog remembers the folder and preset of each queued export in the settings (DESIGN 9 and 10).
    private bool RememberExportChoices(Cutlass.Media.Export.ExportRequest request)
    {
        var folder = Path.GetDirectoryName(request.FinalPath);
        var settings = _services.Settings;
        if (string.Equals(settings.ExportFolder, folder, StringComparison.Ordinal) &&
            string.Equals(settings.ExportPreset, request.Preset.Id, StringComparison.Ordinal))
        {
            return true;
        }

        settings.ExportFolder = folder;
        settings.ExportPreset = request.Preset.Id;
        _ = SaveRememberedExportAsync();
        return true;
    }

    private async Task SaveRememberedExportAsync()
    {
        try
        {
            await _services.SaveSettingsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("The export folder could not be remembered: " + ex.Message);
        }
    }

    // Clear cache (DESIGN 10) through the media pipeline, which also queues the open project's jobs again.
    private async Task<long> ClearCacheForSettingsAsync(CancellationToken cancellationToken)
    {
        await ClearMediaCacheAsync();
        return await _services.MeasureCacheAsync(cancellationToken);
    }

    private async Task ClearRecentProjectsAsync()
    {
        _recent = [];
        await _services.RecentProjects.SaveAsync(_recent);
        await RefreshRecentAsync();
    }

    // Raised on the UI thread after the Settings dialog saved new settings (AppServices already applied the cache
    // limit and the proxy policy of new jobs).
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        var (old, now) = (e.OldSettings, e.NewSettings);
        if (old.DefaultPreviewQuality != now.DefaultPreviewQuality)
        {
            Viewer.Quality = now.DefaultPreviewQuality;
        }

        if (old.UseProxies != now.UseProxies)
        {
            Viewer.UseProxies = now.UseProxies;
        }

        ApplyEditingDefaults(now);
        if (old.AutosaveIntervalMinutes != now.AutosaveIntervalMinutes && _autosave is { } autosave)
        {
            // DESIGN 11: at most once per interval; the new interval counts from the next tick.
            autosave.Interval = AutosaveService.IntervalFromMinutes(now.AutosaveIntervalMinutes);
        }

        if (old.ProxyPolicy != now.ProxyPolicy && HasProject)
        {
            // Media that now qualify get their proxy jobs; existing artifacts are kept.
            EnqueueProjectMedia(Session.Current);
        }

        if (old.CacheLimitGb != now.CacheLimitGb)
        {
            UpdateCacheSummary();
        }

        if (!_services.IsFfmpegReady && !string.Equals(old.FfmpegPath, now.FfmpegPath, StringComparison.Ordinal))
        {
            // Without working services a new ffmpeg is used at once (with them it applies on the next start).
            _ = RedetectFromSettingsAsync(now.FfmpegPath);
        }
    }

    // The defaults the timeline and the Effects tab read when they create clips (DESIGN 10, General).
    private void ApplyEditingDefaults(AppSettings settings)
    {
        Timeline.StillDurationSeconds = settings.DefaultStillSeconds;
        Timeline.ConfirmTrackDeletes = settings.ConfirmDeleteTracks;
        EffectsTab.TransitionLengthSeconds = settings.DefaultTransitionSeconds;
        EffectsTab.TitleLengthSeconds = settings.DefaultTitleSeconds;
    }

    private async Task RedetectFromSettingsAsync(string? ffmpegPath)
    {
        FfmpegStatusText = "Checking ffmpeg...";
        try
        {
            ApplyStartup(await _services.RedetectAsync(ffmpegPath, CancellationToken.None));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            FfmpegStatusText = "ffmpeg check failed";
            FfmpegToolTip = ex.Message;
        }
    }
}
