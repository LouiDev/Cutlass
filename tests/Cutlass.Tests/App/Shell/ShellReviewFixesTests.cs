using Avalonia.Input;
using Cutlass.App;
using Cutlass.App.Services;
using Cutlass.App.ViewModels;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;
using Cutlass.Media.Services;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App.Shell;

/// <summary>
/// Shell fixes from the UI review: X (mark clip) is one undo step, an import never lands in a project that replaced
/// the one it started in, and the autosave after a non-terminating failure is tracked like a timed one.
/// </summary>
public sealed class ShellReviewFixesTests : IDisposable
{
    private static readonly MediaAsset LongAsset = All.First(a => a.Id == Long);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cutlass-shellfix-" + Guid.NewGuid().ToString("N"));

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
    public async Task MarkClipSetsInAndOutAsOneUndoStepWithoutAWarning()
    {
        var vm = Create();
        await vm.Welcome.NewProjectCommand.ExecuteAsync(null);
        vm.Session.Replace(new ProjectBuilder().With(ProjectBuilder.V1, V("c1", 100, 50)).InOut(0, 50).Build());
        vm.Timeline.Playhead = 120;
        var warnings = new List<string>();
        vm.Timeline.WarningRaised += (_, w) => warnings.Add(w);
        var steps = vm.Session.UndoCount;

        // X through the key binding service, as the window routes it.
        Assert.True(vm.Keys.HandleKeyDown(Key.X, KeyModifiers.None, textInputFocused: false));

        Assert.Equal((100L, 150L), (vm.Session.Current.Sequence.InFrame, vm.Session.Current.Sequence.OutFrame));
        Assert.Equal((100L, 150L), (vm.Timeline.InPoint, vm.Timeline.OutPoint));
        Assert.Empty(warnings);
        Assert.Equal(steps + 1, vm.Session.UndoCount);

        // One undo restores the old range (it used to leave In=100/Out=101).
        Assert.True(vm.Session.Undo());
        Assert.Equal((0L, 50L), (vm.Session.Current.Sequence.InFrame, vm.Session.Current.Sequence.OutFrame));
    }

    [Fact]
    public async Task AnImportWithoutAReplacementAddsTheMedia()
    {
        var vm = Create();
        await vm.Welcome.NewProjectCommand.ExecuteAsync(null);
        vm.MediaBin.ImportFile = (path, _) => Task.FromResult(new MediaImportResult(LongAsset with { Path = path }, null, null));

        var imported = await vm.MediaBin.ImportPathsAsync([Path.Combine(_folder, "long.mp4")]);

        Assert.Single(imported);
        Assert.Single(vm.Session.Current.Media);
        Assert.False(vm.MediaBin.IsImporting);
    }

    [Fact]
    public async Task ARunningImportStopsWhenAnotherProjectBecomesCurrent()
    {
        var vm = Create();
        await vm.Welcome.NewProjectCommand.ExecuteAsync(null);
        var importer = new GatedImporter();
        vm.MediaBin.ImportFile = importer.ImportAsync;

        var import = vm.MediaBin.ImportPathsAsync([Path.Combine(_folder, "long.mp4"), Path.Combine(_folder, "short.mov")]);
        await importer.Called.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vm.MediaBin.IsImporting);
        Assert.Equal(2, vm.MediaBin.Items.Count);

        // New project (the same StartProject path as Open and Recover) while the first file is probed.
        await vm.Welcome.NewProjectCommand.ExecuteAsync(null);
        Assert.True(importer.Token.IsCancellationRequested);
        Assert.False(vm.MediaBin.IsImporting);
        Assert.Empty(vm.MediaBin.Items);

        importer.Gate.SetResult(new MediaImportResult(LongAsset, null, null));
        var imported = await import.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(imported);
        Assert.Empty(vm.Session.Current.Media);
        Assert.Equal(1, importer.Calls);
        Assert.False(vm.Session.IsDirty);
    }

    [Fact]
    public async Task ATimelineFileDropPlacesNothingInAProjectThatReplacedItsOwn()
    {
        var vm = Create();
        await vm.Welcome.NewProjectCommand.ExecuteAsync(null);
        var importer = new GatedImporter();
        vm.MediaBin.ImportFile = importer.ImportAsync;
        var v1 = vm.Session.Current.Sequence.Tracks.First(t => t.Kind == TrackKind.Video).Id;

        var drop = vm.Timeline.DropFilesAsync([Path.Combine(_folder, "long.mp4")], v1, 0, insert: false);
        await importer.Called.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var generation = vm.Timeline.ProjectGeneration;
        await vm.Welcome.NewProjectCommand.ExecuteAsync(null);
        Assert.Equal(generation + 1, vm.Timeline.ProjectGeneration);

        importer.Gate.SetResult(new MediaImportResult(LongAsset, null, null));
        await drop.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(vm.Session.Current.Media);
        Assert.All(vm.Session.Current.Sequence.Tracks, t => Assert.Empty(t.Clips));
    }

    [Fact]
    public async Task AWelcomeDropStopsWhenAnotherProjectBecomesCurrent()
    {
        var vm = Create();
        var importer = new GatedImporter();
        vm.MediaBin.ImportFile = importer.ImportAsync;

        var drop = vm.Welcome.DropMediaAsync([Path.Combine(_folder, "long.mp4")]);
        await importer.Called.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vm.HasProject);
        await vm.Welcome.NewProjectCommand.ExecuteAsync(null);

        importer.Gate.SetResult(new MediaImportResult(LongAsset, null, null));
        await drop.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(vm.Session.Current.Media);
        Assert.All(vm.Session.Current.Sequence.Tracks, t => Assert.Empty(t.Clips));
    }

    [Fact]
    public async Task TheAutosaveAfterAnUnobservedExceptionIsDeletedByACleanClose()
    {
        var vm = Create();
        await vm.Welcome.NewProjectCommand.ExecuteAsync(null);
        vm.Session.Execute(p => TimelineEdits.SetInOut(p, 10, 20));
        var target = vm.Autosave.TargetPath;

        // Not terminating: the normal, tracked autosave path (posted to the UI thread, here inline).
        vm.HandleCrash("Unobserved task exception", new InvalidOperationException("test"), TimeSpan.FromSeconds(5), terminating: false);
        await WaitUntilAsync(() => vm.Autosave.WriteCount == 1);
        Assert.True(File.Exists(target));

        // "Don't save" and quit: the clean close deletes what this session wrote.
        await vm.Autosave.ClearAsync(includeProjectAutosave: false);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void TheStatusBarAudioBadgeFollowsADeviceLostDuringPlayback()
    {
        var vm = Create();
        vm.Session.Replace(new ProjectBuilder().With(ProjectBuilder.V1, V("c1", 0, 50)).Build());
        var engine = new Cutlass.Tests.App.Playback.FakePlaybackEngine();
        vm.Viewer.AttachPlayback(engine, null);
        Assert.False(vm.IsAudioUnavailable);

        Assert.True(vm.Viewer.Playback.TogglePlay());
        engine.HasAudio = false;
        engine.RaiseAudioUnavailable();
        Assert.True(vm.IsAudioUnavailable);
        Assert.Contains("lost", vm.AudioToolTip, StringComparison.Ordinal);

        // The next play start reopens the device and clears the badge.
        vm.Viewer.Playback.Stop();
        engine.HasAudio = true;
        Assert.True(vm.Viewer.Playback.TogglePlay());
        Assert.False(vm.IsAudioUnavailable);
        vm.Viewer.Playback.Stop();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private MainWindowViewModel Create()
    {
        var paths = new AppPaths(Path.Combine(_folder, "settings.json"), Path.Combine(_folder, "data"), Path.Combine(_folder, "cache"), OperatingSystem.IsWindows());
        var registry = new ChildProcessRegistry(null);
        var services = new AppServices(paths, registry, new ProcessRunner(registry, null));
        return new MainWindowViewModel(services, new FakeDialogService(), action => action());
    }

    // An importer whose first file waits until the test releases it.
    private sealed class GatedImporter
    {
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<MediaImportResult> Gate { get; } = new();

        public CancellationToken Token { get; private set; }

        public int Calls { get; private set; }

        public Task<MediaImportResult> ImportAsync(string path, CancellationToken token)
        {
            Calls++;
            Token = token;
            Called.TrySetResult();
            return Gate.Task;
        }
    }
}
