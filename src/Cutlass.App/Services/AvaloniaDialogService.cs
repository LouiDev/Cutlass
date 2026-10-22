using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Cutlass.App.ViewModels;
using Cutlass.App.Views;

namespace Cutlass.App.Services;

/// <summary>
/// The Avalonia implementation of <see cref="IDialogService"/> (TECH_BRIEF 5.6): <c>StorageProvider</c> pickers with
/// <c>TryGetLocalPath()</c> (a null local path shows "Only local files are supported"), modal message boxes, the
/// error and export dialogs, the clipboard and the launcher.
/// </summary>
public sealed class AvaloniaDialogService : IDialogService
{
    private readonly Func<Window?> _owner;

    /// <summary>Creates the service.</summary>
    /// <param name="owner">Returns the window dialogs are owned by (the main window).</param>
    public AvaloniaDialogService(Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PickMediaFilesAsync()
    {
        if (_owner() is not { } owner)
        {
            return [];
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import media",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Media files") { Patterns = [.. MediaBinViewModel.SupportedExtensions.Order().Select(e => "*" + e)] },
                FilePickerFileTypes.All,
            ],
        });
        return await LocalPathsAsync(files);
    }

    /// <inheritdoc />
    public async Task<string?> PickProjectToOpenAsync()
    {
        if (_owner() is not { } owner)
        {
            return null;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open project",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Cutlass project") { Patterns = ["*.cutlass"] }, FilePickerFileTypes.All],
        });
        return (await LocalPathsAsync(files)).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<string?> PickProjectSavePathAsync(string suggestedName, string? folder)
    {
        if (_owner() is not { } owner)
        {
            return null;
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save project",
            SuggestedFileName = suggestedName + ".cutlass",
            DefaultExtension = "cutlass",
            ShowOverwritePrompt = true,
            SuggestedStartLocation = folder is null ? null : await owner.StorageProvider.TryGetFolderFromPathAsync(folder),
            FileTypeChoices = [new FilePickerFileType("Cutlass project") { Patterns = ["*.cutlass"] }],
        });
        return await LocalPathAsync(file);
    }

    /// <inheritdoc />
    public async Task<string?> PickExportPathAsync(string suggestedPath, string extension, string formatName)
    {
        if (_owner() is not { } owner)
        {
            return null;
        }

        var folder = string.IsNullOrEmpty(suggestedPath) ? null : Path.GetDirectoryName(suggestedPath);
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to",
            SuggestedFileName = Path.GetFileName(suggestedPath),
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = false,
            SuggestedStartLocation = string.IsNullOrEmpty(folder) ? null : await owner.StorageProvider.TryGetFolderFromPathAsync(folder),
            FileTypeChoices = [new FilePickerFileType(formatName) { Patterns = ["*" + extension] }],
        });
        return await LocalPathAsync(file);
    }

    /// <inheritdoc />
    public async Task<string?> PickFfmpegAsync()
    {
        if (_owner() is not { } owner)
        {
            return null;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Locate ffmpeg",
            AllowMultiple = false,
            FileTypeFilter = OperatingSystem.IsWindows()
                ? [new FilePickerFileType("ffmpeg") { Patterns = ["ffmpeg.exe"] }, FilePickerFileTypes.All]
                : [FilePickerFileTypes.All],
        });
        return (await LocalPathsAsync(files)).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<SaveChangesChoice> AskSaveChangesAsync(string projectName)
    {
        if (_owner() is not { } owner)
        {
            return SaveChangesChoice.Cancel;
        }

        var choice = await new MessageDialog(
            "Unsaved changes",
            $"Save the changes to {projectName}?",
            ["Save", "Don't save", "Cancel"],
            cancelIndex: 2).ShowAsync(owner);
        return choice switch
        {
            0 => SaveChangesChoice.Save,
            1 => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel,
        };
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        if (_owner() is not { } owner)
        {
            return false;
        }

        return await new MessageDialog(title, message, [confirmLabel, "Cancel"], cancelIndex: 1).ShowAsync(owner) == 0;
    }

    /// <inheritdoc />
    public async Task ShowMessageAsync(string title, string message)
    {
        if (_owner() is { } owner)
        {
            await new MessageDialog(title, message, ["OK"], cancelIndex: 0).ShowAsync(owner);
        }
    }

    /// <inheritdoc />
    public async Task ShowErrorAsync(ErrorDialogViewModel error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (_owner() is not { } owner)
        {
            return;
        }

        var dialog = new ErrorDialog { DataContext = error };
        void OnClose(object? sender, EventArgs e) => dialog.Close();
        error.CloseRequested += OnClose;
        try
        {
            await dialog.ShowDialog(owner);
        }
        finally
        {
            error.CloseRequested -= OnClose;
        }
    }

    /// <inheritdoc />
    public Task<bool> ShowSpeedDialogAsync(SpeedDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return _owner() is { } owner ? SpeedDialog.ShowAsync(owner, dialog) : Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> ShowSequenceSettingsDialogAsync(SequenceSettingsDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return _owner() is { } owner ? SequenceSettingsDialog.ShowAsync(owner, dialog) : Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> ShowMcpStartDialogAsync(McpStartDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return _owner() is { } owner ? McpStartDialog.ShowAsync(owner, dialog) : Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task ShowConnectClientsDialogAsync(ConnectClientsViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return _owner() is { } owner ? ConnectClientsDialog.ShowAsync(owner, dialog) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ShowSettingsDialogAsync(SettingsViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return _owner() is { } owner ? SettingsDialog.ShowAsync(owner, dialog) : Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string title, string? startFolder)
    {
        if (_owner() is not { } owner)
        {
            return null;
        }

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = string.IsNullOrEmpty(startFolder) ? null : await owner.StorageProvider.TryGetFolderFromPathAsync(startFolder),
        });
        if (folders.Count == 0)
        {
            return null;
        }

        var path = folders[0].TryGetLocalPath();
        if (path is null)
        {
            await ShowMessageAsync("Cutlass", "Only local folders are supported.");
        }

        return path;
    }

    /// <inheritdoc />
    public async Task ShowExportDialogAsync(ExportDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (_owner() is not { } owner)
        {
            return;
        }

        var window = new ExportDialog { DataContext = dialog };
        void OnClose(object? sender, bool queued) => window.Close();
        dialog.CloseRequested += OnClose;
        try
        {
            await window.ShowDialog(owner);
        }
        finally
        {
            dialog.CloseRequested -= OnClose;
        }
    }

    /// <inheritdoc />
    public async Task CopyToClipboardAsync(string text)
    {
        if (_owner()?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <inheritdoc />
    public async Task RevealInFileManagerAsync(string path)
    {
        if (_owner() is not { } owner || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            // The launcher hands the folder to the OS; no Process is started here (the repository's process rule).
            await owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
        }
    }

    /// <inheritdoc />
    public async Task OpenFileAsync(string path)
    {
        if (_owner() is not { } owner || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!File.Exists(path))
        {
            await ShowMessageAsync("Cutlass", "The file " + path + " was not found.");
            return;
        }

        // The launcher hands the file to the OS; no Process is started here (the repository's process rule).
        if (!await owner.Launcher.LaunchFileInfoAsync(new FileInfo(path)))
        {
            await RevealInFileManagerAsync(path);
        }
    }

    /// <inheritdoc />
    public async Task<RecoveryChoice> AskRecoveryAsync(string projectName, string savedText)
    {
        if (_owner() is not { } owner)
        {
            return RecoveryChoice.Ignore;
        }

        var choice = await new MessageDialog(
            "Recover unsaved changes",
            $"Cutlass did not close cleanly. An autosave of {projectName} from {savedText} has changes that were not saved.\n\n" +
            "Recover opens the autosaved state; Discard deletes it.",
            ["Recover", "Discard", "Not now"],
            cancelIndex: 2).ShowAsync(owner);
        return choice switch
        {
            0 => RecoveryChoice.Recover,
            1 => RecoveryChoice.Discard,
            _ => RecoveryChoice.Ignore,
        };
    }

    /// <inheritdoc />
    public Task ShowRelinkDialogAsync(RelinkDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return _owner() is { } owner ? RelinkDialog.ShowAsync(owner, dialog) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<string?> PickRelinkFileAsync(string fileName, string? startFolder)
    {
        if (_owner() is not { } owner)
        {
            return null;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Locate " + fileName,
            AllowMultiple = false,
            SuggestedFileName = fileName,
            SuggestedStartLocation = string.IsNullOrEmpty(startFolder) || !Directory.Exists(startFolder)
                ? null
                : await owner.StorageProvider.TryGetFolderFromPathAsync(startFolder),
            FileTypeFilter =
            [
                new FilePickerFileType("Media files") { Patterns = [.. MediaBinViewModel.SupportedExtensions.Order().Select(e => "*" + e)] },
                FilePickerFileTypes.All,
            ],
        });
        return (await LocalPathsAsync(files)).FirstOrDefault();
    }

    private async Task<string?> LocalPathAsync(IStorageFile? file)
    {
        if (file is null)
        {
            return null;
        }

        var path = file.TryGetLocalPath();
        if (path is null)
        {
            await ShowMessageAsync("Cutlass", "Only local files are supported.");
        }

        return path;
    }

    private async Task<IReadOnlyList<string>> LocalPathsAsync(IReadOnlyList<IStorageFile> files)
    {
        var paths = files.Select(f => f.TryGetLocalPath()).ToList();
        if (paths.Any(p => p is null))
        {
            await ShowMessageAsync("Cutlass", "Only local files are supported.");
        }

        return [.. paths.Where(p => p is not null).Select(p => p!)];
    }
}
