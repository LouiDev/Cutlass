using Avalonia.Input;
using Cutlass.App;
using Cutlass.App.Services;
using Cutlass.App.Services.Commands;
using Cutlass.App.ViewModels;
using Cutlass.App.ViewModels.Dialogs;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;
using Cutlass.Media.Settings;

namespace Cutlass.Tests.App.Shell;

/// <summary>
/// The Settings dialog wired into the app: Ctrl+, and the Help commands, the atomic save through
/// <see cref="AppServices.ApplySettingsAsync"/> and the live changes the main window applies from
/// <see cref="AppServices.SettingsChanged"/>.
/// </summary>
public sealed class SettingsLiveApplyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cutlass-settings-live-" + Guid.NewGuid().ToString("N"));

    /// <summary>Removes the temporary folders.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written.
        }
    }

    [Fact]
    public async Task CtrlCommaOpensTheSettingsDialog()
    {
        var (vm, _, dialogs) = Create();
        Assert.True(vm.Commands.IsRegistered(CommandIds.Settings));
        Assert.True(vm.Keys.HandleKeyDown(Key.OemComma, KeyModifiers.Control, textInputFocused: false));
        await Task.Yield();
        var dialog = Assert.Single(dialogs.SettingsDialogs);
        Assert.Equal((int)SettingsPage.General, dialog.SelectedPageIndex);
    }

    [Fact]
    public void AboutOpensTheAboutPage()
    {
        var (vm, _, dialogs) = Create();
        Assert.True(vm.Commands.TryExecute(CommandIds.About));
        Assert.Equal((int)SettingsPage.About, Assert.Single(dialogs.SettingsDialogs).SelectedPageIndex);
        Assert.True(vm.Commands.IsRegistered(CommandIds.OpenLogFolder));
    }

    [Fact]
    public async Task ApplySavesAtomicallyAndRaisesSettingsChanged()
    {
        var (_, services, _) = Create();
        SettingsChangedEventArgs? raised = null;
        services.SettingsChanged += (_, e) => raised = e;
        var before = services.Settings;
        var next = before with { AutosaveIntervalMinutes = 9, CacheLimitGb = 33 };

        await services.ApplySettingsAsync(next, CancellationToken.None);

        Assert.Same(next, services.Settings);
        Assert.NotNull(raised);
        Assert.Same(before, raised!.OldSettings);
        Assert.Same(next, raised.NewSettings);
        var reloaded = await services.SettingsStore.LoadAsync();
        Assert.Equal(9, reloaded.AutosaveIntervalMinutes);
        Assert.Equal(33, reloaded.CacheLimitGb);
        Assert.False(File.Exists(services.SettingsStore.SettingsFile + ".tmp"));
    }

    [Fact]
    public async Task TheMainWindowAppliesTheChangesLive()
    {
        var (vm, services, _) = Create();
        var next = services.Settings with
        {
            DefaultPreviewQuality = PreviewQuality.Quarter,
            UseProxies = false,
            DefaultStillSeconds = 2,
            DefaultTitleSeconds = 3,
            DefaultTransitionSeconds = 0.5,
            ConfirmDeleteTracks = false,
        };

        await services.ApplySettingsAsync(next, CancellationToken.None);

        Assert.Equal(PreviewQuality.Quarter, vm.Viewer.Quality);
        Assert.False(vm.Viewer.UseProxies);
        Assert.Equal(2, vm.Timeline.StillDurationSeconds);
        Assert.Equal(3, vm.EffectsTab.TitleLengthSeconds);
        Assert.Equal(0.5, vm.EffectsTab.TransitionLengthSeconds);
        Assert.False(vm.Timeline.ConfirmTrackDeletes);
    }

    [Fact]
    public async Task TheAutosaveIntervalFollowsTheSettingsLive()
    {
        var (vm, services, _) = Create();
        await services.ApplySettingsAsync(services.Settings with { AutosaveIntervalMinutes = 7 }, CancellationToken.None);
        Assert.Equal(TimeSpan.FromMinutes(7), vm.Autosave.Interval);
        Assert.Equal(AutosaveService.IntervalFromMinutes(7), vm.Autosave.Interval);

        // DESIGN 11: an interval of 0 turns autosave off.
        await services.ApplySettingsAsync(services.Settings with { AutosaveIntervalMinutes = 0 }, CancellationToken.None);
        Assert.Equal(TimeSpan.Zero, vm.Autosave.Interval);
        Assert.False(vm.Autosave.IsEnabled);
    }

    [Fact]
    public void TheDialogStartsFromTheSettingsInEffect()
    {
        var (vm, services, _) = Create();
        services.Settings.AutosaveIntervalMinutes = 4;
        var dialog = vm.CreateSettingsDialog(SettingsPage.Media);
        Assert.Equal("4", dialog.AutosaveMinutesText);
        Assert.Equal((int)SettingsPage.Media, dialog.SelectedPageIndex);
        Assert.Equal(services.Paths.CacheRoot, dialog.CacheFolder);
        Assert.Equal("ffmpeg: not found", dialog.AboutFfmpegText);
    }

    [Fact]
    public async Task TheFfmpegCheckRefusesABadLocationWithoutRunningAnything()
    {
        var (_, services, _) = Create();
        var check = await services.CheckFfmpegAsync(Path.Combine(_folder, "no-ffmpeg-here"), CancellationToken.None);
        Assert.False(check.IsUsable);
        Assert.EndsWith("is not an executable file.", check.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFfmpegStatusLineFollowsDesign10() =>
        Assert.Equal("found: N-126308, libavfilter 12", AppServices.FfmpegCheckText("N-126308", 12));

    private (MainWindowViewModel Vm, AppServices Services, FakeDialogService Dialogs) Create()
    {
        var paths = new AppPaths(Path.Combine(_folder, "settings.json"), Path.Combine(_folder, "data"), Path.Combine(_folder, "cache"), OperatingSystem.IsWindows());
        var registry = new ChildProcessRegistry(null);
        var services = new AppServices(paths, registry, new ProcessRunner(registry, null));
        var dialogs = new FakeDialogService();
        return (new MainWindowViewModel(services, dialogs, action => action()), services, dialogs);
    }
}
