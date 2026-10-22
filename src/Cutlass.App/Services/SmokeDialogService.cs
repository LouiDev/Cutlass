using Cutlass.App.ViewModels;

namespace Cutlass.App.Services;

/// <summary>
/// The dialog service of a smoke run: nothing waits for a person. Pickers return nothing, confirmations say yes,
/// the save prompt discards, and every message or error is logged and counted so the runner can fail the step that
/// caused it.
/// </summary>
public sealed class SmokeDialogService : IDialogService
{
    private readonly SmokeLog _log;
    private readonly List<string> _messages = [];

    /// <summary>Creates the service.</summary>
    /// <param name="log">The smoke log.</param>
    public SmokeDialogService(SmokeLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <summary>The messages and errors shown so far, oldest first.</summary>
    public IReadOnlyList<string> Messages => _messages;

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> PickMediaFilesAsync() => Answer<IReadOnlyList<string>>("pick media files", []);

    /// <inheritdoc />
    public Task<string?> PickProjectToOpenAsync() => Answer<string?>("pick a project to open", null);

    /// <inheritdoc />
    public Task<string?> PickProjectSavePathAsync(string suggestedName, string? folder) => Answer<string?>("pick a project save path", null);

    /// <inheritdoc />
    public Task<string?> PickExportPathAsync(string suggestedPath, string extension, string formatName) => Answer<string?>("pick an export path", null);

    /// <inheritdoc />
    public Task<string?> PickFfmpegAsync() => Answer<string?>("locate ffmpeg", null);

    /// <inheritdoc />
    public Task<SaveChangesChoice> AskSaveChangesAsync(string projectName) => Answer("save changes to " + projectName + "? (discard)", SaveChangesChoice.Discard);

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) => Answer(title + ": " + message + " (" + confirmLabel + ")", true);

    /// <inheritdoc />
    public Task ShowMessageAsync(string title, string message)
    {
        Record("message: " + title + ": " + message);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowErrorAsync(ErrorDialogViewModel error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Record("error: " + error.Title + ": " + error.Summary + "\n" + error.DetailsText);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowExportDialogAsync(ExportDialogViewModel dialog)
    {
        LastDialog = dialog;
        return Answer("export dialog (not shown)", true);
    }

    /// <summary>
    /// The speed the next speed dialog answers with (the smoke <c>speed</c> command sets it); null cancels the dialog.
    /// </summary>
    public int? NextSpeedPercent { get; set; }

    /// <summary>Whether the next speed dialog ripples the following clips (the dialog default is on).</summary>
    public bool NextSpeedRipple { get; set; } = true;

    /// <inheritdoc />
    public Task<bool> ShowSpeedDialogAsync(SpeedDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        LastDialog = dialog;
        if (NextSpeedPercent is not { } percent)
        {
            return Answer("speed dialog (cancelled)", false);
        }

        dialog.Percent = percent;
        dialog.RippleFollowing = NextSpeedRipple;
        NextSpeedPercent = null;
        return Answer(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"speed dialog: {dialog.PercentValue}%, ripple {dialog.RippleFollowing}"), true);
    }

    /// <inheritdoc />
    public Task<bool> ShowSequenceSettingsDialogAsync(SequenceSettingsDialogViewModel dialog)
    {
        LastDialog = dialog;
        return Answer("sequence settings dialog (cancelled)", false);
    }

    /// <summary>
    /// The view model of the last Settings, Relink, Export, Speed or Sequence Settings dialog the app asked to show
    /// (the smoke command <c>show-dialog</c> renders it in its real window for a screenshot).
    /// </summary>
    public object? LastDialog { get; private set; }

    /// <summary>The last Settings dialog shown (the smoke <c>settings</c> command checks that Ctrl+, opened one).</summary>
    public SettingsViewModel? LastSettingsDialog { get; private set; }

    /// <inheritdoc />
    public Task<bool> ShowSettingsDialogAsync(SettingsViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        LastSettingsDialog = dialog;
        LastDialog = dialog;
        return Answer("settings dialog on page " + (SettingsPage)dialog.SelectedPageIndex + " (cancelled)", false);
    }

    /// <inheritdoc />
    public Task<string?> PickFolderAsync(string title, string? startFolder) => Answer<string?>("pick a folder: " + title, null);

    /// <summary>The text of the last clipboard copy (the smoke command <c>agent-briefing</c> checks it), or null.</summary>
    public string? LastClipboardText { get; private set; }

    /// <inheritdoc />
    public Task CopyToClipboardAsync(string text)
    {
        LastClipboardText = text;
        return Answer("copy to clipboard (" + text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " characters)", true);
    }

    /// <summary>
    /// The first-start dialog never registers a client in a smoke run: it answers Start with "Register with: none"
    /// (the client registry is a sandbox anyway, AGENT_INTEGRATION 9.3).
    /// </summary>
    /// <param name="dialog">The dialog state.</param>
    /// <returns>True (Start).</returns>
    public Task<bool> ShowMcpStartDialogAsync(McpStartDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        LastDialog = dialog;
        dialog.SelectedChoice = McpClientChoice.None;
        return Answer("MCP start dialog (Start without registering; " + dialog.Choices.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " choices)", true);
    }

    /// <summary>Agent > Connect an agent client... is recorded (for <c>show-dialog connect-clients</c>) and closed at once.</summary>
    /// <param name="dialog">The client list.</param>
    /// <returns>A completed task.</returns>
    public Task ShowConnectClientsDialogAsync(ConnectClientsViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        LastDialog = dialog;
        return Answer("connect clients dialog (" + dialog.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " clients; closed)", true);
    }

    /// <inheritdoc />
    public Task RevealInFileManagerAsync(string path) => Answer("reveal " + path, true);

    /// <inheritdoc />
    public Task<RecoveryChoice> AskRecoveryAsync(string projectName, string savedText) =>
        Answer("recover " + projectName + " from " + savedText + "? (not now)", RecoveryChoice.Ignore);

    /// <inheritdoc />
    public Task ShowRelinkDialogAsync(RelinkDialogViewModel dialog)
    {
        LastDialog = dialog;
        return Answer("relink dialog (not shown)", true);
    }

    /// <inheritdoc />
    public Task<string?> PickRelinkFileAsync(string fileName, string? startFolder) => Answer<string?>("locate " + fileName, null);

    private void Record(string text)
    {
        _messages.Add(text);
        _log.Write("dialog " + text);
    }

    private Task<T> Answer<T>(string what, T answer)
    {
        _log.Write("dialog " + what);
        return Task.FromResult(answer);
    }
}
