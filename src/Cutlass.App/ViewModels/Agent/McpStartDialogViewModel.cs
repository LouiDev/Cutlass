using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services.Agent.Clients;
using Cutlass.App.Services.Agent.Clients.Profiles;

namespace Cutlass.App.ViewModels.Agent;

/// <summary>
/// The first-start dialog of Agent > Start MCP server (MCP_HOSTING_CONCEPT 4.4, AGENT_INTEGRATION 6.6 and 9.4): "Allow
/// agent connections for this session" (on), "Keep it on in future sessions" (off) and "Register with:" a dropdown of
/// the detected clients plus "none", with the exact command or configuration entry that registering writes and, for a
/// client CLI with scopes, the scope. Start registers the chosen client through the shared
/// <see cref="ConnectClientsViewModel"/> (off the UI thread) and keeps the dialog open with the output and a Close
/// button; with "none" Start closes it at once. The caller starts the server after the dialog closed with true. UI
/// thread only; it never touches controls.
/// </summary>
public sealed partial class McpStartDialogViewModel : ObservableObject
{
    private readonly ConnectClientsViewModel? _clients;

    /// <summary>Creates the dialog state from the client list's last check.</summary>
    /// <param name="clients">The client list, or null when registering is unavailable.</param>
    /// <param name="workingDirectory">Where a client CLI runs (a local scope belongs to it), or null.</param>
    public McpStartDialogViewModel(ConnectClientsViewModel? clients, string? workingDirectory)
    {
        _clients = clients;
        LocalFolderText = workingDirectory ?? "the current folder";
        var detected = clients?.Items.Where(i => !i.IsTextOnly && i.Probe.Installed).Select(i => new McpClientChoice(i)).ToList() ?? [];
        Choices = [.. detected, McpClientChoice.None];
        HasDetectedClients = detected.Count > 0;
        SelectedChoice = detected.FirstOrDefault(c => c.Item!.Probe.State is AgentClientState.NotRegistered or AgentClientState.Stale) ?? McpClientChoice.None;
        AllowForSession = true;
        OutputText = string.Empty;
    }

    /// <summary>Raised when the dialog should close: true for Start (or Close after a registration), false for Cancel.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>The window title.</summary>
    public static string Title => "Start the MCP server";

    /// <summary>The explanation at the top.</summary>
    public static string IntroText =>
        "An MCP client starts the Cutlass MCP server itself. It needs the agent server of this window, and the client needs to know the server. Both can be set up here; Agent > Connect an agent client... lists every client.";

    /// <summary>The detected clients and "none".</summary>
    public IReadOnlyList<McpClientChoice> Choices { get; }

    /// <summary>True when at least one client was detected.</summary>
    public bool HasDetectedClients { get; }

    /// <summary>"Allow agent connections for this session".</summary>
    [ObservableProperty]
    public partial bool AllowForSession { get; set; }

    /// <summary>"Keep it on in future sessions" (saves "Allow agent connections").</summary>
    [ObservableProperty]
    public partial bool KeepOn { get; set; }

    /// <summary>The client "Register with:" names, or <see cref="McpClientChoice.None"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegisterNow), nameof(CommandText), nameof(StatusText), nameof(Scopes), nameof(HasScopes), nameof(CommandHeader))]
    public partial McpClientChoice SelectedChoice { get; set; }

    /// <summary>The scope for a client CLI with scopes (the profile's default first).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandText))]
    public partial string? SelectedScope { get; set; }

    /// <summary>The output after Start ran the registration, or empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutput))]
    public partial string OutputText { get; private set; }

    /// <summary>True while the registration runs (the buttons are disabled).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand), nameof(CancelCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>True once the registration ran; the primary button reads Close.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryText), nameof(ShowCancel))]
    public partial bool IsDone { get; private set; }

    /// <summary>True when the last registration succeeded.</summary>
    public bool Registered { get; private set; }

    /// <summary>True when Start registers a client.</summary>
    public bool RegisterNow => SelectedChoice.Item is not null;

    /// <summary>The scopes of the chosen client's CLI (empty for most clients).</summary>
    public IReadOnlyList<string> Scopes => SelectedChoice.Item?.Scopes ?? [];

    /// <summary>True when the scope dropdown shows.</summary>
    public bool HasScopes => Scopes.Count > 1;

    /// <summary>"which runs:" for a CLI, "which writes:" for a configuration file.</summary>
    public string CommandHeader => SelectedChoice.Item?.Profile.Kind == AgentClientKind.Cli ? "Registering runs:" : "Registering writes:";

    /// <summary>The exact command or configuration entry Start writes, or what "none" means.</summary>
    public string CommandText => SelectedChoice.Item is { } item && _clients is { } clients
        ? clients.Registry.RegistrationText(item.Profile, clients.Context(), SelectedScope ?? item.SelectedScope)
        : HasDetectedClients
            ? "Nothing is registered now; Agent > Connect an agent client... does it later."
            : "No agent client was detected on this machine. Agent > Connect an agent client... lists every client and its setup text.";

    /// <summary>The folder a local scope registration belongs to.</summary>
    public string LocalFolderText { get; }

    /// <summary>The chosen client and its state ("name: not registered"), or empty for "none".</summary>
    public string StatusText => SelectedChoice.Item is { } item ? item.Name + ": " + item.StateText : string.Empty;

    /// <summary>True when the output box shows.</summary>
    public bool HasOutput => OutputText.Length > 0;

    /// <summary>"Start" or "Close".</summary>
    public string PrimaryText => IsDone ? "Close" : "Start";

    /// <summary>True while Cancel shows (until the registration ran).</summary>
    public bool ShowCancel => !IsDone;

    /// <summary>
    /// Whether Agent > Start MCP server shows this dialog (MCP_HOSTING_CONCEPT 4.4): the first time, and again while a
    /// detected client cannot reach this exe (not registered or stale) and no client has a current registration.
    /// </summary>
    /// <param name="dialogSeen">The <c>McpStartDialogSeen</c> setting.</param>
    /// <param name="probes">The last check of every client.</param>
    /// <returns>True to show the dialog.</returns>
    public static bool ShouldShow(bool dialogSeen, IEnumerable<AgentClientProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);
        var list = probes.ToList();
        return !dialogSeen || (list.Any(p => p.IsMissing) && !list.Any(p => p.State == AgentClientState.Current));
    }

    partial void OnSelectedChoiceChanged(McpClientChoice value) => SelectedScope = value.Item?.SelectedScope;

    private bool CanPrimary() => !IsBusy;

    private bool CanCancel() => !IsBusy;

    /// <summary>Start: registers the chosen client and shows the output; with "none" (or after it ran) closes with true.</summary>
    /// <returns>A task that completes when the registration ran or the dialog asked to close.</returns>
    [RelayCommand(CanExecute = nameof(CanPrimary))]
    private async Task PrimaryAsync()
    {
        if (IsDone || SelectedChoice.Item is not { } item || _clients is null)
        {
            CloseRequested?.Invoke(this, true);
            return;
        }

        IsBusy = true;
        OutputText = CommandText + "\nRunning...";
        try
        {
            var result = await _clients.RegisterProfileAsync(item, SelectedScope);
            Registered = result.Succeeded;
            OutputText = result.Output + "\n\n" + (result.Succeeded
                ? "Registered with " + item.Name + "." + (item.Profile.Note is { Length: > 0 } note ? " " + note : string.Empty)
                : "The registration failed; see the output above.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            OutputText = "The registration could not run: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsDone = true;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>Cancel: closes with false.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
