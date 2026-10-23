using Cutlass.App.Services;
using Cutlass.App.Services.Agent;
using Cutlass.App.Services.Ffmpeg;
using Cutlass.App.ViewModels.Dialogs;
using Cutlass.Media.Settings;
using Cutlass.Tests.App.Shell;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// AGENT_INTEGRATION 6.1: the Settings > Agents page loads the agent settings, validates them in tab order after
/// Playback, applies them with the other pages, edits the folder lists, shows the server status, masks, copies and
/// regenerates the token, and fills the MCP setup text with the real exe path.
/// </summary>
public sealed class SettingsAgentsPageTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "cutlass-agent-settings-tests");

    [Fact]
    public void ThePageIsTheFourthTabBeforeAbout()
    {
        Assert.Equal(3, (int)SettingsPage.Agents);
        Assert.Equal(4, (int)SettingsPage.About);
        var vm = new SettingsViewModel(new Host(new AppSettings()), new FakeDialogService(), page: SettingsPage.Agents);
        Assert.Equal(3, vm.SelectedPageIndex);
    }

    [Fact]
    public async Task TheAgentSettingsRoundTripThroughApply()
    {
        var host = new Host(new AppSettings
        {
            AgentConnectionsEnabled = true,
            AgentPort = 47400,
            AgentLeaseSeconds = 120,
            AgentMaxLeaseSeconds = 240,
            AgentAskFirst = true,
            AgentReadableFolders = [Path.Combine(Root, "footage")],
            AgentWritableFolders = [Path.Combine(Root, "renders")],
        });
        var vm = new SettingsViewModel(host, new FakeDialogService());
        Assert.True(vm.AllowAgentConnections);
        Assert.Equal("47400", vm.AgentPortText);
        Assert.Equal("120", vm.AgentLeaseText);
        Assert.Equal("240", vm.AgentMaxLeaseText);
        Assert.True(vm.AgentAskFirst);
        Assert.Equal([Path.Combine(Root, "footage")], vm.AgentReadFolders);
        Assert.Equal([Path.Combine(Root, "renders")], vm.AgentWriteFolders);

        vm.AllowAgentConnections = false;
        vm.AgentPortText = "47391";
        vm.AgentLeaseText = "60";
        vm.AgentAskFirst = false;
        vm.SelectedReadFolder = vm.AgentReadFolders[0];
        vm.RemoveReadFolderCommand.Execute(null);
        Assert.True(await vm.ApplyChangesAsync());

        var applied = Assert.Single(host.Applied);
        Assert.False(applied.AgentConnectionsEnabled);
        Assert.Equal(47391, applied.AgentPort);
        Assert.Equal(60, applied.AgentLeaseSeconds);
        Assert.Equal(240, applied.AgentMaxLeaseSeconds);
        Assert.False(applied.AgentAskFirst);
        Assert.Empty(applied.AgentReadableFolders);
        Assert.Equal([Path.Combine(Root, "renders")], applied.AgentWritableFolders);
        Assert.Equal(applied.AutosaveIntervalMinutes, host.Settings.AutosaveIntervalMinutes);
    }

    [Theory]
    [UseCulture("de-DE")]
    [InlineData("port", "80", "Agent port: enter a whole number from 1024 to 65535.")]
    [InlineData("port", "65536", "Agent port: enter a whole number from 1024 to 65535.")]
    [InlineData("lease", "14", "Agent lease: enter whole seconds from 15 to 600.")]
    [InlineData("lease", "1.5", "Agent lease: enter whole seconds from 15 to 600.")]
    [InlineData("max", "601", "Longest agent lease: enter whole seconds from 15 to 600.")]
    public void InvalidAgentFieldsBlockApply(string field, string text, string message)
    {
        var vm = new SettingsViewModel(new Host(new AppSettings()), new FakeDialogService());
        Assert.Null(vm.ProblemText);
        switch (field)
        {
            case "port": vm.AgentPortText = text; break;
            case "lease": vm.AgentLeaseText = text; break;
            default: vm.AgentMaxLeaseText = text; break;
        }

        Assert.Equal(message, vm.ProblemText);
        Assert.False(vm.OkCommand.CanExecute(null));
        Assert.Null(vm.BuildSettings());
    }

    [Fact]
    public async Task FoldersAreAddedOnceAsFullPaths()
    {
        var dialogs = new FakeDialogService { NextFolder = Path.Combine(Root, "out") };
        var vm = new SettingsViewModel(new Host(new AppSettings()), dialogs);
        await vm.AddWriteFolderCommand.ExecuteAsync(null);
        await vm.AddWriteFolderCommand.ExecuteAsync(null);
        Assert.Equal([Path.GetFullPath(Path.Combine(Root, "out"))], vm.AgentWriteFolders);
        Assert.False(vm.RemoveWriteFolderCommand.CanExecute(null));
        vm.SelectedWriteFolder = vm.AgentWriteFolders[0];
        Assert.True(vm.RemoveWriteFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task TheTokenIsMaskedCopiedAndRegeneratedAfterConfirming()
    {
        var agents = new AgentHost();
        var dialogs = new FakeDialogService();
        var vm = new SettingsViewModel(new Host(new AppSettings()), dialogs, agents: agents);
        await vm.LoadAgentsAsync();
        Assert.Equal(new string('*', 16) + "WXYZ", vm.AgentTokenText);
        Assert.DoesNotContain("abcdefghij", vm.AgentTokenText, StringComparison.Ordinal);

        await vm.CopyTokenCommand.ExecuteAsync(null);
        Assert.Equal("Copied.", vm.AgentTokenNote);

        dialogs.ConfirmAnswer = false;
        await vm.RegenerateTokenCommand.ExecuteAsync(null);
        Assert.Equal(0, agents.Regenerated);

        dialogs.ConfirmAnswer = true;
        await vm.RegenerateTokenCommand.ExecuteAsync(null);
        Assert.Equal(1, agents.Regenerated);
        Assert.EndsWith("NEW1", vm.AgentTokenText, StringComparison.Ordinal);
        Assert.Equal("Regenerated.", vm.AgentTokenNote);
    }

    [Fact]
    public void TheStatusLineAndTheMcpTextFollowTheServer()
    {
        var agents = new AgentHost { Status = AgentServerStatus.Listening, StatusText = "Listening on http://localhost:47390/" };
        var vm = new SettingsViewModel(new Host(new AppSettings()), new FakeDialogService(), agents: agents);
        Assert.Equal("Listening on http://localhost:47390/", vm.AgentStatusText);
        Assert.True(vm.IsAgentListening);
        Assert.False(vm.IsAgentError);
        Assert.Equal("C:\\Program Files\\Cutlass\\Cutlass.Agent.Mcp.exe", vm.McpPathText);
        Assert.False(vm.HasClients);
        Assert.Null(vm.Clients);

        agents.Status = AgentServerStatus.PortInUse;
        agents.StatusText = "Port 47390 is in use";
        agents.RaiseStatusChanged();
        Assert.Equal("Port 47390 is in use", vm.AgentStatusText);
        Assert.True(vm.IsAgentError);
        Assert.False(vm.IsAgentListening);
        Assert.False(vm.IsAgentForcedOn);
    }

    [Fact]
    public void ForcedOnByTheCommandLineKeepsTheSavedValue()
    {
        var agents = new AgentHost { Forced = true };
        var host = new Host(new AppSettings { AgentConnectionsEnabled = false });
        var vm = new SettingsViewModel(host, new FakeDialogService(), agents: agents);
        Assert.True(vm.IsAgentForcedOn);
        Assert.False(vm.AllowAgentConnections);
        Assert.StartsWith("Started with --agent-server", vm.AgentForcedNote, StringComparison.Ordinal);
        Assert.False(vm.BuildSettings()!.AgentConnectionsEnabled);
    }

    private sealed class Host(AppSettings settings) : ISettingsHost
    {
        public AppSettings Settings { get; private set; } = settings;

        public List<AppSettings> Applied { get; } = [];

        public string CacheFolder => Path.Combine(Root, "cache");

        public bool IsFfmpegReady => true;

        public string FfmpegSummary => "N-1 (libavfilter 12.0), ffmpeg";

        public Task<FfmpegCheck> CheckFfmpegAsync(string? path, CancellationToken cancellationToken) =>
            Task.FromResult(new FfmpegCheck(true, "found", path));

        public Task<long> MeasureCacheAsync(CancellationToken cancellationToken) => Task.FromResult(0L);

        public Task<long> ClearCacheAsync(CancellationToken cancellationToken) => Task.FromResult(0L);

        public Task<IReadOnlyList<string>> ListAudioDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Applied.Add(settings);
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class AgentHost : IAgentSettingsHost
    {
        private string _token = "abcdefghijklmnopqrstuvwxyz0123456789WXYZ";

        public event EventHandler? StatusChanged;

        public AgentServerStatus Status { get; set; } = AgentServerStatus.Off;

        public string StatusText { get; set; } = "Off";

        public bool Forced { get; init; }

        public bool IsForcedOn => Forced;

        public string McpExecutablePath => "C:\\Program Files\\Cutlass\\Cutlass.Agent.Mcp.exe";

        public string AgentDocsPath => Path.Combine(Root, "AGENT_INTEGRATION.md");

        public int Regenerated { get; private set; }

        public void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

        public Task<string> ReadTokenAsync(CancellationToken cancellationToken) => Task.FromResult(_token);

        public Task<string> RegenerateTokenAsync(CancellationToken cancellationToken)
        {
            Regenerated++;
            _token = "zyxwvutsrqponmlkjihgfedcba9876543210NEW1";
            return Task.FromResult(_token);
        }
    }
}
