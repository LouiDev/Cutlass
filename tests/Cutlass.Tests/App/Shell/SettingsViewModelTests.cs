using Cutlass.App.Services;
using Cutlass.App.Services.Ffmpeg;
using Cutlass.App.ViewModels.Dialogs;
using Cutlass.Compiler.Export;
using Cutlass.Media.Settings;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.App.Shell;

/// <summary>The Settings dialog (DESIGN 10): loading, validation messages, Apply, OK and Cancel, ffmpeg checks and the cache.</summary>
public sealed class SettingsViewModelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "cutlass-settings-tests");

    [Fact]
    public void LoadsTheSettingsInEffect()
    {
        var host = new FakeSettingsHost(new AppSettings
        {
            AutosaveIntervalMinutes = 7,
            DefaultTransitionSeconds = 0.5,
            DefaultStillSeconds = 3,
            DefaultTitleSeconds = 4.25,
            ConfirmDeleteTracks = false,
            ProxyPolicy = ProxyPolicy.Always,
            CacheLimitGb = 40,
            DefaultPreviewQuality = PreviewQuality.Quarter,
            UseProxies = false,
            ExportPreset = "vp9",
        });
        var vm = new SettingsViewModel(host, new FakeDialogService());
        Assert.Equal("7", vm.AutosaveMinutesText);
        Assert.Equal("0.5", vm.TransitionSecondsText);
        Assert.Equal("3", vm.StillSecondsText);
        Assert.Equal("4.25", vm.TitleSecondsText);
        Assert.False(vm.ConfirmDeleteTracks);
        Assert.Equal(ProxyPolicy.Always, vm.SelectedProxyPolicy);
        Assert.Equal("40", vm.CacheLimitText);
        Assert.Equal(PreviewQuality.Quarter, vm.SelectedQuality);
        Assert.False(vm.UseProxies);
        Assert.Same(ExportPresets.Vp9, vm.SelectedExportPreset);
        Assert.Equal(string.Empty, vm.FfmpegPathText);
        Assert.True(vm.UsesPathSearch);
        Assert.Equal(SettingsViewModel.SystemDefaultDevice, vm.SelectedAudioDevice);
        Assert.Equal(host.CacheFolder, vm.CacheFolder);
        Assert.Null(vm.ProblemText);
    }

    [Fact]
    [UseCulture("de-DE")]
    public void NumbersAreInvariantEvenUnderAGermanCulture()
    {
        var vm = new SettingsViewModel(new FakeSettingsHost(new AppSettings { DefaultTransitionSeconds = 1.5 }), new FakeDialogService());
        Assert.Equal("1.5", vm.TransitionSecondsText);
        vm.TransitionSecondsText = "1,5";
        Assert.StartsWith("Default transition:", vm.ProblemText, StringComparison.Ordinal);
        vm.TransitionSecondsText = "1.25";
        Assert.Null(vm.ProblemText);
        Assert.Equal(1.25, vm.BuildSettings()!.DefaultTransitionSeconds);
    }

    [Theory]
    [InlineData("autosave", "0", "Autosave interval: enter whole minutes from 1 to 60.")]
    [InlineData("autosave", "61", "Autosave interval: enter whole minutes from 1 to 60.")]
    [InlineData("autosave", "2.5", "Autosave interval: enter whole minutes from 1 to 60.")]
    [InlineData("transition", "0", "Default transition: enter seconds from 0.1 to 10 (for example 1.5).")]
    [InlineData("transition", "abc", "Default transition: enter seconds from 0.1 to 10 (for example 1.5).")]
    [InlineData("still", "601", "Default still duration: enter seconds from 0.1 to 600.")]
    [InlineData("title", "NaN", "Default title duration: enter seconds from 0.1 to 600.")]
    [InlineData("cache", "0", "Cache size limit: enter whole GB from 1 to 10000.")]
    [InlineData("cache", "", "Cache size limit: enter whole GB from 1 to 10000.")]
    [InlineData("export", "relative/folder", "Export folder: enter a full path, or use the project folder.")]
    [InlineData("ffmpeg", "bin/ffmpeg", "ffmpeg location: enter a full path to ffmpeg or its folder, or use PATH.")]
    public void InvalidFieldsExplainThemselvesAndBlockApply(string field, string text, string message)
    {
        var vm = new SettingsViewModel(new FakeSettingsHost(new AppSettings()), new FakeDialogService());
        switch (field)
        {
            case "autosave": vm.AutosaveMinutesText = text; break;
            case "transition": vm.TransitionSecondsText = text; break;
            case "still": vm.StillSecondsText = text; break;
            case "title": vm.TitleSecondsText = text; break;
            case "cache": vm.CacheLimitText = text; break;
            case "export": vm.ExportFolder = text; break;
            case "ffmpeg": vm.FfmpegPathText = text; break;
        }

        Assert.Equal(message, vm.ProblemText);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        Assert.False(vm.OkCommand.CanExecute(null));
        Assert.Null(vm.BuildSettings());
    }

    [Fact]
    public async Task ApplySavesTheFieldsAndKeepsTheLayout()
    {
        var host = new FakeSettingsHost(new AppSettings { TopRowFraction = 0.6, DefaultTransition = "WipeLeft" });
        var vm = new SettingsViewModel(host, new FakeDialogService());
        vm.AutosaveMinutesText = " 5 ";
        vm.CacheLimitText = "30";
        vm.SelectedProxyPolicy = ProxyPolicy.Never;
        vm.SelectedQuality = PreviewQuality.Half;
        vm.UseProxies = false;
        vm.ConfirmDeleteTracks = false;
        vm.SelectedExportPreset = ExportPresets.ProRes;
        vm.ExportFolder = Root;

        Assert.True(await vm.ApplyChangesAsync());

        var saved = Assert.Single(host.Applied);
        Assert.Equal(5, saved.AutosaveIntervalMinutes);
        Assert.Equal(30, saved.CacheLimitGb);
        Assert.Equal(ProxyPolicy.Never, saved.ProxyPolicy);
        Assert.Equal(PreviewQuality.Half, saved.DefaultPreviewQuality);
        Assert.False(saved.UseProxies);
        Assert.False(saved.ConfirmDeleteTracks);
        Assert.Equal("prores", saved.ExportPreset);
        Assert.Equal(Path.GetFullPath(Root), saved.ExportFolder);
        Assert.Equal(0.6, saved.TopRowFraction);
        Assert.Equal("WipeLeft", saved.DefaultTransition);
        Assert.Null(saved.AudioDevice);
        Assert.Same(saved, host.Settings);
    }

    [Fact]
    public async Task CancelDiscardsWhatWasNotApplied()
    {
        var host = new FakeSettingsHost(new AppSettings());
        var vm = new SettingsViewModel(host, new FakeDialogService());
        bool? closed = null;
        vm.CloseRequested += (_, applied) => closed = applied;

        vm.AutosaveMinutesText = "9";
        await vm.ApplyCommand.ExecuteAsync(null);
        vm.AutosaveMinutesText = "11";
        vm.CancelCommand.Execute(null);

        Assert.False(closed);
        Assert.Equal(9, Assert.Single(host.Applied).AutosaveIntervalMinutes);
        Assert.Equal(9, host.Settings.AutosaveIntervalMinutes);
    }

    [Fact]
    public async Task OkAppliesAndCloses()
    {
        var host = new FakeSettingsHost(new AppSettings());
        var vm = new SettingsViewModel(host, new FakeDialogService());
        bool? closed = null;
        vm.CloseRequested += (_, applied) => closed = applied;
        vm.TitleSecondsText = "2";
        await vm.OkCommand.ExecuteAsync(null);
        Assert.True(closed);
        Assert.Equal(2, host.Settings.DefaultTitleSeconds);
    }

    [Fact]
    public async Task ASaveErrorKeepsTheDialogOpenWithTheReason()
    {
        var host = new FakeSettingsHost(new AppSettings()) { SaveError = new IOException("disk full") };
        var vm = new SettingsViewModel(host, new FakeDialogService());
        bool? closed = null;
        vm.CloseRequested += (_, applied) => closed = applied;
        await vm.OkCommand.ExecuteAsync(null);
        Assert.Null(closed);
        Assert.Equal("The settings could not be saved: disk full", vm.ApplyErrorText);
    }

    [Fact]
    public async Task ATypedFfmpegIsCheckedBeforeItIsStored()
    {
        var host = new FakeSettingsHost(new AppSettings());
        host.Checks[Path.Combine(Root, "bad")] = new FfmpegCheck(false, "ffprobe was not found next to it", null);
        var vm = new SettingsViewModel(host, new FakeDialogService());

        vm.FfmpegPathText = Path.Combine(Root, "bad");
        Assert.False(await vm.ApplyChangesAsync());
        Assert.Equal("ffmpeg: ffprobe was not found next to it", vm.ApplyErrorText);
        Assert.Empty(host.Applied);
        Assert.False(vm.IsFfmpegOk);

        var good = Path.Combine(Root, "good");
        host.Checks[good] = new FfmpegCheck(true, "found: N-126308, libavfilter 12", Path.Combine(good, "ffmpeg.exe"));
        vm.FfmpegPathText = "\"" + good + "\"";
        Assert.True(await vm.ApplyChangesAsync());
        Assert.Equal(Path.Combine(good, "ffmpeg.exe"), host.Settings.FfmpegPath);
        Assert.Equal([Path.Combine(Root, "bad"), good], host.CheckedPaths);
        Assert.True(vm.IsFfmpegOk);
        Assert.Equal("found: N-126308, libavfilter 12", vm.FfmpegStatusText);
    }

    [Fact]
    public async Task ValidateShowsTheResultAndApplyReusesIt()
    {
        var host = new FakeSettingsHost(new AppSettings());
        var path = Path.Combine(Root, "ffmpeg");
        host.Checks[path] = new FfmpegCheck(true, "found: 7.1, libavfilter 10", path);
        var vm = new SettingsViewModel(host, new FakeDialogService());
        vm.FfmpegPathText = path;
        await vm.ValidateFfmpegCommand.ExecuteAsync(null);
        Assert.Equal("found: 7.1, libavfilter 10", vm.FfmpegStatusText);
        Assert.False(vm.IsCheckingFfmpeg);
        Assert.True(await vm.ApplyChangesAsync());
        Assert.Single(host.CheckedPaths);
    }

    [Fact]
    public async Task UsePathClearsTheLocationAndSearches()
    {
        var host = new FakeSettingsHost(new AppSettings { FfmpegPath = Path.Combine(Root, "ffmpeg") });
        host.Checks[string.Empty] = new FfmpegCheck(true, "found: N-1, libavfilter 12", Path.Combine(Root, "path", "ffmpeg"));
        var vm = new SettingsViewModel(host, new FakeDialogService());
        Assert.False(vm.UsesPathSearch);
        await vm.UsePathCommand.ExecuteAsync(null);
        Assert.True(vm.UsesPathSearch);
        Assert.Equal([string.Empty], host.CheckedPaths);
        Assert.Contains("(" + Path.Combine(Root, "path", "ffmpeg") + ")", vm.FfmpegStatusText, StringComparison.Ordinal);
        Assert.True(await vm.ApplyChangesAsync());
        Assert.Null(host.Settings.FfmpegPath);
    }

    [Fact]
    public async Task BrowsePicksAndChecksTheFfmpeg()
    {
        var host = new FakeSettingsHost(new AppSettings());
        var picked = Path.Combine(Root, "picked", "ffmpeg.exe");
        host.Checks[picked] = new FfmpegCheck(true, "found: N-2, libavfilter 11", picked);
        var dialogs = new FakeDialogService { NextFfmpeg = picked };
        var vm = new SettingsViewModel(host, dialogs);
        await vm.BrowseFfmpegCommand.ExecuteAsync(null);
        Assert.Equal(picked, vm.FfmpegPathText);
        Assert.Equal("found: N-2, libavfilter 11", vm.FfmpegStatusText);
    }

    [Fact]
    public async Task LoadMeasuresTheCacheAndListsDevices()
    {
        var host = new FakeSettingsHost(new AppSettings { AudioDevice = "USB Headset" }) { UsedBytes = 3L * 1024 * 1024 * 1024 };
        host.Devices.AddRange(["Speakers", "Headphones"]);
        var vm = new SettingsViewModel(host, new FakeDialogService());
        Assert.Equal("Measuring...", vm.CacheUsageText);
        await vm.LoadAsync();
        Assert.Equal("3.0 GB used", vm.CacheUsageText);

        // The saved device is not plugged in: it stays selectable so OK keeps it.
        Assert.Equal([SettingsViewModel.SystemDefaultDevice, "USB Headset", "Speakers", "Headphones"], vm.AudioDevices);
        Assert.Equal("USB Headset", vm.SelectedAudioDevice);
        vm.SelectedAudioDevice = SettingsViewModel.SystemDefaultDevice;
        Assert.Null(vm.BuildSettings()!.AudioDevice);
        vm.SelectedAudioDevice = "Speakers";
        Assert.Equal("Speakers", vm.BuildSettings()!.AudioDevice);
    }

    [Fact]
    public async Task ClearCacheAsksFirst()
    {
        var host = new FakeSettingsHost(new AppSettings()) { UsedBytes = 5000 };
        var dialogs = new FakeDialogService();
        var vm = new SettingsViewModel(host, dialogs);
        await vm.ClearCacheCommand.ExecuteAsync(null);
        Assert.Equal(0, host.Clears);

        dialogs.ConfirmAnswer = true;
        await vm.ClearCacheCommand.ExecuteAsync(null);
        Assert.Equal(1, host.Clears);
        Assert.Equal("0 B used", vm.CacheUsageText);
        Assert.False(vm.IsClearingCache);
    }

    [Fact]
    public async Task ClearRecentAsksFirst()
    {
        var cleared = 0;
        var dialogs = new FakeDialogService();
        var vm = new SettingsViewModel(new FakeSettingsHost(new AppSettings()), dialogs, () =>
        {
            cleared++;
            return Task.CompletedTask;
        });
        Assert.True(vm.CanClearRecent);
        await vm.ClearRecentCommand.ExecuteAsync(null);
        Assert.Equal(0, cleared);
        dialogs.ConfirmAnswer = true;
        await vm.ClearRecentCommand.ExecuteAsync(null);
        Assert.Equal(1, cleared);
        Assert.Equal("The recent list is empty.", vm.RecentStatusText);
    }

    [Fact]
    public async Task ExportFolderBrowseAndReset()
    {
        var dialogs = new FakeDialogService { NextFolder = Root };
        var vm = new SettingsViewModel(new FakeSettingsHost(new AppSettings()), dialogs);
        await vm.BrowseExportFolderCommand.ExecuteAsync(null);
        Assert.Equal(Root, vm.ExportFolder);
        vm.UseProjectFolderCommand.Execute(null);
        Assert.Equal(string.Empty, vm.ExportFolder);
        Assert.Null(vm.BuildSettings()!.ExportFolder);
    }

    [Fact]
    public void OpensOnTheRequestedPageAndShowsAbout()
    {
        var host = new FakeSettingsHost(new AppSettings()) { Summary = "N-126308 (libavfilter 12.3), /usr/bin/ffmpeg" };
        var vm = new SettingsViewModel(host, new FakeDialogService(), page: SettingsPage.About);
        Assert.Equal((int)SettingsPage.About, vm.SelectedPageIndex);
        Assert.Equal("ffmpeg: N-126308 (libavfilter 12.3), /usr/bin/ffmpeg", vm.AboutFfmpegText);
        Assert.StartsWith("Cutlass ", SettingsViewModel.VersionText, StringComparison.Ordinal);
        Assert.Matches(@"^\d+\.\d+\.\d+", SettingsViewModel.AppVersion);
        Assert.Contains("SIL Open Font License", SettingsViewModel.LicenseText, StringComparison.Ordinal);
        Assert.False(vm.CanClearRecent);
    }

    [Fact]
    public void TheRestartNoteFollowsTheFfmpegState()
    {
        Assert.Contains("next time", new SettingsViewModel(new FakeSettingsHost(new AppSettings()) { Ready = true }, new FakeDialogService()).FfmpegRestartNote, StringComparison.Ordinal);
        Assert.Contains("as soon as", new SettingsViewModel(new FakeSettingsHost(new AppSettings()) { Ready = false }, new FakeDialogService()).FfmpegRestartNote, StringComparison.Ordinal);
    }

    /// <summary>A settings host that records applies and answers checks from a table.</summary>
    private sealed class FakeSettingsHost : ISettingsHost
    {
        public FakeSettingsHost(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; private set; }

        public string CacheFolder => Path.Combine(Root, "cache");

        public bool Ready { get; init; } = true;

        public bool IsFfmpegReady => Ready;

        public string Summary { get; init; } = "N-126308 (libavfilter 12.0), ffmpeg";

        public string FfmpegSummary => Summary;

        public long UsedBytes { get; set; }

        public int Clears { get; private set; }

        public Exception? SaveError { get; init; }

        public List<AppSettings> Applied { get; } = [];

        public List<string> Devices { get; } = [];

        public Dictionary<string, FfmpegCheck> Checks { get; } = [];

        public List<string> CheckedPaths { get; } = [];

        public Task<FfmpegCheck> CheckFfmpegAsync(string? path, CancellationToken cancellationToken)
        {
            CheckedPaths.Add(path ?? string.Empty);
            return Task.FromResult(Checks.TryGetValue(path ?? string.Empty, out var check) ? check : new FfmpegCheck(false, "not found", null));
        }

        public Task<long> MeasureCacheAsync(CancellationToken cancellationToken) => Task.FromResult(UsedBytes);

        public Task<long> ClearCacheAsync(CancellationToken cancellationToken)
        {
            Clears++;
            UsedBytes = 0;
            return Task.FromResult(UsedBytes);
        }

        public Task<IReadOnlyList<string>> ListAudioDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([.. Devices]);

        public Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            if (SaveError is not null)
            {
                return Task.FromException(SaveError);
            }

            Applied.Add(settings);
            Settings = settings;
            return Task.CompletedTask;
        }
    }
}
