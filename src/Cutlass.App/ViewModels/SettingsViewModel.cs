using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services;
using Cutlass.Compiler.Export;
using Cutlass.Media.Settings;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The Settings dialog (Ctrl+, DESIGN 10): General, Media, Playback, Agents (AGENT_INTEGRATION 6.1, in
/// <c>SettingsViewModel.Agents.cs</c>) and About. The fields edit a working copy; OK and
/// Apply validate it and save it atomically through <see cref="ISettingsHost.ApplySettingsAsync"/>, which applies it
/// live. Cancel closes without applying what changed since the last Apply (the usual Windows meaning of Apply, so an
/// applied change stays). Clear cache and clearing the recent list act at once, after a confirmation.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>The audio device entry that stands for the OpenAL default device (stored as null).</summary>
    public const string SystemDefaultDevice = "System default";

    /// <summary>The autosave interval range in minutes.</summary>
    public const int MinAutosaveMinutes = 1, MaxAutosaveMinutes = 60;

    /// <summary>The cache size limit range in GB.</summary>
    public const int MinCacheGb = 1, MaxCacheGb = 10_000;

    /// <summary>The default transition duration range in seconds.</summary>
    public const double MinTransitionSeconds = 0.1, MaxTransitionSeconds = 10;

    /// <summary>The default still and title duration range in seconds.</summary>
    public const double MinStillSeconds = 0.1, MaxStillSeconds = 600;

    private readonly ISettingsHost _host;
    private readonly IDialogService _dialogs;
    private readonly Func<Task>? _clearRecent;
    private readonly Func<CancellationToken, Task<long>> _clearCache;
    private CancellationTokenSource? _ffmpegCheck;
    private string? _checkedFfmpegText;
    private FfmpegCheck? _lastCheck;

    /// <summary>Creates the dialog state from the settings in effect.</summary>
    /// <param name="host">The app side (settings, background work, saving).</param>
    /// <param name="dialogs">Pickers, confirmations and the file manager.</param>
    /// <param name="clearRecent">Clears the recent projects list, or null when there is none.</param>
    /// <param name="page">The page shown first.</param>
    /// <param name="clearCache">Clears the cache and answers the bytes still in use, or null for
    /// <see cref="ISettingsHost.ClearCacheAsync"/> (the main window also queues the open project's jobs again).</param>
    /// <param name="agents">The agent server for the Agents page (status, token, MCP paths), or null when there is none.</param>
    public SettingsViewModel(
        ISettingsHost host,
        IDialogService dialogs,
        Func<Task>? clearRecent = null,
        SettingsPage page = SettingsPage.General,
        Func<CancellationToken, Task<long>>? clearCache = null,
        Services.Agent.IAgentSettingsHost? agents = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(dialogs);
        _host = host;
        _dialogs = dialogs;
        _clearRecent = clearRecent;
        _clearCache = clearCache ?? host.ClearCacheAsync;
        ExportPresetChoices = ExportPresets.All;
        ProxyPolicies = Enum.GetValues<ProxyPolicy>();
        Qualities = Enum.GetValues<PreviewQuality>();
        AudioDevices = [SystemDefaultDevice];
        CacheFolder = host.CacheFolder;
        CacheUsageText = "Measuring...";
        FfmpegStatusText = host.IsFfmpegReady ? "in use: " + host.FfmpegSummary : "not found";
        IsFfmpegOk = host.IsFfmpegReady;
        AttachAgents(agents);
        LoadFrom(host.Settings);
        SelectedPageIndex = (int)page;
    }

    /// <summary>Raised when the dialog should close; the argument is true when settings were applied with OK.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>The page shown (the tab index, see <see cref="SettingsPage"/>).</summary>
    [ObservableProperty]
    public partial int SelectedPageIndex { get; set; }

    // ---- General

    /// <summary>The autosave interval in whole minutes, as typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(ApplyCommand))]
    public partial string AutosaveMinutesText { get; set; } = string.Empty;

    /// <summary>The default transition duration in seconds, as typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(ApplyCommand))]
    public partial string TransitionSecondsText { get; set; } = string.Empty;

    /// <summary>The default still duration in seconds, as typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(ApplyCommand))]
    public partial string StillSecondsText { get; set; } = string.Empty;

    /// <summary>The default title duration in seconds, as typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(ApplyCommand))]
    public partial string TitleSecondsText { get; set; } = string.Empty;

    /// <summary>Ask before deleting a track that holds clips.</summary>
    [ObservableProperty]
    public partial bool ConfirmDeleteTracks { get; set; }

    /// <summary>The export dialog's folder, empty for the project's folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(ApplyCommand))]
    public partial string ExportFolder { get; set; } = string.Empty;

    /// <summary>The preset the export dialog selects first.</summary>
    [ObservableProperty]
    public partial ExportPreset SelectedExportPreset { get; set; } = ExportPresets.Default;

    /// <summary>Every export preset.</summary>
    public IReadOnlyList<ExportPreset> ExportPresetChoices { get; }

    /// <summary>The result of the last "Clear recent projects", or empty.</summary>
    [ObservableProperty]
    public partial string RecentStatusText { get; set; } = string.Empty;

    /// <summary>True when the recent list can be cleared.</summary>
    public bool CanClearRecent => _clearRecent is not null;

    // ---- Media

    /// <summary>The ffmpeg executable or folder, empty to search PATH and the well-known folders.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyPropertyChangedFor(nameof(UsesPathSearch))]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(ApplyCommand))]
    public partial string FfmpegPathText { get; set; } = string.Empty;

    /// <summary>The ffmpeg status line, for example "found: N-126308, libavfilter 12".</summary>
    [ObservableProperty]
    public partial string FfmpegStatusText { get; set; } = string.Empty;

    /// <summary>True when the last check (or the running ffmpeg) is usable.</summary>
    [ObservableProperty]
    public partial bool IsFfmpegOk { get; set; }

    /// <summary>True while an ffmpeg check runs.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(ApplyCommand))]
    public partial bool IsCheckingFfmpeg { get; set; }

    /// <summary>True when no location is set, so ffmpeg is searched for.</summary>
    public bool UsesPathSearch => string.IsNullOrWhiteSpace(FfmpegPathText);

    /// <summary>Shown under the ffmpeg location: when a new one takes effect.</summary>
    public string FfmpegRestartNote => _host.IsFfmpegReady
        ? "A different ffmpeg takes effect the next time Cutlass starts."
        : "A working ffmpeg is used as soon as you apply it.";

    /// <summary>The proxy policy.</summary>
    [ObservableProperty]
    public partial ProxyPolicy SelectedProxyPolicy { get; set; }

    /// <summary>The proxy policies (Auto, Always, Never).</summary>
    public IReadOnlyList<ProxyPolicy> ProxyPolicies { get; }

    /// <summary>The cache folder (read-only).</summary>
    public string CacheFolder { get; }

    /// <summary>The cache size limit in whole GB, as typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(ApplyCommand))]
    public partial string CacheLimitText { get; set; } = string.Empty;

    /// <summary>The cache use, for example "3.1 GB used".</summary>
    [ObservableProperty]
    public partial string CacheUsageText { get; set; } = string.Empty;

    /// <summary>True while the cache is being cleared.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCacheCommand))]
    public partial bool IsClearingCache { get; set; }

    // ---- Playback

    /// <summary>The default preview quality.</summary>
    [ObservableProperty]
    public partial PreviewQuality SelectedQuality { get; set; }

    /// <summary>The preview qualities.</summary>
    public IReadOnlyList<PreviewQuality> Qualities { get; }

    /// <summary>"System default" followed by the OpenAL devices (filled by <see cref="LoadAsync"/>).</summary>
    public ObservableCollection<string> AudioDevices { get; }

    /// <summary>The audio output device, <see cref="SystemDefaultDevice"/> for the default.</summary>
    [ObservableProperty]
    public partial string SelectedAudioDevice { get; set; } = string.Empty;

    /// <summary>Use proxies for preview when they exist.</summary>
    [ObservableProperty]
    public partial bool UseProxies { get; set; }

    /// <summary>When a new audio device takes effect (the playback session keeps the device it opened).</summary>
    public static string AudioDeviceNote => "A different device takes effect the next time Cutlass starts.";

    // ---- About

    /// <summary>The Cutlass version, for example "Cutlass 0.1.0".</summary>
    public static string VersionText => "Cutlass " + AppVersion;

    /// <summary>The runtime and OS.</summary>
    public static string RuntimeText =>
        ".NET " + Environment.Version.ToString() + ", " + System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim() +
        " (" + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant() + ")";

    /// <summary>The ffmpeg build in use.</summary>
    public string AboutFfmpegText => "ffmpeg: " + _host.FfmpegSummary;

    /// <summary>License notes (DESIGN 10: Inter is under the SIL OFL).</summary>
    public static string LicenseText =>
        "Cutlass is Copyright (c) 2026 LouiDev.\n" +
        "The Inter typeface is used under the SIL Open Font License 1.1 (fonts/OFL.txt next to the app).\n" +
        "Avalonia, CommunityToolkit.Mvvm and Silk.NET are used under the MIT License; OpenAL Soft under the LGPL.\n" +
        "ffmpeg is not bundled: Cutlass runs the ffmpeg and ffprobe you install, under their own license.";

    /// <summary>Project links as plain text (selectable, nothing is opened).</summary>
    public static string LinksText =>
        "Inter: https://rsms.me/inter/\nAvalonia: https://avaloniaui.net/\nffmpeg: https://ffmpeg.org/\nOpenAL Soft: https://openal-soft.org/";

    /// <summary>The app version from the assembly (the informational version without a build suffix).</summary>
    public static string AppVersion
    {
        get
        {
            var assembly = typeof(SettingsViewModel).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "0.0.0";
            var plus = version.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? version[..plus] : version;
        }
    }

    // ---- State

    /// <summary>The first validation problem, or null when the settings can be applied.</summary>
    public string? ProblemText => Validate(out _);

    /// <summary>True while settings are being saved.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(ApplyCommand))]
    public partial bool IsApplying { get; set; }

    /// <summary>The last save error, or null.</summary>
    [ObservableProperty]
    public partial string? ApplyErrorText { get; set; }

    /// <summary>Parses a whole number typed in the invariant culture within a range.</summary>
    /// <param name="text">The text.</param>
    /// <param name="min">The minimum.</param>
    /// <param name="max">The maximum.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when valid.</returns>
    public static bool TryParseWhole(string? text, int min, int max, out int value) =>
        int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    /// <summary>Parses seconds typed in the invariant culture (a point as the decimal separator) within a range.</summary>
    /// <param name="text">The text.</param>
    /// <param name="min">The minimum.</param>
    /// <param name="max">The maximum.</param>
    /// <param name="value">The value.</param>
    /// <returns>True when valid.</returns>
    public static bool TryParseSeconds(string? text, double min, double max, out double value) =>
        double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value) && value >= min && value <= max;

    /// <summary>Formats seconds for a field, for example "1" or "0.5".</summary>
    /// <param name="value">The seconds.</param>
    /// <returns>The text.</returns>
    public static string FormatSeconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Loads what needs the background: the cache use and the OpenAL device list. The dialog shows at once and fills
    /// these in when they arrive.
    /// </summary>
    /// <returns>A task that completes when both are loaded.</returns>
    public async Task LoadAsync()
    {
        var usage = _host.MeasureCacheAsync(CancellationToken.None);
        var devices = _host.ListAudioDevicesAsync(CancellationToken.None);
        var agents = LoadAgentsAsync();
        try
        {
            CacheUsageText = FormatUsage(await usage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CacheUsageText = "Could not measure: " + ex.Message;
        }

        var selected = SelectedAudioDevice;
        foreach (var device in await devices)
        {
            if (!AudioDevices.Contains(device))
            {
                AudioDevices.Add(device);
            }
        }

        // Re-assert the selection now that the list holds it.
        SelectedAudioDevice = AudioDevices.Contains(selected) ? selected : SystemDefaultDevice;
        await agents;
    }

    /// <summary>Builds the settings the dialog would apply, on top of the settings in effect (layout fields stay).</summary>
    /// <returns>The settings, or null when a field is invalid (see <see cref="ProblemText"/>).</returns>
    public AppSettings? BuildSettings() => Validate(out var settings) is null ? settings : null;

    /// <summary>Validates the fields and applies them (Apply). Returns false when a field is invalid or the save failed.</summary>
    /// <returns>True when applied.</returns>
    public async Task<bool> ApplyChangesAsync()
    {
        ApplyErrorText = null;
        var problem = Validate(out var settings);
        if (problem is not null || settings is null)
        {
            ApplyErrorText = problem;
            return false;
        }

        // A typed ffmpeg location is checked before it is stored, so a typo never replaces a working one.
        var location = NormalizedFfmpegPath();
        if (location is not null && !string.Equals(location, _host.Settings.FfmpegPath, StringComparison.Ordinal))
        {
            var check = _lastCheck is not null && string.Equals(_checkedFfmpegText, location, StringComparison.Ordinal)
                ? _lastCheck
                : await CheckFfmpegCoreAsync(location);
            if (check is null || !check.IsUsable)
            {
                ApplyErrorText = "ffmpeg: " + (check?.StatusText ?? "the check was cancelled");
                return false;
            }

            settings.FfmpegPath = check.FfmpegPath;
        }

        IsApplying = true;
        try
        {
            await _host.ApplySettingsAsync(settings, CancellationToken.None);
            LoadFrom(settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ApplyErrorText = "The settings could not be saved: " + ex.Message;
            return false;
        }
        finally
        {
            IsApplying = false;
        }
    }

    /// <summary>Checks the ffmpeg location in the field ("Validate"), off the UI thread.</summary>
    /// <returns>The result, or null when a newer check replaced it.</returns>
    public Task<FfmpegCheck?> CheckFfmpegAsync() => CheckFfmpegCoreAsync(NormalizedFfmpegPath());

    private static string FormatUsage(long bytes) => AppServices.FormatBytes(bytes) + " used";

    private bool CanApply() => !IsApplying && !IsCheckingFfmpeg && ProblemText is null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task OkAsync()
    {
        if (await ApplyChangesAsync())
        {
            CloseRequested?.Invoke(this, true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyAsync() => ApplyChangesAsync();

    [RelayCommand]
    private void Cancel()
    {
        _ffmpegCheck?.Cancel();
        CloseRequested?.Invoke(this, false);
    }

    [RelayCommand]
    private Task ValidateFfmpegAsync() => CheckFfmpegAsync();

    [RelayCommand]
    private async Task BrowseFfmpegAsync()
    {
        if (await _dialogs.PickFfmpegAsync() is { } path)
        {
            FfmpegPathText = path;
            await CheckFfmpegAsync();
        }
    }

    [RelayCommand]
    private async Task UsePathAsync()
    {
        FfmpegPathText = string.Empty;
        await CheckFfmpegAsync();
    }

    [RelayCommand]
    private async Task BrowseExportFolderAsync()
    {
        var start = string.IsNullOrWhiteSpace(ExportFolder) ? null : ExportFolder;
        if (await _dialogs.PickFolderAsync("Default export folder", start) is { } folder)
        {
            ExportFolder = folder;
        }
    }

    [RelayCommand]
    private void UseProjectFolder() => ExportFolder = string.Empty;

    [RelayCommand]
    private Task OpenCacheFolderAsync() => _dialogs.RevealInFileManagerAsync(CacheFolder);

    private bool CanClearCache() => !IsClearingCache;

    [RelayCommand(CanExecute = nameof(CanClearCache))]
    private async Task ClearCacheAsync()
    {
        if (!await _dialogs.ConfirmAsync(
                "Clear cache",
                "Delete the cached thumbnails, waveforms, frame indexes and proxies? Media that a running job works on keep theirs, and the open project's media are rebuilt in the background.",
                "Clear cache"))
        {
            return;
        }

        IsClearingCache = true;
        CacheUsageText = "Clearing...";
        try
        {
            CacheUsageText = FormatUsage(await _clearCache(CancellationToken.None));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CacheUsageText = "Could not clear: " + ex.Message;
        }
        finally
        {
            IsClearingCache = false;
        }
    }

    [RelayCommand]
    private async Task ClearRecentAsync()
    {
        if (_clearRecent is null ||
            !await _dialogs.ConfirmAsync("Clear recent projects", "Remove every project from the recent list? The files stay where they are.", "Clear"))
        {
            return;
        }

        try
        {
            await _clearRecent();
            RecentStatusText = "The recent list is empty.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RecentStatusText = "Could not clear the list: " + ex.Message;
        }
    }

    private async Task<FfmpegCheck?> CheckFfmpegCoreAsync(string? location)
    {
        _ffmpegCheck?.Cancel();
        var cts = new CancellationTokenSource();
        _ffmpegCheck = cts;
        IsCheckingFfmpeg = true;
        FfmpegStatusText = "Checking...";
        try
        {
            var result = await _host.CheckFfmpegAsync(location, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return null;
            }

            _checkedFfmpegText = location;
            _lastCheck = result;
            IsFfmpegOk = result.IsUsable;
            FfmpegStatusText = result.IsUsable && location is null && result.FfmpegPath is { } found
                ? result.StatusText + " (" + found + ")"
                : result.StatusText;
            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (ReferenceEquals(_ffmpegCheck, cts))
            {
                IsCheckingFfmpeg = false;
                _ffmpegCheck = null;
            }

            cts.Dispose();
        }
    }

    private string? NormalizedFfmpegPath()
    {
        var text = (FfmpegPathText ?? string.Empty).Trim().Trim('"').Trim();
        return text.Length == 0 ? null : text;
    }

    private void LoadFrom(AppSettings settings)
    {
        AutosaveMinutesText = settings.AutosaveIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        TransitionSecondsText = FormatSeconds(settings.DefaultTransitionSeconds);
        StillSecondsText = FormatSeconds(settings.DefaultStillSeconds);
        TitleSecondsText = FormatSeconds(settings.DefaultTitleSeconds);
        ConfirmDeleteTracks = settings.ConfirmDeleteTracks;
        ExportFolder = settings.ExportFolder ?? string.Empty;
        SelectedExportPreset = ExportPresets.Find(settings.ExportPreset) ?? ExportPresets.Default;
        FfmpegPathText = settings.FfmpegPath ?? string.Empty;
        SelectedProxyPolicy = settings.ProxyPolicy;
        CacheLimitText = settings.CacheLimitGb.ToString(CultureInfo.InvariantCulture);
        SelectedQuality = settings.DefaultPreviewQuality;
        UseProxies = settings.UseProxies;
        var device = string.IsNullOrEmpty(settings.AudioDevice) ? SystemDefaultDevice : settings.AudioDevice;
        if (!AudioDevices.Contains(device))
        {
            // A device that is not plugged in now stays selectable, so OK does not silently reset it.
            AudioDevices.Add(device);
        }

        SelectedAudioDevice = device;
        LoadAgentsFrom(settings);
    }

    // The first problem in tab order, and the settings when there is none.
    private string? Validate(out AppSettings? settings)
    {
        settings = null;
        if (!TryParseWhole(AutosaveMinutesText, MinAutosaveMinutes, MaxAutosaveMinutes, out var autosave))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Autosave interval: enter whole minutes from {MinAutosaveMinutes} to {MaxAutosaveMinutes}.");
        }

        if (!TryParseSeconds(TransitionSecondsText, MinTransitionSeconds, MaxTransitionSeconds, out var transition))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Default transition: enter seconds from {MinTransitionSeconds} to {MaxTransitionSeconds} (for example 1.5).");
        }

        if (!TryParseSeconds(StillSecondsText, MinStillSeconds, MaxStillSeconds, out var still))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Default still duration: enter seconds from {MinStillSeconds} to {MaxStillSeconds}.");
        }

        if (!TryParseSeconds(TitleSecondsText, MinStillSeconds, MaxStillSeconds, out var title))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Default title duration: enter seconds from {MinStillSeconds} to {MaxStillSeconds}.");
        }

        var exportFolder = (ExportFolder ?? string.Empty).Trim();
        if (exportFolder.Length > 0 && !IsFullPath(exportFolder))
        {
            return "Export folder: enter a full path, or use the project folder.";
        }

        var ffmpeg = NormalizedFfmpegPath();
        if (ffmpeg is not null && !IsFullPath(ffmpeg))
        {
            return "ffmpeg location: enter a full path to ffmpeg or its folder, or use PATH.";
        }

        if (!TryParseWhole(CacheLimitText, MinCacheGb, MaxCacheGb, out var cacheGb))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Cache size limit: enter whole GB from {MinCacheGb} to {MaxCacheGb}.");
        }

        settings = _host.Settings with
        {
            AutosaveIntervalMinutes = autosave,
            DefaultTransitionSeconds = transition,
            DefaultStillSeconds = still,
            DefaultTitleSeconds = title,
            ConfirmDeleteTracks = ConfirmDeleteTracks,
            ExportFolder = exportFolder.Length == 0 ? null : Path.GetFullPath(exportFolder),
            ExportPreset = (SelectedExportPreset ?? ExportPresets.Default).Id,
            FfmpegPath = ffmpeg is null ? null : _host.Settings.FfmpegPath is { } saved && SamePathText(saved, ffmpeg) ? saved : ffmpeg,
            ProxyPolicy = SelectedProxyPolicy,
            CacheLimitGb = cacheGb,
            DefaultPreviewQuality = SelectedQuality,
            UseProxies = UseProxies,
            AudioDevice = SelectedAudioDevice is null or SystemDefaultDevice ? null : SelectedAudioDevice,
        };

        // The Agents page comes after Playback in tab order (AGENT_INTEGRATION 6.1).
        var agentProblem = ValidateAgents(settings, out var withAgents);
        settings = agentProblem is null ? withAgents : null;
        return agentProblem;
    }

    private static bool SamePathText(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsFullPath(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path) && Path.GetFullPath(path).Length > 0;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
