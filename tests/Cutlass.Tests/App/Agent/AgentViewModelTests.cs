using Cutlass.Agent.Hosting;
using Cutlass.Agent.Lock;
using Cutlass.Agent.Tools.Audit;
using Cutlass.App.Services.Agent;
using Cutlass.App.ViewModels.Agent;
using Cutlass.Core.Editing;
using Cutlass.Core.Session;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// AGENT_INTEGRATION 6.2 and 6.3: the banner, badge and panel state of <see cref="AgentViewModel"/> follow the lock's
/// transitions (free, held, paused, pending, asking, and the 3 s outcome after the lock ended) with a manual clock,
/// and the buttons reach the lock through <see cref="IAgentLockControls"/>.
/// </summary>
public sealed class AgentViewModelTests
{
    private long _now = 5_000_000;

    [Fact]
    public void FreeShowsNoBannerAndTheServerBadge()
    {
        var (vm, _, _) = Create();
        Assert.False(vm.IsBannerVisible);
        Assert.False(vm.IsLocked);
        Assert.Equal("MCP off", vm.BadgeText);
        Assert.True(vm.BadgeIsIdle);
        Assert.False(vm.TakeOverCommand.CanExecute(null));
        Assert.Equal("_Start MCP server", vm.ServerMenuText);
        Assert.Equal("Start", vm.ServerButtonText);

        // AGENT_INTEGRATION 6.6: the badge follows the server once an agent does not hold the project.
        // An exe that exists, so the background exe check cannot turn the badge amber.
        var server = new FakeAgentServer { BaseUrl = "http://localhost:47390/", McpExecutablePath = typeof(AgentViewModelTests).Assembly.Location };
        vm.Server = server;
        server.Set(AgentServerStatus.Listening, "Listening on http://localhost:47390/");
        vm.IsEnabled = true;
        Assert.Equal("MCP ready", vm.BadgeText);
        Assert.True(vm.BadgeIsOk);
        Assert.False(vm.BadgeIsIdle);
        Assert.Equal("ready", vm.ServerStateText);
        Assert.Equal("http://localhost:47390/", vm.ServerUrlText);
        Assert.Equal("_Stop MCP server", vm.ServerMenuText);
        Assert.Equal("Stop", vm.ServerButtonText);

        server.Set(AgentServerStatus.PortInUse, "Port 47390 is in use");
        Assert.Equal("MCP: port in use", vm.BadgeText);
        Assert.True(vm.BadgeIsDanger);
        Assert.True(vm.IsServerError);
        Assert.Equal(string.Empty, vm.ServerUrlText);
        Assert.Contains("Settings > Agents", vm.BadgeToolTip, StringComparison.Ordinal);

        server.Set(AgentServerStatus.Error, "Access denied");
        Assert.Equal("MCP: error", vm.BadgeText);
        server.Set(AgentServerStatus.OtherWindow, "Another Cutlass window serves agents");
        Assert.Equal("MCP: other window", vm.BadgeText);
        Assert.True(vm.BadgeIsIdle);
        server.Set(AgentServerStatus.Off, "Off");
        Assert.Equal("MCP off", vm.BadgeText);
    }

    [Fact]
    public async Task AMissingExeTurnsTheReadyBadgeAmber()
    {
        var (vm, _, _) = Create();
        var server = new FakeAgentServer { McpExecutablePath = Path.Combine(Path.GetTempPath(), "cutlass-no-such-dir", "Cutlass.Agent.Mcp.exe") };
        vm.Server = server;
        server.Set(AgentServerStatus.Listening, "Listening on http://localhost:47390/");
        await vm.RefreshExeAsync();
        Assert.Equal("missing", vm.ExeStateText);
        Assert.True(vm.IsExeMissing);
        Assert.Equal("MCP: exe missing", vm.BadgeText);
        Assert.True(vm.BadgeIsWarning);

        var exe = Path.GetTempFileName();
        try
        {
            server.McpExecutablePath = exe;
            await vm.RefreshExeAsync();
            Assert.Equal("found", vm.ExeStateText);
            Assert.Equal("MCP ready", vm.BadgeText);
        }
        finally
        {
            File.Delete(exe);
        }
    }

    [Fact]
    public async Task TheBadgeOpensThePanelOrSettingsWhenTheServerFailed()
    {
        var (vm, _, _) = Create();
        var actions = new FakeActions();
        vm.Actions = actions;
        var shown = 0;
        vm.ShowPanelRequested += (_, _) => shown++;
        await vm.BadgeClickCommand.ExecuteAsync(null);
        Assert.Equal(1, shown);

        var server = new FakeAgentServer();
        vm.Server = server;
        server.Set(AgentServerStatus.PortInUse, "Port 47390 is in use");
        await vm.BadgeClickCommand.ExecuteAsync(null);
        Assert.Equal(1, shown);
        Assert.Equal(["settings"], actions.Calls);

        await vm.ToggleServerCommand.ExecuteAsync(null);
        await vm.CopyBriefingCommand.ExecuteAsync(null);
        await vm.ConnectClientsCommand.ExecuteAsync(null);
        Assert.Equal(["settings", "toggle", "briefing last", "connect"], actions.Calls);
    }

    [Fact]
    public void HeldShowsTheBannerWithNameReasonLastCallElapsedAndBatch()
    {
        var (vm, agentLock, controls) = Create();
        var locked = 0;
        vm.LockedChanged += (_, _) => locked++;
        var id = Acquire(agentLock, "Claude Code", "Remove silences, add title");

        Assert.Equal(1, locked);
        Assert.True(vm.IsBannerVisible);
        Assert.True(vm.IsLocked);
        Assert.True(vm.IsHeld);
        Assert.True(vm.ShowBannerControls);
        Assert.Equal("Claude Code", vm.AgentNameText);
        Assert.Equal("is editing: Remove silences, add title", vm.HeadlineText);
        Assert.Equal("no calls yet", vm.LastCallText);
        Assert.Equal("0:00", vm.ElapsedText);
        Assert.Equal("Pause", vm.PauseResumeText);
        Assert.Equal("MCP: Claude Code", vm.BadgeText);
        Assert.True(vm.BadgeIsAccent);
        Assert.True(vm.TakeOverCommand.CanExecute(null));

        _now += 3_000;
        agentLock.Touch(id, "edit.split", _now);
        _now += 2_000;
        agentLock.NoteBatch("Remove silences");
        vm.Refresh();
        Assert.Equal("edit.split, 2 s ago", vm.LastCallText);
        Assert.Equal("0:05", vm.ElapsedText);
        Assert.Equal("Batch: Remove silences", vm.BatchText);
        Assert.Equal("Lease 1:28 left", vm.LeaseText);
        Assert.Equal("Held by Claude Code", vm.PhaseText);

        vm.PauseResumeCommand.Execute(null);
        Assert.Equal(["pause"], controls.Calls);
    }

    [Fact]
    public void PausedTurnsTheButtonToResumeAndTheBadgeAmber()
    {
        var (vm, agentLock, controls) = Create();
        Acquire(agentLock, "Claude Code", "Trim");
        Assert.True(agentLock.Pause(_now));

        Assert.True(vm.IsPaused);
        Assert.False(vm.IsHeld);
        Assert.True(vm.IsLocked);
        Assert.Equal("Resume", vm.PauseResumeText);
        Assert.Equal("MCP paused", vm.BadgeText);
        Assert.True(vm.BadgeIsAmber);
        Assert.False(vm.BadgeIsAccent);
        Assert.StartsWith("Lease paused, ", vm.LeaseText, StringComparison.Ordinal);

        vm.PauseResumeCommand.Execute(null);
        Assert.Equal(["resume"], controls.Calls);
    }

    [Theory]
    [InlineData("revoke", "You took over")]
    [InlineData("release", "Claude Code finished")]
    [InlineData("expire", "Claude Code timed out")]
    public void TheOutcomeShowsForThreeSecondsAfterTheLockEnds(string how, string text)
    {
        var (vm, agentLock, _) = Create();
        var locked = 0;
        var id = Acquire(agentLock, "Claude Code", "Trim");
        vm.LockedChanged += (_, _) => locked++;
        switch (how)
        {
            case "revoke":
                agentLock.Revoke(_now);
                break;
            case "release":
                agentLock.Release(id, AgentUserState.Idle, _now);
                break;
            default:
                _now += 91_000;
                agentLock.Tick(_now, AgentUserState.Idle);
                break;
        }

        Assert.Equal(1, locked);
        Assert.False(vm.IsLocked);
        Assert.True(vm.IsShowingOutcome);
        Assert.True(vm.IsBannerVisible);
        Assert.False(vm.ShowBannerControls);
        Assert.Equal(text, vm.HeadlineText);
        Assert.Equal("MCP off", vm.BadgeText);

        _now += 2_900;
        vm.Refresh();
        Assert.True(vm.IsBannerVisible);
        _now += 200;
        vm.Refresh();
        Assert.False(vm.IsShowingOutcome);
        Assert.False(vm.IsBannerVisible);
    }

    [Fact]
    public void APendingAgentShowsInTheBannerAndTheStatusBar()
    {
        var (vm, agentLock, _) = Create();
        var busy = new AgentUserState(InGesture: true, ModalOpen: false, ExportRunning: false, IsPlaying: false);
        var outcome = agentLock.Acquire(new AcquireRequest("Cursor", "Add a title", WaitMs: 10_000), busy, _now);
        Assert.Equal(AcquireStatus.Waiting, outcome.Status);

        Assert.True(vm.IsPending);
        Assert.False(vm.IsLocked);
        Assert.True(vm.IsBannerVisible);
        Assert.Equal("Cursor", vm.AgentNameText);
        Assert.Equal("is waiting for the project: Add a title", vm.HeadlineText);
        Assert.Equal("Cursor is waiting for the project", vm.PendingText);
        Assert.Equal("MCP waiting", vm.BadgeText);

        // The gesture ends: the grant happens and the banner turns into the held banner.
        agentLock.OnUserStateChanged(AgentUserState.Idle, _now);
        Assert.True(vm.IsHeld);
        Assert.Equal(string.Empty, vm.PendingText);
    }

    [Fact]
    public void AskFirstShowsAllowAndDeny()
    {
        var (vm, agentLock, controls) = Create();
        agentLock.Policy = AgentPolicy.Default with { AskFirst = true };
        agentLock.Acquire(new AcquireRequest("Claude Code", "Cut the silence"), AgentUserState.Idle, _now);

        Assert.True(vm.IsAsking);
        Assert.True(vm.IsBannerVisible);
        Assert.Equal("wants to edit: Cut the silence", vm.HeadlineText);
        Assert.True(vm.AllowCommand.CanExecute(null));
        vm.DenyCommand.Execute(null);
        vm.AllowCommand.Execute(null);
        Assert.Equal(["deny", "allow"], controls.Calls);
    }

    [Fact]
    public void TheCallLogKeepsTheNewestFirstAndCapsAt500()
    {
        var (vm, _, _) = Create();
        Assert.False(vm.HasCalls);
        for (var i = 0; i < 505; i++)
        {
            vm.AddCall(new AuditEntry(DateTime.UtcNow, "Claude Code", "edit.split", i, i % 2 == 0 ? "ok" : "lock_required", i, "{\"frame\":" + i + "}"));
        }

        Assert.True(vm.HasCalls);
        Assert.Equal(AgentViewModel.MaxCalls, vm.Calls.Count);
        Assert.Equal(504, vm.Calls[0].Entry.DurationMs);
        Assert.False(vm.Calls[0].IsError);
        Assert.True(vm.Calls[1].IsError);
        Assert.Equal("504 ms", vm.Calls[0].DurationText);
        Assert.Equal("edit.split {\"frame\":504}", vm.Calls[0].ToolTip);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65_400, "1:05")]
    [InlineData(3_725_000, "1:02:05")]
    [InlineData(-5, "0:00")]
    public void DurationsUseMinutesAndSeconds(long ms, string text) => Assert.Equal(text, AgentViewModel.FormatDuration(ms));

    private string Acquire(AgentLock agentLock, string name, string reason)
    {
        var outcome = agentLock.Acquire(new AcquireRequest(name, reason), AgentUserState.Idle, _now);
        Assert.Equal(AcquireStatus.Granted, outcome.Status);
        return outcome.LockId!;
    }

    private (AgentViewModel Vm, AgentLock Lock, FakeControls Controls) Create()
    {
        var session = new ProjectSession(TimelineEdits.NewProject(new Cutlass.Tests.TestSupport.SequentialIdGenerator()), () => 0);
        var agentLock = new AgentLock(session);
        var vm = new AgentViewModel(() => _now);
        var controls = new FakeControls();
        vm.Controls = controls;
        agentLock.Changed += (_, state) => vm.Apply(state);
        return (vm, agentLock, controls);
    }

    private sealed class FakeActions : IAgentMenuActions
    {
        public List<string> Calls { get; } = [];

        public Task ToggleServerAsync()
        {
            Calls.Add("toggle");
            return Task.CompletedTask;
        }

        public Task CopyBriefingAsync(string? targetId)
        {
            Calls.Add("briefing " + (targetId ?? "last"));
            return Task.CompletedTask;
        }

        public Task ConnectClientsAsync()
        {
            Calls.Add("connect");
            return Task.CompletedTask;
        }

        public Task ShowAgentSettingsAsync()
        {
            Calls.Add("settings");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeControls : IAgentLockControls
    {
        public List<string> Calls { get; } = [];

        public bool TakeOver()
        {
            Calls.Add("takeover");
            return true;
        }

        public bool Pause()
        {
            Calls.Add("pause");
            return true;
        }

        public bool Resume()
        {
            Calls.Add("resume");
            return true;
        }

        public bool Answer(bool allow)
        {
            Calls.Add(allow ? "allow" : "deny");
            return true;
        }
    }
}
