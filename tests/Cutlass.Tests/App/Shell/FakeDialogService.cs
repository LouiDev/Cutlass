using Cutlass.App.Services;
using Cutlass.App.ViewModels;

namespace Cutlass.Tests.App.Shell;

/// <summary>A dialog service that answers every question with "cancel" and records messages.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public List<string> Messages { get; } = [];

    public Task<IReadOnlyList<string>> PickMediaFilesAsync() => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> PickProjectToOpenAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickProjectSavePathAsync(string suggestedName, string? folder) => Task.FromResult<string?>(null);

    public Task<string?> PickExportPathAsync(string suggestedPath, string extension, string formatName) => Task.FromResult<string?>(null);

    public string? NextFfmpeg { get; set; }

    public Task<string?> PickFfmpegAsync() => Task.FromResult(NextFfmpeg);

    public Task<SaveChangesChoice> AskSaveChangesAsync(string projectName) => Task.FromResult(SaveChangesChoice.Cancel);

    public bool ConfirmAnswer { get; set; }

    public List<string> Confirmations { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        Confirmations.Add(title + ": " + message);
        return Task.FromResult(ConfirmAnswer);
    }

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add(title + ": " + message);
        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(ErrorDialogViewModel error) => Task.CompletedTask;

    public Task ShowExportDialogAsync(ExportDialogViewModel dialog) => Task.CompletedTask;

    public Task<bool> ShowSpeedDialogAsync(SpeedDialogViewModel dialog) => Task.FromResult(false);

    public Task<bool> ShowSequenceSettingsDialogAsync(SequenceSettingsDialogViewModel dialog) => Task.FromResult(false);

    public List<SettingsViewModel> SettingsDialogs { get; } = [];

    public Task<bool> ShowSettingsDialogAsync(SettingsViewModel dialog)
    {
        SettingsDialogs.Add(dialog);
        return Task.FromResult(false);
    }

    public string? NextFolder { get; set; }

    public Task<string?> PickFolderAsync(string title, string? startFolder) => Task.FromResult(NextFolder);

    public List<string> Clipboard { get; } = [];

    public Task CopyToClipboardAsync(string text)
    {
        Clipboard.Add(text);
        return Task.CompletedTask;
    }

    public List<McpStartDialogViewModel> McpDialogs { get; } = [];

    /// <summary>Answers the MCP start dialog (null cancels it).</summary>
    public Func<McpStartDialogViewModel, Task<bool>>? McpDialogAnswer { get; set; }

    public Task<bool> ShowMcpStartDialogAsync(McpStartDialogViewModel dialog)
    {
        McpDialogs.Add(dialog);
        return McpDialogAnswer?.Invoke(dialog) ?? Task.FromResult(false);
    }

    public List<ConnectClientsViewModel> ConnectDialogs { get; } = [];

    public Task ShowConnectClientsDialogAsync(ConnectClientsViewModel dialog)
    {
        ConnectDialogs.Add(dialog);
        return Task.CompletedTask;
    }

    public Task RevealInFileManagerAsync(string path) => Task.CompletedTask;
}
