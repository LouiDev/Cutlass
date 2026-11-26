using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services.Agent;
using Cutlass.App.Services.Agent.Clients;
using Cutlass.App.Services.Agent.Clients.Profiles;
using Cutlass.App.Services.Commands;
using Cutlass.App.ViewModels.Agent;
using Cutlass.App.ViewModels.Dialogs;
using Cutlass.App.ViewModels.MediaBin;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The Agent menu (AGENT_INTEGRATION 6.6 and 9.4, MCP_HOSTING_CONCEPT 4): Start/Stop MCP server (F9) over
/// <see cref="IAgentServerSwitch"/> with the first-start dialog and the take-over question on Stop, "Copy briefing for
/// your agent" (for the client registered last, or the generic text) and its "Copy briefing for" submenu with one item
/// per client profile, and "Connect an agent client..." (the client list). The menu items go through
/// <see cref="CommandIds"/> and <c>RunCommandCommand</c> like every other menu; the Agent panel's header block and the
/// status bar badge reuse the same paths through <see cref="IAgentMenuActions"/>. Every command here is allowed while
/// an agent holds the project: none of them changes the project. The client profiles are data
/// (<see cref="AgentClientRegistry"/>); nothing here names a client.
/// </summary>
public sealed partial class MainWindowViewModel : IAgentMenuActions
{
    // The user asked for the AGENT tab (menu, badge, tab click): it stays while connections are off, so Start is reachable.
    private bool _agentTabRequested;
    private Task? _clientsLoad;

    /// <summary>The agent server's session switch (Agent > Start/Stop MCP server), or null when there is none.</summary>
    public IAgentServerSwitch? ServerSwitch => AgentServer as IAgentServerSwitch;

    /// <summary>
    /// The agent client registry (set by the app: the real user's folders, or a sandbox in smoke runs). Setting it
    /// creates the shared <see cref="Clients"/> list and loads the profiles off the UI thread; null disables the
    /// client features.
    /// </summary>
    public AgentClientRegistry? ClientRegistry
    {
        get;
        set
        {
            field = value;
            Clients = value is null ? null : new ConnectClientsViewModel(value, _dialogs, ClientContext, ReadTokenOrNullAsync);
            Agent.Clients = Clients;
            _clientsLoad = Clients is null ? null : LoadClientsAsync();
        }
    }

    /// <summary>The agent client list shared by the dialog, Settings > Agents, the panel and the first-start dialog.</summary>
    public ConnectClientsViewModel? Clients { get; private set; }

    /// <summary>The "Copy briefing for" submenu: one item per client profile (the plain HTTP profile last).</summary>
    public ObservableCollection<BriefingTargetItem> BriefingTargets { get; } = [];

    /// <summary>
    /// Where a client CLI runs: the open project's folder (a local scope registration belongs to it), else the user's
    /// home folder.
    /// </summary>
    /// <returns>The folder.</returns>
    public string RegistrationDirectory() =>
        HasProject && ProjectPath is { } path && Path.GetDirectoryName(path) is { Length: > 0 } folder
            ? folder
            : ClientRegistry?.Environment.Home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>The live values of a client registration: the MCP exe, the server URL, the token file and the CLI's folder.</summary>
    /// <returns>The context (without the token).</returns>
    public AgentClientContext ClientContext() =>
        new(McpExe, ServerUrl, TokenFile, null, RegistrationDirectory());

    /// <summary>Creates the first-start dialog from the client list's last check.</summary>
    /// <returns>The dialog state.</returns>
    public McpStartDialogViewModel CreateMcpStartDialog() => new(Clients, RegistrationDirectory());

    /// <summary>Waits until the client profiles are loaded and the submenu is filled (no-op without a registry).</summary>
    /// <returns>A task that completes when the profiles are there.</returns>
    public Task EnsureClientsLoadedAsync() => _clientsLoad ?? Task.CompletedTask;

    /// <summary>
    /// Resolves a briefing target (AGENT_INTEGRATION 9.4): null is the client registered last, or the generic tail
    /// when none was (or it is gone); <c>http</c> is the plain HTTP profile; any other id is that profile.
    /// </summary>
    /// <param name="targetId">The profile id, <c>http</c>, or null.</param>
    /// <returns>The profile (null for the generic tail), the tail's id and its kind.</returns>
    public (AgentClientProfile? Profile, string TailId, AgentClientBriefingKind Kind) ResolveBriefingTarget(string? targetId)
    {
        var registry = ClientRegistry;
        if (targetId is null)
        {
            return registry?.Find(registry.LastRegisteredClientId) is { } last
                ? (last, last.Id, last.BriefingKind)
                : (null, AgentBriefing.GenericTarget, AgentClientBriefingKind.Generic);
        }

        if (string.Equals(targetId, HttpTarget, StringComparison.OrdinalIgnoreCase))
        {
            var http = registry?.Find(HttpTarget) ?? registry?.Profiles.FirstOrDefault(p => p.BriefingKind == AgentClientBriefingKind.Http);
            return (http, http?.Id ?? HttpTarget, AgentClientBriefingKind.Http);
        }

        return registry?.Find(targetId) is { } profile
            ? (profile, profile.Id, profile.BriefingKind)
            : (null, AgentBriefing.GenericTarget, AgentClientBriefingKind.Generic);
    }

    /// <summary>
    /// Renders the briefing from the live project (AGENT_INTEGRATION 6.6 and 9.4): the neutral body (the setting's
    /// file, the user copy or the built-in one) and the target's tail (its user copy or the built-in one of its kind),
    /// read off the UI thread, with the project, sequence, tracks, media, lock, server URL, MCP exe, the target's name
    /// and registration, its tools URL and example filled in.
    /// </summary>
    /// <param name="targetId">The profile id, <c>http</c>, or null (the client registered last, else generic).</param>
    /// <param name="token">The token to put into the text (after the user confirmed), or null for the token file.</param>
    /// <returns>The briefing text.</returns>
    public async Task<string> BuildBriefingAsync(string? targetId, string? token)
    {
        await EnsureClientsLoadedAsync();
        var (profile, tailId, kind) = ResolveBriefingTarget(targetId);
        var settings = _services.Settings;
        var template = await AgentBriefing.LoadTemplateAsync(_services.Paths.AgentDirectory, settings.AgentBriefingTemplatePath, tailId, kind, CancellationToken.None);
        var context = ClientContext() with { Token = token };
        var registration = profile is { IsTextOnly: false } && ClientRegistry is { } registry
            ? AgentBriefing.Indent(registry.RegistrationText(profile, context))
            : "  In Cutlass: Agent > Connect an agent client...";
        var values = AgentBriefing.Values(
            Session.Current,
            HasProject,
            HasProject ? ProjectPath : null,
            IsDirty,
            Agent.State,
            context.Url,
            context.Exe,
            registration,
            kind == AgentClientBriefingKind.Http ? AgentBriefing.TokenLine(token, TokenFile) : AgentBriefing.TokenText(token, TokenFile),
            AgentServer?.AgentDocsPath ?? "AGENT_INTEGRATION.md",
            profile?.Name ?? "your agent client",
            profile?.ToolsFormat is { Length: > 0 } format ? context.Url + "v1/tools?format=" + format : null,
            profile?.Example);
        return AgentBriefing.Render(template.Text, values);
    }

    /// <inheritdoc />
    public async Task ToggleServerAsync()
    {
        if (ServerSwitch is not { } server)
        {
            ShowToast("The agent server is not available in this window.");
            return;
        }

        if (server.IsSwitchedOn)
        {
            await StopMcpServerAsync(server);
        }
        else
        {
            await StartMcpServerAsync(server);
        }
    }

    /// <inheritdoc />
    public async Task CopyBriefingAsync(string? targetId)
    {
        await EnsureClientsLoadedAsync();
        var (profile, _, kind) = ResolveBriefingTarget(targetId);
        string? token = null;
        if (kind is AgentClientBriefingKind.Http or AgentClientBriefingKind.Harness && AgentServer is not null &&
            await ConnectClientsViewModel.ConfirmTokenAsync(_dialogs, "Copy briefing (" + (profile?.Name ?? "HTTP only") + ")"))
        {
            token = await ReadTokenOrNullAsync();
        }

        var text = await BuildBriefingAsync(targetId, token);
        await _dialogs.CopyToClipboardAsync(text);
        var target = profile is null ? string.Empty : " for " + profile.Name;
        ShowToast(ServerSwitch is { IsSwitchedOn: false }
            ? "Briefing" + target + " copied. The MCP server is off: start it with Agent > Start MCP server (F9)."
            : "Briefing" + target + " copied. Paste it into your agent session.");
    }

    /// <inheritdoc />
    public async Task ConnectClientsAsync()
    {
        if (Clients is not { } clients)
        {
            await _dialogs.ShowMessageAsync("Connect an agent client", "Connecting agent clients is not available in this window.");
            return;
        }

        await clients.LoadAsync();
        _ = clients.RefreshAsync(force: true);
        await _dialogs.ShowConnectClientsDialogAsync(clients);
    }

    /// <inheritdoc />
    public Task ShowAgentSettingsAsync() => ShowSettingsAsync(SettingsPage.Agents);

    /// <summary>The target id of the plain HTTP briefing.</summary>
    public const string HttpTarget = "http";

    private string McpExe => AgentServer?.McpExecutablePath ?? "Cutlass.Agent.Mcp";

    private string ServerUrl =>
        ServerSwitch?.ExpectedBaseUrl ?? "http://localhost:" + _services.Settings.AgentPort.ToString(CultureInfo.InvariantCulture) + "/";

    private string TokenFile => ServerSwitch?.TokenFile ?? Path.Combine(_services.Paths.AgentDirectory, "token");

    private async Task<string?> ReadTokenOrNullAsync()
    {
        if (AgentServer is not { } host)
        {
            return null;
        }

        try
        {
            return await host.ReadTokenAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("The token could not be read (" + ex.Message + "); the text names the token file instead.");
            return null;
        }
    }

    // Called once from the constructor, after InitializeAgent.
    private void InitializeMcp()
    {
        Agent.Actions = this;
        Commands.Register(CommandIds.AgentToggleServer, ToggleMcpServerCommand);
        Commands.Register(CommandIds.AgentCopyBriefing, CopyAgentBriefingCommand, null);
        Commands.Register(CommandIds.AgentCopyBriefingHttp, CopyAgentBriefingCommand, HttpTarget);
        Commands.Register(CommandIds.AgentConnectClients, ConnectAgentClientsCommand);
        MediaBin.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MediaBinViewModel.IsAgentTab) && MediaBin.IsAgentTab)
            {
                _agentTabRequested = true;
                _ = Agent.OnPanelShownAsync();
            }
        };
    }

    // Loads the profiles and the last registered client, then fills the "Copy briefing for" submenu.
    private async Task LoadClientsAsync()
    {
        if (Clients is not { } clients || ClientRegistry is not { } registry)
        {
            return;
        }

        try
        {
            await clients.LoadAsync();
            await registry.LoadLastRegisteredAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            _log?.Write("agent: the client profiles could not be loaded: " + ex.Message);
            return;
        }

        foreach (var problem in registry.LoadProblems)
        {
            _log?.Write("agent: " + problem);
        }

        BriefingTargets.Clear();
        foreach (var profile in registry.Profiles)
        {
            var id = CommandIds.AgentCopyBriefingFor(profile.Id);
            Commands.Register(id, CopyAgentBriefingCommand, profile.Id);
            BriefingTargets.Add(new BriefingTargetItem(profile.Id, profile.Name.Replace("_", "__", StringComparison.Ordinal), id, RunCommandCommand));
        }

        if (!registry.Profiles.Any(p => p.BriefingKind == AgentClientBriefingKind.Http))
        {
            BriefingTargets.Add(new BriefingTargetItem(HttpTarget, "HTTP only", CommandIds.AgentCopyBriefingHttp, RunCommandCommand));
        }
    }

    // The AGENT tab header shows while connections are on, while an agent holds the project, or once the user asked for it.
    private void UpdateAgentTab() => MediaBin.ShowAgentTab = Agent.IsEnabled || Agent.IsLocked || _agentTabRequested;

    [RelayCommand]
    private Task ToggleMcpServerAsync() => ToggleServerAsync();

    [RelayCommand]
    private Task CopyAgentBriefingAsync(string? targetId) => CopyBriefingAsync(targetId);

    [RelayCommand]
    private Task ConnectAgentClientsAsync() => ConnectClientsAsync();

    private async Task StartMcpServerAsync(IAgentServerSwitch server)
    {
        IReadOnlyList<AgentClientProbe> probes = [];
        if (Clients is { } clients)
        {
            await clients.RefreshAsync(force: false);
            probes = [.. clients.Items.Where(i => !i.IsTextOnly).Select(i => i.Probe)];
        }

        var keepOn = false;
        if (McpStartDialogViewModel.ShouldShow(_services.Settings.McpStartDialogSeen, probes))
        {
            var dialog = CreateMcpStartDialog();
            if (!await _dialogs.ShowMcpStartDialogAsync(dialog))
            {
                return;
            }

            await RememberMcpDialogSeenAsync();
            if (!dialog.AllowForSession)
            {
                return;
            }

            keepOn = dialog.KeepOn;
        }

        await server.StartForSessionAsync(keepOn);
        ShowToast(server.BaseUrl is { } url
            ? "MCP server ready at " + url + (keepOn ? " (on in future sessions too)." : " for this session.")
            : "The MCP server did not start: " + (AgentServer?.StatusText ?? "unknown error") + ".");
    }

    private async Task StopMcpServerAsync(IAgentServerSwitch server)
    {
        if (Agent.IsLocked)
        {
            var name = string.IsNullOrWhiteSpace(Agent.State.AgentName) ? "An agent" : Agent.State.AgentName;
            if (!await _dialogs.ConfirmAsync("Stop the MCP server", name + " is editing. Stop the server and take over?", "Stop and take over"))
            {
                return;
            }

            Agent.Controls?.TakeOver();
        }

        await server.StopForSessionAsync();
        ShowToast(_services.Settings.AgentConnectionsEnabled
            ? "MCP server stopped for this session (Allow agent connections stays on in Settings)."
            : "MCP server stopped.");
    }

    private async Task RememberMcpDialogSeenAsync()
    {
        if (_services.Settings.McpStartDialogSeen)
        {
            return;
        }

        try
        {
            await _services.ApplySettingsAsync(_services.Settings with { McpStartDialogSeen = true }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Write("agent: McpStartDialogSeen could not be saved: " + ex.Message);
        }
    }
}
