using Cutlass.App.Services;
using Cutlass.App.ViewModels.Agent;
using Cutlass.Tests.App.Shell;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// AGENT_INTEGRATION 6.6 and 9.4 (MCP_HOSTING_CONCEPT 4.4): the first-start dialog's defaults, the "Register with"
/// dropdown (the detected clients plus none, the first one that misses its registration chosen), the exact command or
/// configuration entry it shows (with the scope for a CLI with scopes), Start with none, Start with a client (the
/// output in the dialog, then Close) and the dialog without any detected client. Every client lives in a fake home.
/// </summary>
public sealed class McpStartDialogTests : IDisposable
{
    private const string Exe = ClientSandbox.Exe;
    private readonly ClientSandbox _box;
    private readonly ConnectClientsViewModel _clients;
    private bool _registered;

    /// <summary>Creates the client list on a fake home with a fake CLI.</summary>
    public McpStartDialogTests()
    {
        _box = new ClientSandbox(args => args[1] switch
        {
            "add" => Registered(true),
            "get" when _registered => new CliResult(0, "cutlass:\n  Scope: User config\n  Command: " + Exe + "\n", string.Empty),
            _ => new CliResult(1, string.Empty, "No MCP server named \"cutlass\"."),
        });
        _clients = new ConnectClientsViewModel(_box.Registry, new FakeDialogService(), () => _box.Context, () => Task.FromResult<string?>("tok"));
    }

    /// <summary>Removes the fake home.</summary>
    public void Dispose() => _box.Dispose();

    [Fact]
    public async Task TheDefaultsFollowTheSpec()
    {
        Directory.CreateDirectory(Path.Combine(_box.Home, ".cursor"));
        _box.InstallCommand("claude");
        await _clients.RefreshAsync(force: true);
        var dialog = new McpStartDialogViewModel(_clients, "/home/me/edits");

        Assert.True(dialog.AllowForSession);
        Assert.False(dialog.KeepOn);
        Assert.Equal("Start the MCP server", McpStartDialogViewModel.Title);
        Assert.Equal("Start", dialog.PrimaryText);
        Assert.Equal(["Claude Code (not registered)", "Cursor (not registered)", "none"], dialog.Choices.Select(c => c.DisplayText));
        Assert.Equal("Claude Code", dialog.SelectedChoice.Item!.Name);
        Assert.True(dialog.RegisterNow);
        Assert.True(dialog.HasScopes);
        Assert.Equal(["user", "local"], dialog.Scopes);
        Assert.Equal("user", dialog.SelectedScope);
        Assert.Equal("Registering runs:", dialog.CommandHeader);
        Assert.Equal("claude mcp add cutlass --scope user -- \"" + Exe + "\"", dialog.CommandText);
        dialog.SelectedScope = "local";
        Assert.Equal("claude mcp add cutlass --scope local -- \"" + Exe + "\"", dialog.CommandText);
        Assert.Equal("/home/me/edits", dialog.LocalFolderText);
        Assert.Equal("Claude Code: not registered", dialog.StatusText);

        dialog.SelectedChoice = dialog.Choices[1];
        Assert.False(dialog.HasScopes);
        Assert.Equal("Registering writes:", dialog.CommandHeader);
        Assert.StartsWith("Add to " + Path.GetFullPath(Path.Combine(_box.Home, ".cursor", "mcp.json")), dialog.CommandText, StringComparison.Ordinal);

        dialog.SelectedChoice = McpClientChoice.None;
        Assert.False(dialog.RegisterNow);
        Assert.StartsWith("Nothing is registered now", dialog.CommandText, StringComparison.Ordinal);
        Assert.Equal(string.Empty, dialog.StatusText);
    }

    [Fact]
    public async Task StartWithNoneClosesAtOnce()
    {
        Directory.CreateDirectory(Path.Combine(_box.Home, ".cursor"));
        await _clients.RefreshAsync(force: true);
        var dialog = new McpStartDialogViewModel(_clients, null) { SelectedChoice = McpClientChoice.None };
        bool? closed = null;
        dialog.CloseRequested += (_, ok) => closed = ok;
        await dialog.PrimaryCommand.ExecuteAsync(null);
        Assert.True(closed);
        Assert.False(File.Exists(Path.Combine(_box.Home, ".cursor", "mcp.json")));
    }

    [Fact]
    public async Task StartWithAClientShowsTheOutputThenCloses()
    {
        _box.InstallCommand("claude");
        await _clients.RefreshAsync(force: true);
        var dialog = new McpStartDialogViewModel(_clients, "/home/me");
        bool? closed = null;
        dialog.CloseRequested += (_, ok) => closed = ok;

        await dialog.PrimaryCommand.ExecuteAsync(null);

        Assert.Null(closed);
        Assert.True(dialog.IsDone);
        Assert.True(dialog.Registered);
        Assert.False(dialog.ShowCancel);
        Assert.Equal("Close", dialog.PrimaryText);
        Assert.True(dialog.HasOutput);
        Assert.Contains("> claude mcp add cutlass --scope user -- \"" + Exe + "\"", dialog.OutputText, StringComparison.Ordinal);
        Assert.Contains("Registered with Claude Code.", dialog.OutputText, StringComparison.Ordinal);
        Assert.Equal("Claude Code: current (user scope)", dialog.StatusText);
        Assert.Equal("claude-code", _box.Registry.LastRegisteredClientId);
        Assert.Equal("1 current", _clients.SummaryText);

        await dialog.PrimaryCommand.ExecuteAsync(null);
        Assert.True(closed);
    }

    [Fact]
    public async Task WithoutADetectedClientOnlyNoneIsOffered()
    {
        await _clients.RefreshAsync(force: true);
        var dialog = new McpStartDialogViewModel(_clients, null);
        Assert.False(dialog.HasDetectedClients);
        Assert.Equal(["none"], dialog.Choices.Select(c => c.DisplayText));
        Assert.StartsWith("No agent client was detected", dialog.CommandText, StringComparison.Ordinal);

        var unavailable = new McpStartDialogViewModel(null, null);
        Assert.Single(unavailable.Choices);
        bool? closed = null;
        unavailable.CloseRequested += (_, ok) => closed = ok;
        unavailable.CancelCommand.Execute(null);
        Assert.False(closed);
    }

    private CliResult Registered(bool value)
    {
        _registered = value;
        return new CliResult(0, "Added stdio MCP server cutlass with command: " + Exe + " to user config", string.Empty);
    }
}
