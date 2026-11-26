using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services.Dialogs;
using Cutlass.Core.Model;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Projects;

namespace Cutlass.App.ViewModels.Dialogs;

/// <summary>
/// The Relink dialog (DESIGN 4.2 and 11): lists the media to relink, with "Locate..." per item and "Search folder..."
/// for all of them. A located file also resolves the other unresolved items found by name in its folder; a folder
/// search looks recursively by name, by name without extension, then by size plus fingerprint. Relink re-probes every
/// chosen file and hands the updated assets to the main window, which replaces them in one undo step (clips are
/// trimmed when a new source is shorter). Media on a remote volume that the open scan did not touch are marked "Not
/// checked" and offer "Confirm location", which looks up their recorded path (MediaRelinker.ScanAsync with their ids);
/// a found file is then relinked like a located one. File work runs off the UI thread.
/// </summary>
public sealed partial class RelinkDialogViewModel : ObservableObject
{
    private readonly MediaRelinker _relinker;
    private readonly IDialogService _dialogs;
    private readonly Action<IReadOnlyList<MediaAsset>> _apply;
    private readonly Func<IReadOnlyCollection<string>, Task<RelinkScan>>? _confirm;
    private string? _lastFolder;

    /// <summary>Creates the dialog state.</summary>
    /// <param name="items">The media to relink with their link states.</param>
    /// <param name="relinker">Searches and re-probes files.</param>
    /// <param name="dialogs">The file and folder pickers.</param>
    /// <param name="apply">Replaces the updated assets in the project (one undo step).</param>
    /// <param name="startFolder">Where the pickers start (the project folder), or null.</param>
    /// <param name="unconfirmedIds">Media the open scan did not touch (a remote volume), or null.</param>
    /// <param name="confirm">Looks up the recorded paths of confirmed media (a scan with their ids), or null.</param>
    public RelinkDialogViewModel(
        IEnumerable<(MediaAsset Asset, MediaLinkState State)> items,
        MediaRelinker relinker,
        IDialogService dialogs,
        Action<IReadOnlyList<MediaAsset>> apply,
        string? startFolder,
        IReadOnlyCollection<string>? unconfirmedIds = null,
        Func<IReadOnlyCollection<string>, Task<RelinkScan>>? confirm = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(relinker);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(apply);
        _relinker = relinker;
        _dialogs = dialogs;
        _apply = apply;
        _confirm = confirm;
        _lastFolder = startFolder;
        foreach (var (asset, state) in items)
        {
            Items.Add(new RelinkItemViewModel(asset, state) { IsUnconfirmed = unconfirmedIds?.Contains(asset.Id) == true });
        }

        StatusText = Summary();
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The rows.</summary>
    public ObservableCollection<RelinkItemViewModel> Items { get; } = [];

    /// <summary>The selected row (the target of "Locate...").</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LocateCommand))]
    public partial RelinkItemViewModel? SelectedItem { get; set; }

    /// <summary>True while a search or the relink runs.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RelinkCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(LocateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmLocationCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>True when some rows are on a network location the open scan did not check (the hint line shows).</summary>
    public bool HasUnconfirmed => Items.Any(i => i.IsUnconfirmed);

    /// <summary>The summary line, for example "2 of 3 files found".</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; }

    /// <summary>True once the relink was applied.</summary>
    public bool Applied { get; private set; }

    /// <summary>The assets the relink replaced (empty until applied).</summary>
    public IReadOnlyList<MediaAsset> AppliedAssets { get; private set; } = [];

    /// <summary>Resolves an item to a file, then resolves the other unresolved items found by name in its folder.</summary>
    /// <param name="item">The row.</param>
    /// <param name="path">The chosen file.</param>
    /// <returns>A task that completes when the folder was looked at.</returns>
    public async Task LocateFileAsync(RelinkItemViewModel item, string path)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        item.NewPath = Path.GetFullPath(path);
        item.Error = null;
        var folder = Path.GetDirectoryName(item.NewPath);
        _lastFolder = folder;
        if (folder is not null)
        {
            var others = Unresolved();
            if (others.Count > 0)
            {
                var assets = others.Select(o => o.Asset).ToList();
                var located = item.NewPath;
                var matches = await Task.Run(() => MediaRelinker.MatchByName(
                    assets,
                    [.. SafeFiles(folder).Where(f => !string.Equals(f, located, StringComparison.Ordinal))],
                    f => MediaFileFacts.Of(f)?.SizeBytes,
                    OperatingSystem.IsWindows()));
                Resolve(matches);
            }
        }

        UpdateState();
    }

    /// <summary>
    /// "Confirm location" (DESIGN 11): looks up the recorded paths of unconfirmed rows. A found file (at its path or
    /// relative to the project) becomes the row's new path, to be applied with Relink; a missing one says so.
    /// </summary>
    /// <param name="items">The rows to confirm.</param>
    /// <returns>The number of files found.</returns>
    public async Task<int> ConfirmLocationsAsync(IReadOnlyList<RelinkItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var rows = items.Where(i => i.IsUnconfirmed).ToList();
        if (rows.Count == 0 || _confirm is null)
        {
            return 0;
        }

        IsBusy = true;
        StatusText = "Looking up the confirmed locations...";
        var found = 0;
        try
        {
            var scan = await _confirm([.. rows.Select(r => r.Id)]);
            foreach (var row in rows)
            {
                row.IsUnconfirmed = false;
                if (scan.State(row.Id) != MediaLinkState.Offline
                    && scan.Project.Media.FirstOrDefault(m => m.Id == row.Id) is { } asset)
                {
                    row.NewPath = asset.Path;
                    row.Error = null;
                    found++;
                }
                else
                {
                    row.IsMissingAfterConfirm = true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "The location could not be checked: " + ex.Message;
            return 0;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasUnconfirmed));
            ConfirmLocationCommand.NotifyCanExecuteChanged();
        }

        UpdateState();
        return found;
    }

    /// <summary>Searches a folder tree for every unresolved item.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The number of items found.</returns>
    public async Task<int> SearchInFolderAsync(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var wanted = Unresolved();
        if (wanted.Count == 0)
        {
            return 0;
        }

        _lastFolder = folder;
        IsBusy = true;
        StatusText = "Searching " + folder + "...";
        try
        {
            var matches = await _relinker.SearchFolderAsync([.. wanted.Select(w => w.Asset)], folder);
            return Resolve(matches);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = "The folder could not be searched: " + ex.Message;
            return 0;
        }
        finally
        {
            IsBusy = false;
            UpdateState();
        }
    }

    /// <summary>
    /// Re-probes every resolved item and applies them. Items that fail keep their row with the reason; the dialog
    /// closes when everything chosen was applied.
    /// </summary>
    /// <returns>The updated assets that were applied.</returns>
    public async Task<IReadOnlyList<MediaAsset>> ApplyResolvedAsync()
    {
        var resolved = Items.Where(i => i.NewPath is not null).ToList();
        if (resolved.Count == 0)
        {
            return [];
        }

        IsBusy = true;
        StatusText = "Reading the new files...";
        var updated = new List<MediaAsset>();
        try
        {
            foreach (var item in resolved)
            {
                try
                {
                    updated.Add(await _relinker.RefreshAsync(item.Asset, item.NewPath!));
                    item.Error = null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FfmpegException)
                {
                    item.Error = "Cannot use this file: " + ex.Message;
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (updated.Count > 0)
        {
            _apply(updated);
            Applied = true;
            AppliedAssets = updated;
            foreach (var item in resolved.Where(i => !i.HasError).ToList())
            {
                Items.Remove(item);
            }
        }

        UpdateState();
        if (!Items.Any(i => i.HasError))
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        return updated;
    }

    private static IReadOnlyList<string> SafeFiles(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder)] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private List<RelinkItemViewModel> Unresolved() => [.. Items.Where(i => i.NewPath is null)];

    private int Resolve(IReadOnlyDictionary<string, string> matches)
    {
        var count = 0;
        foreach (var item in Items.Where(i => i.NewPath is null))
        {
            if (matches.TryGetValue(item.Id, out var path))
            {
                item.NewPath = path;
                item.Error = null;
                count++;
            }
        }

        return count;
    }

    private string Summary()
    {
        var found = Items.Count(i => i.NewPath is not null);
        return Items.Count == 0
            ? "Every media file is linked."
            : string.Create(CultureInfo.InvariantCulture, $"{found} of {Items.Count} files found");
    }

    private void UpdateState()
    {
        StatusText = Summary();
        RelinkCommand.NotifyCanExecuteChanged();
    }

    private bool CanRelink() => !IsBusy && Items.Any(i => i.NewPath is not null);

    private bool CanSearch() => !IsBusy;

    private bool CanLocate(RelinkItemViewModel? item) => !IsBusy && (item ?? SelectedItem) is not null;

    [RelayCommand(CanExecute = nameof(CanLocate))]
    private async Task LocateAsync(RelinkItemViewModel? item)
    {
        if ((item ?? SelectedItem) is not { } target)
        {
            return;
        }

        if (await _dialogs.PickRelinkFileAsync(target.Name, _lastFolder) is { } path)
        {
            await LocateFileAsync(target, path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchFolderAsync()
    {
        if (await _dialogs.PickFolderAsync("Search folder for missing media", _lastFolder) is { } folder)
        {
            var found = await SearchInFolderAsync(folder);
            if (found == 0)
            {
                StatusText = "No missing file was found in " + folder + ". " + Summary();
            }
        }
    }

    private bool CanConfirm(RelinkItemViewModel? item) => !IsBusy && _confirm is not null && (item ?? SelectedItem) is { IsUnconfirmed: true };

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private Task ConfirmLocationAsync(RelinkItemViewModel? item) =>
        (item ?? SelectedItem) is { } target ? ConfirmLocationsAsync([target]) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRelink))]
    private Task RelinkAsync() => ApplyResolvedAsync();

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
