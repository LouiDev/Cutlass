using Cutlass.App.ViewModels;

namespace Cutlass.App.Services;

/// <summary>
/// Everything view models ask the user through a window: file pickers (TECH_BRIEF 5.6), confirmations, the error
/// and export dialogs, the clipboard and the file manager. View models depend on this interface only, so they never
/// touch controls and stay testable.
/// </summary>
public interface IDialogService
{
    /// <summary>Asks for media files to import (multi-select).</summary>
    /// <returns>The local paths, empty when cancelled.</returns>
    Task<IReadOnlyList<string>> PickMediaFilesAsync();

    /// <summary>Asks for a project to open.</summary>
    /// <returns>The local path, or null when cancelled.</returns>
    Task<string?> PickProjectToOpenAsync();

    /// <summary>Asks where to save a project.</summary>
    /// <param name="suggestedName">The suggested file name without extension.</param>
    /// <param name="folder">The folder to start in, or null.</param>
    /// <returns>The local path, or null when cancelled.</returns>
    Task<string?> PickProjectSavePathAsync(string suggestedName, string? folder);

    /// <summary>Asks where to write an export.</summary>
    /// <param name="suggestedPath">The current output path (folder and name are suggested from it).</param>
    /// <param name="extension">The preset extension including the dot, for example <c>.mp4</c>.</param>
    /// <param name="formatName">The format shown in the type filter, for example <c>H.264</c>.</param>
    /// <returns>The local path, or null when cancelled.</returns>
    Task<string?> PickExportPathAsync(string suggestedPath, string extension, string formatName);

    /// <summary>Asks for the ffmpeg executable ("Locate ffmpeg...").</summary>
    /// <returns>The local path, or null when cancelled.</returns>
    Task<string?> PickFfmpegAsync();

    /// <summary>Asks whether to save a dirty project.</summary>
    /// <param name="projectName">The project name.</param>
    /// <returns>The choice.</returns>
    Task<SaveChangesChoice> AskSaveChangesAsync(string projectName);

    /// <summary>Asks for confirmation.</summary>
    /// <param name="title">The window title.</param>
    /// <param name="message">The question.</param>
    /// <param name="confirmLabel">The label of the confirming button, for example "Remove".</param>
    /// <returns>True when confirmed.</returns>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel);

    /// <summary>Shows a message with an OK button.</summary>
    /// <param name="title">The window title.</param>
    /// <param name="message">The message.</param>
    /// <returns>A task that completes when the dialog closes.</returns>
    Task ShowMessageAsync(string title, string message);

    /// <summary>Shows the error dialog (summary, ffmpeg log tail, Copy details).</summary>
    /// <param name="error">The error.</param>
    /// <returns>A task that completes when the dialog closes.</returns>
    Task ShowErrorAsync(ErrorDialogViewModel error);

    /// <summary>Shows the export dialog.</summary>
    /// <param name="dialog">The dialog view model.</param>
    /// <returns>A task that completes when the dialog closes.</returns>
    Task ShowExportDialogAsync(ExportDialogViewModel dialog);

    /// <summary>Shows the speed dialog (Ctrl+R, DESIGN 5.5).</summary>
    /// <param name="dialog">The dialog state.</param>
    /// <returns>True when the user applied it.</returns>
    Task<bool> ShowSpeedDialogAsync(SpeedDialogViewModel dialog);

    /// <summary>Shows the sequence settings dialog (DESIGN 4.4).</summary>
    /// <param name="dialog">The dialog state.</param>
    /// <returns>True when the user applied it.</returns>
    Task<bool> ShowSequenceSettingsDialogAsync(SequenceSettingsDialogViewModel dialog);

    /// <summary>Shows the Settings dialog (Ctrl+, DESIGN 10).</summary>
    /// <param name="dialog">The dialog state.</param>
    /// <returns>True when the user pressed OK (the settings were applied).</returns>
    Task<bool> ShowSettingsDialogAsync(SettingsViewModel dialog);

    /// <summary>Asks for a folder.</summary>
    /// <param name="title">The picker title.</param>
    /// <param name="startFolder">The folder to start in, or null.</param>
    /// <returns>The local path, or null when cancelled.</returns>
    Task<string?> PickFolderAsync(string title, string? startFolder);

    /// <summary>Puts text on the clipboard.</summary>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the text is on the clipboard.</returns>
    Task CopyToClipboardAsync(string text);

    /// <summary>Opens the folder that holds a file (or the folder itself) in the file manager.</summary>
    /// <param name="path">A file or folder path.</param>
    /// <returns>A task that completes when the request was handed to the OS.</returns>
    Task RevealInFileManagerAsync(string path);

    /// <summary>
    /// Asks whether to recover an autosave that is newer than its project (DESIGN 11): Recover, Discard or decide later.
    /// The default answers <see cref="RecoveryChoice.Ignore"/>, so a test double needs no change.
    /// </summary>
    /// <param name="projectName">The project name ("Untitled" for an untitled project).</param>
    /// <param name="savedText">When the autosave was written, for display.</param>
    /// <returns>The choice.</returns>
    Task<RecoveryChoice> AskRecoveryAsync(string projectName, string savedText) => Task.FromResult(RecoveryChoice.Ignore);

    /// <summary>Shows the Relink dialog (DESIGN 4.2 and 11). The default does nothing.</summary>
    /// <param name="dialog">The dialog state.</param>
    /// <returns>A task that completes when the dialog closes.</returns>
    Task ShowRelinkDialogAsync(RelinkDialogViewModel dialog) => Task.CompletedTask;

    /// <summary>Asks for the new location of one media file ("Locate..." in the Relink dialog). The default cancels.</summary>
    /// <param name="fileName">The missing file's name (shown in the title).</param>
    /// <param name="startFolder">The folder to start in, or null.</param>
    /// <returns>The local path, or null when cancelled.</returns>
    Task<string?> PickRelinkFileAsync(string fileName, string? startFolder) => Task.FromResult<string?>(null);

    /// <summary>
    /// Opens a file with the system's default application ("Open agent docs", AGENT_INTEGRATION 6.1). The default
    /// reveals it in the file manager.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>A task that completes when the request was handed to the OS.</returns>
    Task OpenFileAsync(string path) => RevealInFileManagerAsync(path);
}
