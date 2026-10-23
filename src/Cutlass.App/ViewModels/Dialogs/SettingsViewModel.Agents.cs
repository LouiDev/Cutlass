using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services.Agent;
using Cutlass.App.ViewModels.Agent;
using Cutlass.Media.Settings;

namespace Cutlass.App.ViewModels.Dialogs;

/// <summary>
/// The Settings > Agents page (AGENT_INTEGRATION 6.1): "Allow agent connections" with its status line, the port, the
/// lease and the longest lease an agent may ask for, "Ask me before an agent takes the project", the read and write
/// folder lists, the token (masked, Copy, Regenerate) and the "Connect clients" group (AGENT_INTEGRATION 9.4): the
/// shared client list with its actions, the server's discovery URLs with Copy, and "Open agent docs".
/// The fields join the dialog's working copy and are validated and applied with the other pages.
/// </summary>
public sealed partial class SettingsViewModel
{
    /// <summary>The port range.</summary>
    public const int MinAgentPort = 1024, MaxAgentPort = 65535;

    /// <summary>The lease range in seconds (both the default lease and the most an agent may request).</summary>
    public const int MinLeaseSeconds = 15, MaxLeaseSeconds = 600;

    private IAgentSettingsHost? _agents;
    private string? _token;

    /// <summary>"Allow agent connections" (saved; <c>--agent-server</c> shows the switch on without changing it).</summary>
    [ObservableProperty]
    public partial bool AllowAgentConnections { get; set; }

    /// <summary>The port, as typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(ApplyCommand))]
    public partial string AgentPortText { get; set; } = string.Empty;

    /// <summary>The lease an agent gets, in seconds, as typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(ApplyCommand))]
    public partial string AgentLeaseText { get; set; } = string.Empty;

    /// <summary>The longest lease an agent may request, in seconds, as typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(OkCommand), nameof(ApplyCommand))]
    public partial string AgentMaxLeaseText { get; set; } = string.Empty;

    /// <summary>"Ask me before an agent takes the project".</summary>
    [ObservableProperty]
    public partial bool AgentAskFirst { get; set; }

    /// <summary>"Agent may read from": extra folders.</summary>
    public ObservableCollection<string> AgentReadFolders { get; } = [];

    /// <summary>"Agent may write to": extra folders.</summary>
    public ObservableCollection<string> AgentWriteFolders { get; } = [];

    /// <summary>The selected read folder (Remove acts on it).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveReadFolderCommand))]
    public partial string? SelectedReadFolder { get; set; }

    /// <summary>The selected write folder (Remove acts on it).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveWriteFolderCommand))]
    public partial string? SelectedWriteFolder { get; set; }

    /// <summary>The token, masked except its last four characters, or a placeholder while it loads.</summary>
    [ObservableProperty]
    public partial string AgentTokenText { get; set; } = "Loading...";

    /// <summary>The result of the last token action ("Copied", "Regenerated"), or empty.</summary>
    [ObservableProperty]
    public partial string AgentTokenNote { get; set; } = string.Empty;

    /// <summary>The server status line, for example "Listening on http://localhost:47390/".</summary>
    [ObservableProperty]
    public partial string AgentStatusText { get; set; } = "Off";

    /// <summary>True while the server listens (Success dot).</summary>
    [ObservableProperty]
    public partial bool IsAgentListening { get; set; }

    /// <summary>True when the server could not start (Danger).</summary>
    [ObservableProperty]
    public partial bool IsAgentError { get; set; }

    /// <summary>True when <c>--agent-server</c> turned connections on for this session.</summary>
    public bool IsAgentForcedOn => _agents?.IsForcedOn ?? false;

    /// <summary>The note under a forced switch (the switch itself reads "On for this session (--agent-server)"), else empty.</summary>
    public string AgentForcedNote => IsAgentForcedOn ? "Started with --agent-server: on for this session only; the saved setting is unchanged." : string.Empty;

    /// <summary>The note under the folder lists.</summary>
    public static string AgentFoldersNote =>
        "The project's folder and its media folders are always readable; the export folder (or the project's folder) is always writable.";

    /// <summary>The agent client list of the "Connect clients" group, or null when there is none.</summary>
    public ConnectClientsViewModel? Clients { get; }

    /// <summary>True when the "Connect clients" group shows the client list.</summary>
    public bool HasClients => Clients is not null;

    /// <summary>The MCP exe the clients start (shown under the group).</summary>
    public string McpPathText => _agents?.McpExecutablePath ?? "Cutlass.Agent.Mcp";

    /// <summary>Wraps a path in double quotes when it holds a space.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The path, quoted when needed.</returns>
    public static string QuoteIfNeeded(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Contains(' ', StringComparison.Ordinal) ? "\"" + path + "\"" : path;
    }

    /// <summary>Masks a token for display: asterisks and its last four characters.</summary>
    /// <param name="token">The token.</param>
    /// <returns>The masked text.</returns>
    public static string MaskToken(string? token) =>
        string.IsNullOrEmpty(token) ? "(none)" : new string('*', 16) + (token.Length > 4 ? token[^4..] : string.Empty);

    /// <summary>Reads the token and the server status (called by <see cref="LoadAsync"/>).</summary>
    /// <returns>A task that completes when the token was read.</returns>
    public async Task LoadAgentsAsync()
    {
        if (_agents is not { } agents)
        {
            AgentTokenText = "Unavailable";
            return;
        }

        UpdateAgentStatus();
        if (Clients is { } clients)
        {
            // The rows and the discovery URLs at once; the client checks (a CLI may take seconds) finish in the background.
            await clients.LoadAsync();
            _ = clients.RefreshAsync(force: false);
        }

        try
        {
            _token = await agents.ReadTokenAsync(CancellationToken.None);
            AgentTokenText = MaskToken(_token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentTokenText = "Could not read the token: " + ex.Message;
        }
    }

    private void AttachAgents(IAgentSettingsHost? agents)
    {
        _agents = agents;
        if (agents is not null)
        {
            // The dialog lives shorter than the host; the handler is removed when the dialog closes (Cancel or OK).
            agents.StatusChanged += OnAgentStatusChanged;
            CloseRequested += (_, _) => agents.StatusChanged -= OnAgentStatusChanged;
            UpdateAgentStatus();
        }
    }

    private void OnAgentStatusChanged(object? sender, EventArgs e) => UpdateAgentStatus();

    private void UpdateAgentStatus()
    {
        if (_agents is not { } agents)
        {
            return;
        }

        AgentStatusText = agents.StatusText;
        IsAgentListening = agents.Status == AgentServerStatus.Listening;
        IsAgentError = agents.Status is AgentServerStatus.PortInUse or AgentServerStatus.Error;
    }

    private void LoadAgentsFrom(AppSettings settings)
    {
        AllowAgentConnections = settings.AgentConnectionsEnabled;
        AgentPortText = settings.AgentPort.ToString(CultureInfo.InvariantCulture);
        AgentLeaseText = settings.AgentLeaseSeconds.ToString(CultureInfo.InvariantCulture);
        AgentMaxLeaseText = settings.AgentMaxLeaseSeconds.ToString(CultureInfo.InvariantCulture);
        AgentAskFirst = settings.AgentAskFirst;
        Fill(AgentReadFolders, settings.AgentReadableFolders);
        Fill(AgentWriteFolders, settings.AgentWritableFolders);
    }

    private static void Fill(ObservableCollection<string> target, IReadOnlyList<string> folders)
    {
        target.Clear();
        foreach (var folder in folders)
        {
            target.Add(folder);
        }
    }

    // The Agents page fields, validated after the other pages (tab order), applied on top of the built settings.
    private string? ValidateAgents(AppSettings built, out AppSettings result)
    {
        result = built;
        if (!TryParseWhole(AgentPortText, MinAgentPort, MaxAgentPort, out var port))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Agent port: enter a whole number from {MinAgentPort} to {MaxAgentPort}.");
        }

        if (!TryParseWhole(AgentLeaseText, MinLeaseSeconds, MaxLeaseSeconds, out var lease))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Agent lease: enter whole seconds from {MinLeaseSeconds} to {MaxLeaseSeconds}.");
        }

        if (!TryParseWhole(AgentMaxLeaseText, MinLeaseSeconds, MaxLeaseSeconds, out var maxLease))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Longest agent lease: enter whole seconds from {MinLeaseSeconds} to {MaxLeaseSeconds}.");
        }

        result = built with
        {
            AgentConnectionsEnabled = AllowAgentConnections,
            AgentPort = port,
            AgentLeaseSeconds = lease,
            AgentMaxLeaseSeconds = maxLease,
            AgentAskFirst = AgentAskFirst,
            AgentReadableFolders = [.. AgentReadFolders],
            AgentWritableFolders = [.. AgentWriteFolders],
        };
        return null;
    }

    [RelayCommand]
    private Task AddReadFolderAsync() => AddFolderAsync(AgentReadFolders, "Agent may read from");

    [RelayCommand]
    private Task AddWriteFolderAsync() => AddFolderAsync(AgentWriteFolders, "Agent may write to");

    private bool CanRemoveReadFolder() => SelectedReadFolder is not null;

    private bool CanRemoveWriteFolder() => SelectedWriteFolder is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveReadFolder))]
    private void RemoveReadFolder()
    {
        if (SelectedReadFolder is { } folder)
        {
            AgentReadFolders.Remove(folder);
            SelectedReadFolder = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveWriteFolder))]
    private void RemoveWriteFolder()
    {
        if (SelectedWriteFolder is { } folder)
        {
            AgentWriteFolders.Remove(folder);
            SelectedWriteFolder = null;
        }
    }

    private async Task AddFolderAsync(ObservableCollection<string> target, string title)
    {
        if (await _dialogs.PickFolderAsync(title, null) is not { } folder)
        {
            return;
        }

        var full = Path.GetFullPath(folder);
        if (!target.Any(f => SamePathText(f, full)))
        {
            target.Add(full);
        }
    }

    [RelayCommand]
    private async Task CopyTokenAsync()
    {
        if (_token is { } token)
        {
            await _dialogs.CopyToClipboardAsync(token);
            AgentTokenNote = "Copied.";
        }
    }

    [RelayCommand]
    private async Task RegenerateTokenAsync()
    {
        if (_agents is not { } agents ||
            !await _dialogs.ConfirmAsync(
                "Regenerate token",
                "Replace the agent token? Connected agents and MCP servers must reconnect with the new token.",
                "Regenerate"))
        {
            return;
        }

        try
        {
            _token = await agents.RegenerateTokenAsync(CancellationToken.None);
            AgentTokenText = MaskToken(_token);
            AgentTokenNote = "Regenerated.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AgentTokenNote = "Could not regenerate the token: " + ex.Message;
        }
    }

    [RelayCommand]
    private Task OpenAgentDocsAsync() =>
        _agents is { } agents ? _dialogs.OpenFileAsync(agents.AgentDocsPath) : Task.CompletedTask;
}
