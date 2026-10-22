using Avalonia.Input;
using Cutlass.App.Services;
using Cutlass.App.Services.Agent;
using Cutlass.App.Services.Agent.Clients;
using Cutlass.App.ViewModels;
using Cutlass.Tests.Compiler;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// AGENT_INTEGRATION 6.6 and 9.4: the Agent menu commands run through <see cref="CommandIds"/> (F9 toggles the
/// server, one "Copy briefing for" command per client profile), Start shows the first-start dialog once and again
/// while a detected client misses its registration, "Keep it on" and "Allow for this session" reach the server
/// switch, Stop asks before it takes the project from an agent, the briefings reach the clipboard with the target's
/// tail (the plain item for the client registered last, the token only after the confirmation), Connect opens the
/// client list, and the menu stays usable while an agent holds the project. Every client lives in a fake home.
/// </summary>
public sealed class AgentMenuTests : IDisposable
{
    private const string Exe = ClientSandbox.Exe;
    private const string ExeJson = "C:\\\\Program Files\\\\Cutlass\\\\Cutlass.Agent.Mcp.exe";
    private readonly AgentShellFixture _f = new();
    private readonly ClientSandbox _box = new();
    private readonly FakeAgentServer _server = new() { McpExecutablePath = Exe };

    /// <summary>Wires the fake server and the sandbox registry.</summary>
    public AgentMenuTests()
    {
        _f.Vm.Agent.ExeExists = _ => Task.FromResult(true);
        _f.Vm.AgentServer = _server;
        _f.Vm.ClientRegistry = _box.Registry;
    }

    /// <summary>Removes the fixture's folders.</summary>
    public void Dispose()
    {
        _f.Dispose();
        _box.Dispose();
    }

    [Fact]
    public async Task TheMenuCommandsAndTheShortcutAreRegistered()
    {
        var vm = _f.Vm;
        await vm.EnsureClientsLoadedAsync();
        foreach (var id in new[] { CommandIds.AgentToggleServer, CommandIds.AgentCopyBriefing, CommandIds.AgentCopyBriefingHttp, CommandIds.AgentConnectClients, CommandIds.ShowAgentPanel, CommandIds.AgentTakeOver })
        {
            Assert.True(vm.Commands.IsRegistered(id), id);
        }

        Assert.Equal(12, vm.BriefingTargets.Count);
        Assert.Equal("HTTP only (curl)", vm.BriefingTargets[^1].Header);
        foreach (var target in vm.BriefingTargets)
        {
            Assert.True(vm.Commands.IsRegistered(target.CommandId), target.CommandId);
            Assert.Same(vm.RunCommandCommand, target.Command);
        }

        Assert.Equal(CommandIds.AgentToggleServer, DefaultKeymap.Build()[KeyChord.Parse("F9")]);
        Assert.Contains(KeyChord.Parse("F9"), Shell.KeymapCompletenessTests.DesignChords());
        Assert.Equal("_Start MCP server", vm.Agent.ServerMenuText);
    }

    [Fact]
    public async Task TheFirstStartShowsTheDialogOnceAndStartsForTheSession()
    {
        var vm = _f.Vm;
        _box.Write(".cursor/mcp.json", "{ \"mcpServers\": { \"cutlass\": { \"command\": \"" + ExeJson + "\", \"args\": [] } } }");
        _f.Dialogs.McpDialogAnswer = d =>
        {
            Assert.True(d.AllowForSession);
            Assert.False(d.KeepOn);
            Assert.Equal(["Cursor (current)", "none"], d.Choices.Select(c => c.DisplayText));
            Assert.Same(McpClientChoice.None, d.SelectedChoice);
            Assert.False(d.RegisterNow);
            return Task.FromResult(true);
        };

        Assert.True(vm.Keys.HandleKeyDown(Key.F9, KeyModifiers.None, textInputFocused: false));
        await WaitAsync(() => _server.Calls.Count == 1);
        Assert.Equal(["start"], _server.Calls);
        Assert.Single(_f.Dialogs.McpDialogs);
        Assert.True(_f.Services.Settings.McpStartDialogSeen);
        Assert.False(_f.Services.Settings.AgentConnectionsEnabled);
        Assert.StartsWith("MCP server ready at http://localhost:47390/", vm.ToastText, StringComparison.Ordinal);
        Assert.Equal("1 current", vm.Agent.ClientsText);

        // Stop, then Start again: the dialog was seen and a client has the registration, so it does not show.
        _server.IsSwitchedOn = true;
        await vm.ToggleServerAsync();
        await vm.ToggleServerAsync();
        Assert.Equal(["start", "stop", "start"], _server.Calls);
        Assert.Single(_f.Dialogs.McpDialogs);
    }

    [Fact]
    public async Task TheDialogShowsAgainWhileADetectedClientMissesItAndCancelStartsNothing()
    {
        var vm = _f.Vm;
        Directory.CreateDirectory(Path.Combine(_box.Home, ".cursor"));
        await _f.Services.ApplySettingsAsync(_f.Services.Settings with { McpStartDialogSeen = true }, CancellationToken.None);
        _f.Dialogs.McpDialogAnswer = d =>
        {
            Assert.True(d.RegisterNow);
            Assert.Equal("Cursor", d.SelectedChoice.Item!.Name);
            Assert.StartsWith("Add to ", d.CommandText, StringComparison.Ordinal);
            return Task.FromResult(false);
        };

        await vm.ToggleServerAsync();
        Assert.Single(_f.Dialogs.McpDialogs);
        Assert.Empty(_server.Calls);

        _f.Dialogs.McpDialogAnswer = d =>
        {
            d.KeepOn = true;
            d.SelectedChoice = McpClientChoice.None;
            return Task.FromResult(true);
        };
        await vm.ToggleServerAsync();
        Assert.Equal(["start keep"], _server.Calls);
        Assert.False(File.Exists(Path.Combine(_box.Home, ".cursor", "mcp.json")));

        // "Allow agent connections for this session" cleared: nothing starts.
        _server.IsSwitchedOn = false;
        _f.Dialogs.McpDialogAnswer = d =>
        {
            d.AllowForSession = false;
            return Task.FromResult(true);
        };
        await vm.ToggleServerAsync();
        Assert.Equal(["start keep"], _server.Calls);
    }

    [Fact]
    public void TheDialogDecision()
    {
        var notRegistered = new AgentClientProbe(true, null, AgentClientState.NotRegistered);
        var stale = new AgentClientProbe(true, null, AgentClientState.Stale);
        var current = new AgentClientProbe(true, null, AgentClientState.Current);
        var absent = new AgentClientProbe(false, null, AgentClientState.NotInstalled);
        Assert.True(McpStartDialogViewModel.ShouldShow(false, []));
        Assert.False(McpStartDialogViewModel.ShouldShow(true, []));
        Assert.False(McpStartDialogViewModel.ShouldShow(true, [absent]));
        Assert.True(McpStartDialogViewModel.ShouldShow(true, [absent, notRegistered]));
        Assert.True(McpStartDialogViewModel.ShouldShow(true, [stale]));
        Assert.False(McpStartDialogViewModel.ShouldShow(true, [notRegistered, current]));
    }

    [Fact]
    public async Task WhileAnAgentHoldsTheProjectTheMenuStaysAndStopAsksFirst()
    {
        var vm = _f.Vm;
        await vm.EnsureClientsLoadedAsync();
        var controls = new RevokingControls(_f);
        vm.Agent.Controls = controls;
        _server.IsSwitchedOn = true;
        _f.Grant("Claude Code", "Trim");
        Assert.True(vm.Agent.IsLocked);
        foreach (var id in new[] { CommandIds.AgentToggleServer, CommandIds.AgentCopyBriefing, CommandIds.AgentCopyBriefingHttp, CommandIds.AgentConnectClients, CommandIds.AgentCopyBriefingFor("cursor") })
        {
            Assert.True(vm.Commands.IsAllowed(id), id);
            Assert.True(vm.RunCommandCommand.CanExecute(id), id);
        }

        // No: the agent keeps the project and the server runs on.
        _f.Dialogs.ConfirmAnswer = false;
        await vm.ToggleServerAsync();
        Assert.Equal(["Stop the MCP server: Claude Code is editing. Stop the server and take over?"], _f.Dialogs.Confirmations);
        Assert.Empty(_server.Calls);
        Assert.True(vm.Agent.IsLocked);

        // Yes: take over, then stop.
        _f.Dialogs.ConfirmAnswer = true;
        await vm.ToggleServerAsync();
        Assert.Equal(1, controls.TakeOvers);
        Assert.False(vm.Agent.IsLocked);
        Assert.Equal(["stop"], _server.Calls);
    }

    [Fact]
    public async Task TheBriefingsCarryTheTargetsTailAndTheTokenOnlyAfterConfirming()
    {
        var vm = _f.Vm;
        _f.Grant("Claude Code", "Trim");

        // No client registered yet: the generic tail.
        await vm.CopyBriefingAsync(null);
        var generic = Assert.Single(_f.Dialogs.Clipboard);
        Assert.Empty(AgentBriefing.Unfilled(generic));
        Assert.Contains("- Lock: held by Claude Code", generic, StringComparison.Ordinal);
        Assert.Contains("Agent > Connect an agent client...", generic, StringComparison.Ordinal);
        Assert.DoesNotContain("{", generic, StringComparison.Ordinal);
        Assert.Empty(_f.Dialogs.Confirmations);
        Assert.Contains("MCP server is off", vm.ToastText, StringComparison.Ordinal);

        // A "Copy briefing for" item: that client's registration as the fallback.
        Assert.True(vm.Commands.TryExecute(CommandIds.AgentCopyBriefingFor("claude-code")));
        await WaitAsync(() => _f.Dialogs.Clipboard.Count == 2);
        Assert.Contains("\n  claude mcp add cutlass --scope user -- \"" + Exe + "\"", _f.Dialogs.Clipboard[^1], StringComparison.Ordinal);
        Assert.StartsWith("Briefing for Claude Code copied.", vm.ToastText, StringComparison.Ordinal);

        // HTTP only: the token file unless confirmed.
        _f.Dialogs.ConfirmAnswer = false;
        await vm.CopyBriefingAsync(MainWindowViewModel.HttpTarget);
        Assert.Contains("$(cat '" + _server.TokenFile + "')", _f.Dialogs.Clipboard[^1], StringComparison.Ordinal);
        Assert.DoesNotContain(_server.Token, _f.Dialogs.Clipboard[^1], StringComparison.Ordinal);
        Assert.StartsWith("Copy briefing (HTTP only (curl)): Put the agent token", _f.Dialogs.Confirmations[^1], StringComparison.Ordinal);

        _f.Dialogs.ConfirmAnswer = true;
        Assert.True(vm.Commands.TryExecute(CommandIds.AgentCopyBriefingHttp));
        await WaitAsync(() => _f.Dialogs.Clipboard.Count == 4);
        Assert.Contains("export CUTLASS_TOKEN='" + _server.Token + "'", _f.Dialogs.Clipboard[^1], StringComparison.Ordinal);

        // A harness also asks before the token goes in.
        await vm.CopyBriefingAsync("anthropic-sdk");
        Assert.Contains("v1/tools?format=anthropic", _f.Dialogs.Clipboard[^1], StringComparison.Ordinal);
        Assert.Contains("Token: " + _server.Token, _f.Dialogs.Clipboard[^1], StringComparison.Ordinal);
        Assert.StartsWith("Copy briefing (Anthropic SDK harness)", _f.Dialogs.Confirmations[^1], StringComparison.Ordinal);

        // After a registration the plain item renders for that client.
        Directory.CreateDirectory(Path.Combine(_box.Home, ".cursor"));
        await vm.Clients!.RefreshAsync(force: true);
        await vm.Clients.RegisterProfileAsync(vm.Clients.Items.Single(i => i.Id == "cursor"), null);
        Assert.Equal("cursor", _box.Registry.LastRegisteredClientId);
        await vm.CopyBriefingAsync(null);
        Assert.Contains("the MCP server \"cutlass\" in Cursor", _f.Dialogs.Clipboard[^1], StringComparison.Ordinal);
        Assert.Contains("  Add to " + Path.GetFullPath(Path.Combine(_box.Home, ".cursor", "mcp.json")), _f.Dialogs.Clipboard[^1], StringComparison.Ordinal);
        Assert.Empty(AgentBriefing.Unfilled(_f.Dialogs.Clipboard[^1]));
    }

    [Fact]
    public async Task ConnectOpensTheClientListAndTheBadgeFailureOpensTheAgentsPage()
    {
        var vm = _f.Vm;
        Assert.True(vm.Commands.TryExecute(CommandIds.AgentConnectClients));
        await WaitAsync(() => _f.Dialogs.ConnectDialogs.Count == 1);
        Assert.Same(vm.Clients, _f.Dialogs.ConnectDialogs[0]);
        await vm.Clients!.RefreshAsync(force: false);
        Assert.Equal(12, vm.Clients.Items.Count);
        Assert.Same(vm.Clients, vm.Agent.Clients);

        await vm.ShowAgentSettingsAsync();
        Assert.Equal((int)SettingsPage.Agents, _f.Dialogs.SettingsDialogs[0].SelectedPageIndex);
        Assert.Same(vm.Clients, _f.Dialogs.SettingsDialogs[0].Clients);
    }

    [Fact]
    public async Task WithoutARegistryConnectSaysSo()
    {
        var vm = _f.Vm;
        vm.ClientRegistry = null;
        await vm.ConnectClientsAsync();
        Assert.Contains("not available", _f.Dialogs.Messages[^1], StringComparison.Ordinal);
        Assert.Equal("unavailable", vm.Agent.ClientsText);
        await vm.CopyBriefingAsync(null);
        Assert.Contains("Agent > Connect an agent client...", _f.Dialogs.Clipboard[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheAgentTabStaysOnceRequestedWhileTheServerIsOff()
    {
        var vm = _f.Vm;
        Assert.False(vm.MediaBin.ShowAgentTab);
        Assert.True(vm.Commands.TryExecute(CommandIds.ShowAgentPanel));
        Assert.True(vm.MediaBin.ShowAgentTab);
        Assert.True(vm.MediaBin.IsAgentTab);
        vm.Agent.IsEnabled = true;
        vm.Agent.IsEnabled = false;
        Assert.True(vm.MediaBin.ShowAgentTab);
        Assert.True(vm.MediaBin.IsAgentTab);
    }

    [Fact]
    public void TheMenuInTheWindowFollowsTheSpec()
    {
        var xaml = File.ReadAllText(Path.Combine(Golden.RepositoryRoot(), "src", "Cutlass.App", "Views", "MainWindow.axaml"));
        var sequence = xaml.IndexOf("Header=\"_Sequence\"", StringComparison.Ordinal);
        var agent = xaml.IndexOf("Header=\"_Agent\"", StringComparison.Ordinal);
        var view = xaml.IndexOf("Header=\"_View\"", StringComparison.Ordinal);
        Assert.True(sequence < agent && agent < view, "the Agent menu sits between Sequence and View");
        var menu = xaml[agent..view];
        string[] order =
        [
            "CommandIds.AgentToggleServer", "CommandIds.AgentCopyBriefing}", "ItemsSource=\"{Binding BriefingTargets}\"",
            "CommandIds.AgentConnectClients", "CommandIds.ShowAgentPanel", "CommandIds.AgentTakeOver",
        ];
        var at = -1;
        foreach (var id in order)
        {
            var next = menu.IndexOf(id, StringComparison.Ordinal);
            Assert.True(next > at, id + " is out of order");
            at = next;
        }

        Assert.Contains("InputGesture=\"F9\"", menu, StringComparison.Ordinal);
        Assert.Contains("Header=\"C_onnect an agent client...\"", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("Register with", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("other clients", menu, StringComparison.Ordinal);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class RevokingControls(AgentShellFixture fixture) : IAgentLockControls
    {
        public int TakeOvers { get; private set; }

        public bool TakeOver()
        {
            TakeOvers++;
            return fixture.Lock.Revoke(fixture.Now);
        }

        public bool Pause() => false;

        public bool Resume() => false;

        public bool Answer(bool allow) => false;
    }
}
