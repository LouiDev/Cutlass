using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services;
using Cutlass.Media.Projects;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The welcome overlay shown while no project is open (DESIGN 4.1): New, Open, up to ten recent projects (missing
/// ones greyed out with "Remove from list"), a "Drop media here" target and the ffmpeg status with the install hint
/// and "Locate ffmpeg..." when it is missing. Autosaves left by a session that did not close cleanly (DESIGN 11)
/// are listed above the recent projects with Recover and Discard. The drop target also has "Import media...", which
/// is the way in on Linux, where OS file drops do not arrive (<see cref="PlatformFeatures.OsFileDrops"/>).
/// </summary>
public sealed partial class WelcomeViewModel : ObservableObject
{
    private readonly Func<Task> _newProject;
    private readonly Func<Task> _openProject;
    private readonly Func<string, Task> _openRecent;
    private readonly Func<string, Task> _removeRecent;
    private readonly Func<Task> _locateFfmpeg;
    private readonly Func<IReadOnlyList<string>, Task> _dropMedia;

    /// <summary>Creates the overlay state; the actions are the main window's.</summary>
    /// <param name="newProject">Creates an untitled project.</param>
    /// <param name="openProject">Opens a project through the file dialog.</param>
    /// <param name="openRecent">Opens a recent project.</param>
    /// <param name="removeRecent">Removes an entry from the recent list.</param>
    /// <param name="locateFfmpeg">Lets the user pick ffmpeg.</param>
    /// <param name="dropMedia">Creates an untitled project, imports the files and places them end to end.</param>
    public WelcomeViewModel(
        Func<Task> newProject,
        Func<Task> openProject,
        Func<string, Task> openRecent,
        Func<string, Task> removeRecent,
        Func<Task> locateFfmpeg,
        Func<IReadOnlyList<string>, Task> dropMedia)
    {
        _newProject = newProject;
        _openProject = openProject;
        _openRecent = openRecent;
        _removeRecent = removeRecent;
        _locateFfmpeg = locateFfmpeg;
        _dropMedia = dropMedia;
        FfmpegStatusText = "Looking for ffmpeg...";
    }

    /// <summary>The recent projects, newest first.</summary>
    public ObservableCollection<RecentProjectItem> Recent { get; } = [];

    /// <summary>The ffmpeg status line.</summary>
    [ObservableProperty]
    public partial string FfmpegStatusText { get; set; }

    /// <summary>True when ffmpeg was found and works.</summary>
    [ObservableProperty]
    public partial bool IsFfmpegOk { get; set; }

    /// <summary>True when ffmpeg is missing or unusable (the install hint shows).</summary>
    [ObservableProperty]
    public partial bool IsFfmpegMissing { get; set; }

    /// <summary>True when the recent list has entries.</summary>
    public bool HasRecent => Recent.Count > 0;

    /// <summary>Recoverable autosaves (untitled projects and projects with a newer autosave), newest first.</summary>
    public ObservableCollection<RecoverableProject> Recoverable { get; } = [];

    /// <summary>True when there is something to recover.</summary>
    public bool HasRecoverable => Recoverable.Count > 0;

    /// <summary>The large target's title: "Drop media here", or "Import media" where OS file drops do not work.</summary>
    public static string DropTitle => PlatformFeatures.OsFileDrops ? "Drop media here" : "Import media";

    /// <summary>The line under <see cref="DropTitle"/>.</summary>
    public static string DropHint => PlatformFeatures.OsFileDrops
        ? "Creates an untitled project and places the clips end to end."
        : "Creates an untitled project and places the clips end to end. Dropping files from the file manager does not work on Linux yet.";

    /// <summary>The subtitle under the app name.</summary>
    public static string Subtitle => PlatformFeatures.OsFileDrops
        ? "Open a project or drop media to start cutting."
        : "Open a project or import media to start cutting.";

    /// <summary>
    /// "Import media..." in the drop target (set by the main window): picks files, then works like a drop (an untitled
    /// project with the media end to end on V1/A1).
    /// </summary>
    public Func<Task>? ImportMediaRequested { get; set; }

    /// <summary>Opens a recoverable autosave (set by the main window).</summary>
    public Func<RecoverableProject, Task>? RecoverRequested { get; set; }

    /// <summary>Deletes a recoverable autosave (set by the main window).</summary>
    public Func<RecoverableProject, Task>? DiscardRecoveryRequested { get; set; }

    /// <summary>Replaces the recoverable list.</summary>
    /// <param name="items">The autosaves, newest first.</param>
    public void SetRecoverable(IEnumerable<RecoverableProject> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Recoverable.Clear();
        foreach (var item in items)
        {
            Recoverable.Add(item);
        }

        OnPropertyChanged(nameof(HasRecoverable));
    }

    /// <summary>The install hint for this OS (DESIGN 10, first-run help).</summary>
    public static string InstallHint => OperatingSystem.IsWindows()
        ? "Install it with: winget install Gyan.FFmpeg"
        : "Install it with: sudo apt install ffmpeg (Debian/Ubuntu) or sudo dnf install ffmpeg (Fedora with RPM Fusion)";

    /// <summary>Replaces the recent list.</summary>
    /// <param name="items">The entries.</param>
    public void SetRecent(IEnumerable<RecentProjectItem> items)
    {
        Recent.Clear();
        foreach (var item in items)
        {
            Recent.Add(item);
        }

        OnPropertyChanged(nameof(HasRecent));
    }

    /// <summary>Handles files dropped on the overlay.</summary>
    /// <param name="paths">The dropped paths.</param>
    /// <returns>A task that completes when the import finished.</returns>
    public Task DropMediaAsync(IReadOnlyList<string> paths) => paths.Count == 0 ? Task.CompletedTask : _dropMedia(paths);

    [RelayCommand]
    private Task NewProjectAsync() => _newProject();

    [RelayCommand]
    private Task OpenProjectAsync() => _openProject();

    [RelayCommand]
    private Task OpenRecentAsync(RecentProjectItem? item) => item is { Exists: true } ? _openRecent(item.Path) : Task.CompletedTask;

    [RelayCommand]
    private Task RemoveRecentAsync(RecentProjectItem? item) => item is null ? Task.CompletedTask : _removeRecent(item.Path);

    [RelayCommand]
    private Task LocateFfmpegAsync() => _locateFfmpeg();

    [RelayCommand]
    private Task ImportMediaAsync() => ImportMediaRequested is { } import ? import() : Task.CompletedTask;

    [RelayCommand]
    private Task RecoverAsync(RecoverableProject? item) =>
        item is not null && RecoverRequested is { } recover ? recover(item) : Task.CompletedTask;

    [RelayCommand]
    private Task DiscardRecoveryAsync(RecoverableProject? item) =>
        item is not null && DiscardRecoveryRequested is { } discard ? discard(item) : Task.CompletedTask;
}
