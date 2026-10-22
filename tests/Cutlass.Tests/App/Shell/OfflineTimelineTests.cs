using Cutlass.App;
using Cutlass.App.Services;
using Cutlass.App.ViewModels;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;
using Cutlass.Media.Projects;

namespace Cutlass.Tests.App.Shell;

/// <summary>
/// The shell wiring of DESIGN 11 offline media: the timeline asks the media bin which media are offline, redraws when
/// their link states change, and File > Relink media... runs through the command registry.
/// </summary>
public sealed class OfflineTimelineTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cutlass-offline-" + Guid.NewGuid().ToString("N"));

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
    public void TheTimelineFollowsTheMediaBinLinkStates()
    {
        var vm = Create();
        var raised = 0;
        vm.Timeline.MediaLinkStatesChanged += (_, _) => raised++;
        Assert.NotNull(vm.Timeline.IsMediaOffline);
        Assert.False(vm.Timeline.IsMediaOffline!("m1"));

        vm.MediaBin.SetLinkState("m1", MediaLinkState.Offline);
        Assert.True(vm.Timeline.IsMediaOffline("m1"));
        Assert.Equal(1, raised);

        // Changed media are online: only missing files are hatched.
        vm.MediaBin.SetLinkState("m1", MediaLinkState.Changed);
        Assert.False(vm.Timeline.IsMediaOffline("m1"));
        Assert.Equal(2, raised);

        vm.MediaBin.SetLinkStates(new Dictionary<string, MediaLinkState>(StringComparer.Ordinal) { ["m2"] = MediaLinkState.Offline });
        Assert.True(vm.Timeline.IsMediaOffline("m2"));
        Assert.False(vm.Timeline.IsMediaOffline("m1"));
        Assert.Equal(3, raised);
    }

    [Fact]
    public void RelinkMediaIsAMenuCommandWithoutAShortcut()
    {
        var vm = Create();
        Assert.True(vm.Commands.IsRegistered(CommandIds.RelinkMedia));
        Assert.DoesNotContain(CommandIds.RelinkMedia, DefaultKeymap.Build().Values);

        // Without a project there is nothing to relink, so the command does nothing.
        Assert.True(vm.Commands.TryExecute(CommandIds.RelinkMedia));
    }

    [Fact]
    public async Task MediaOfflineOnOpenAreNotTouchedUntilRelinked()
    {
        // A project whose media file is missing when it opens.
        Directory.CreateDirectory(_folder);
        var media = Path.Combine(_folder, "media", "gone.mp4");
        var asset = Cutlass.Tests.TestSupport.TestMedia.Asset("m_gone", media, 4_000_000, video: false, audio: true);
        var project = new Cutlass.Tests.TestSupport.ProjectBuilder().Build() with { Media = [asset] };
        var projectPath = Path.Combine(_folder, "offline.cutlass");
        await new ProjectStore().SaveAsync(project, projectPath);

        var vm = Create();
        Assert.True(await vm.OpenProjectFileAsync(projectPath));
        Assert.Contains("m_gone", vm.UntouchedMediaIds);
        Assert.Empty(vm.UnconfirmedMediaIds);
        Assert.Equal(MediaLinkState.Offline, vm.MediaBin.LinkStateOf("m_gone"));

        // The file appears, but nothing looks at its path: the bin keeps it offline without a file check.
        Directory.CreateDirectory(Path.GetDirectoryName(media)!);
        await File.WriteAllTextAsync(media, "back");
        vm.MediaBin.LoadMissingPosters();
        await Task.Delay(100);
        Assert.Equal(MediaLinkState.Offline, vm.MediaBin.LinkStateOf("m_gone"));

        // Relinking (here: to the same, now present path) releases it.
        vm.ApplyRelink([asset]);
        Assert.DoesNotContain("m_gone", vm.UntouchedMediaIds);
        Assert.Equal(MediaLinkState.Online, vm.MediaBin.LinkStateOf("m_gone"));
    }

    [Fact]
    public async Task ConfirmingALocationScansOnlyTheConfirmedMedia()
    {
        Directory.CreateDirectory(_folder);
        var present = Path.Combine(_folder, "present.wav");
        await File.WriteAllTextAsync(present, "bytes");
        var asset = Cutlass.Tests.TestSupport.TestMedia.Asset("m_p", present, 4_000_000, video: false, audio: true);
        var vm = Create();
        await vm.Welcome.NewProjectCommand.ExecuteAsync(null);
        vm.Session.Replace(new Cutlass.Tests.TestSupport.ProjectBuilder().Build() with { Media = [asset] });

        var scan = await vm.ConfirmMediaLocationsAsync(["m_p"]);
        Assert.NotEqual(MediaLinkState.Offline, scan.State("m_p"));
        Assert.Empty(scan.UnconfirmedIds);
    }

    private MainWindowViewModel Create()
    {
        var paths = new AppPaths(Path.Combine(_folder, "settings.json"), Path.Combine(_folder, "data"), Path.Combine(_folder, "cache"), OperatingSystem.IsWindows());
        var registry = new ChildProcessRegistry(null);
        var services = new AppServices(paths, registry, new ProcessRunner(registry, null));
        return new MainWindowViewModel(services, new FakeDialogService(), action => action());
    }
}
