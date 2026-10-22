using Cutlass.App.ViewModels;

namespace Cutlass.App.Services;

/// <summary>
/// Wraps a dialog service and counts the modal dialogs and pickers that are open (AGENT_INTEGRATION 2.4: the agent
/// host's <c>UserState.ModalOpen</c>). The spec puts <c>OpenDialogCount</c> on <c>AvaloniaDialogService</c>; a decorator
/// gives the same count for every implementation (the smoke service included) without touching each method, so it
/// wraps whatever service the composition root picked. The clipboard and the file manager are not modal and are not
/// counted. UI thread only, like every dialog.
/// </summary>
public sealed class TrackedDialogService : IDialogService
{
    private readonly IDialogService _inner;

    /// <summary>Creates the decorator.</summary>
    /// <param name="inner">The service that shows the dialogs.</param>
    public TrackedDialogService(IDialogService inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Raised after a dialog opened or closed (the count changed).</summary>
    public event EventHandler? DialogsChanged;

    /// <summary>The dialogs and pickers open now.</summary>
    public int OpenDialogCount { get; private set; }

    /// <summary>The wrapped service.</summary>
    public IDialogService Inner => _inner;

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> PickMediaFilesAsync() => TrackAsync(_inner.PickMediaFilesAsync);

    /// <inheritdoc />
    public Task<string?> PickProjectToOpenAsync() => TrackAsync(_inner.PickProjectToOpenAsync);

    /// <inheritdoc />
    public Task<string?> PickProjectSavePathAsync(string suggestedName, string? folder) =>
        TrackAsync(() => _inner.PickProjectSavePathAsync(suggestedName, folder));

    /// <inheritdoc />
    public Task<string?> PickExportPathAsync(string suggestedPath, string extension, string formatName) =>
        TrackAsync(() => _inner.PickExportPathAsync(suggestedPath, extension, formatName));

    /// <inheritdoc />
    public Task<string?> PickFfmpegAsync() => TrackAsync(_inner.PickFfmpegAsync);

    /// <inheritdoc />
    public Task<SaveChangesChoice> AskSaveChangesAsync(string projectName) => TrackAsync(() => _inner.AskSaveChangesAsync(projectName));

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) =>
        TrackAsync(() => _inner.ConfirmAsync(title, message, confirmLabel));

    /// <inheritdoc />
    public Task ShowMessageAsync(string title, string message) => TrackAsync(() => _inner.ShowMessageAsync(title, message));

    /// <inheritdoc />
    public Task ShowErrorAsync(ErrorDialogViewModel error) => TrackAsync(() => _inner.ShowErrorAsync(error));

    /// <inheritdoc />
    public Task ShowExportDialogAsync(ExportDialogViewModel dialog) => TrackAsync(() => _inner.ShowExportDialogAsync(dialog));

    /// <inheritdoc />
    public Task<bool> ShowSpeedDialogAsync(SpeedDialogViewModel dialog) => TrackAsync(() => _inner.ShowSpeedDialogAsync(dialog));

    /// <inheritdoc />
    public Task<bool> ShowSequenceSettingsDialogAsync(SequenceSettingsDialogViewModel dialog) =>
        TrackAsync(() => _inner.ShowSequenceSettingsDialogAsync(dialog));

    /// <inheritdoc />
    public Task<bool> ShowSettingsDialogAsync(SettingsViewModel dialog) => TrackAsync(() => _inner.ShowSettingsDialogAsync(dialog));

    /// <inheritdoc />
    public Task<bool> ShowMcpStartDialogAsync(McpStartDialogViewModel dialog) => TrackAsync(() => _inner.ShowMcpStartDialogAsync(dialog));

    /// <inheritdoc />
    public Task ShowConnectClientsDialogAsync(ConnectClientsViewModel dialog) => TrackAsync(() => _inner.ShowConnectClientsDialogAsync(dialog));

    /// <inheritdoc />
    public Task<string?> PickFolderAsync(string title, string? startFolder) => TrackAsync(() => _inner.PickFolderAsync(title, startFolder));

    /// <inheritdoc />
    public Task CopyToClipboardAsync(string text) => _inner.CopyToClipboardAsync(text);

    /// <inheritdoc />
    public Task RevealInFileManagerAsync(string path) => _inner.RevealInFileManagerAsync(path);

    /// <inheritdoc />
    public Task OpenFileAsync(string path) => _inner.OpenFileAsync(path);

    /// <inheritdoc />
    public Task<RecoveryChoice> AskRecoveryAsync(string projectName, string savedText) =>
        TrackAsync(() => _inner.AskRecoveryAsync(projectName, savedText));

    /// <inheritdoc />
    public Task ShowRelinkDialogAsync(RelinkDialogViewModel dialog) => TrackAsync(() => _inner.ShowRelinkDialogAsync(dialog));

    /// <inheritdoc />
    public Task<string?> PickRelinkFileAsync(string fileName, string? startFolder) =>
        TrackAsync(() => _inner.PickRelinkFileAsync(fileName, startFolder));

    private async Task<T> TrackAsync<T>(Func<Task<T>> show)
    {
        Enter();
        try
        {
            return await show();
        }
        finally
        {
            Leave();
        }
    }

    private async Task TrackAsync(Func<Task> show)
    {
        Enter();
        try
        {
            await show();
        }
        finally
        {
            Leave();
        }
    }

    private void Enter()
    {
        OpenDialogCount++;
        DialogsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Leave()
    {
        OpenDialogCount = Math.Max(0, OpenDialogCount - 1);
        DialogsChanged?.Invoke(this, EventArgs.Empty);
    }
}
