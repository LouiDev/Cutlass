using Cutlass.App.Services.Agent.Clients;
using Cutlass.App.ViewModels;
using Cutlass.Tests.App.Shell;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// AGENT_INTEGRATION 9.4: the client list orders detected clients first, then the others, then the text-only rows;
/// each row offers what fits its state (Register, Update, Remove, Copy), a foreign entry and a removal are confirmed,
/// a copied HTTP text carries the token only after the confirmation, the summary feeds the panel's "Clients" line,
/// and the discovery URLs are listed with Copy. Every client lives in a fake home.
/// </summary>
public sealed class ConnectClientsViewModelTests : IDisposable
{
    private const string ExeJson = "C:\\\\Program Files\\\\Cutlass\\\\Cutlass.Agent.Mcp.exe";
    private readonly ClientSandbox _box = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly ConnectClientsViewModel _clients;

    /// <summary>Creates the list on a fake home.</summary>
    public ConnectClientsViewModelTests()
    {
        _clients = new ConnectClientsViewModel(_box.Registry, _dialogs, () => _box.Context, () => Task.FromResult<string?>("tok_secret"));
    }

    /// <summary>Removes the fake home.</summary>
    public void Dispose() => _box.Dispose();

    [Fact]
    public async Task DetectedClientsComeFirstAndTextRowsLast()
    {
        await _clients.LoadAsync();
        Assert.Equal(12, _clients.Items.Count);
        Assert.Equal("not checked", _clients.SummaryText);
        Assert.Equal("copy only", _clients.Items[^1].StateText);

        Directory.CreateDirectory(Path.Combine(_box.Home, ".codex"));
        _box.Write(".cursor/mcp.json", "{ \"mcpServers\": { \"cutlass\": { \"command\": \"" + ExeJson + "\", \"args\": [] } } }");
        await _clients.RefreshAsync(force: true);

        Assert.Equal(
            ["cursor", "codex-cli", "claude-code", "claude-desktop", "windsurf", "vscode", "gemini-cli", "continue", "zed", "openai-compatible", "anthropic-sdk", "http"],
            _clients.Items.Select(i => i.Id));
        Assert.Equal("1 current", _clients.SummaryText);
        Assert.Equal(1, _clients.CurrentCount);
        Assert.Equal("Cursor: current\nCodex CLI: not registered", _clients.SummaryToolTip);

        var cursor = _clients.Items[0];
        Assert.True(cursor.IsCurrent);
        Assert.False(cursor.ShowPrimary);
        Assert.True(cursor.ShowRemove);
        var codex = _clients.Items[1];
        Assert.True(codex.ShowPrimary);
        Assert.Equal("Register", codex.PrimaryText);
        Assert.False(codex.ShowRemove);
        var desktop = _clients.Items[3];
        Assert.Equal("not installed", desktop.StateText);
        Assert.False(desktop.ShowPrimary);
        Assert.False(desktop.ShowRemove);
        var http = _clients.Items[^1];
        Assert.True(http.IsTextOnly);
        Assert.False(http.ShowPrimary);
        Assert.True(http.CopyCommand.CanExecute(null));
        Assert.Equal("copy the setup text (HTTP)", http.WhereText);
        Assert.Equal("via the claude command line tool", _clients.Items[2].WhereText);
        Assert.False(_clients.Items[2].HasScopes); // Not installed: nothing to register, so no scope choice.
        Assert.False(cursor.HasScopes);

        // The panel's "Clients" line follows the summary.
        var agent = new AgentViewModel(() => 0) { Clients = _clients };
        Assert.Equal("1 current", agent.ClientsText);
        Assert.True(agent.HasCurrentClients);
        Assert.Contains("Cursor: current", agent.ClientsToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterRemoveAndAForeignEntryAskFirst()
    {
        Directory.CreateDirectory(Path.Combine(_box.Home, ".cursor"));
        await _clients.RefreshAsync(force: true);
        var cursor = _clients.Items.Single(i => i.Id == "cursor");
        string? registered = null;
        _clients.Registered += (_, id) => registered = id;

        await cursor.PrimaryCommand.ExecuteAsync(null);
        Assert.True(cursor.IsCurrent);
        Assert.Equal("cursor", registered);
        Assert.Contains("Registered with Cursor.", _clients.OutputText, StringComparison.Ordinal);
        Assert.Equal("1 current", _clients.SummaryText);
        Assert.Empty(_dialogs.Confirmations);

        // Remove asks; no keeps the entry.
        _dialogs.ConfirmAnswer = false;
        await cursor.RemoveCommand.ExecuteAsync(null);
        Assert.True(cursor.IsCurrent);
        Assert.StartsWith("Remove the cutlass entry: Remove the cutlass entry from Cursor?", _dialogs.Confirmations[^1], StringComparison.Ordinal);
        _dialogs.ConfirmAnswer = true;
        await cursor.RemoveCommand.ExecuteAsync(null);
        Assert.Equal(AgentClientState.NotRegistered, cursor.Probe.State);
        Assert.Equal("none current", _clients.SummaryText);

        // A foreign entry is replaced only after a yes.
        _box.Write(".cursor/mcp.json", "{ \"mcpServers\": { \"cutlass\": { \"command\": \"npx\", \"args\": [\"other\"] } } }");
        await _clients.RefreshAsync(force: true);
        Assert.True(cursor.IsWarning);
        Assert.Equal("Update", cursor.PrimaryText);
        _dialogs.ConfirmAnswer = false;
        await cursor.PrimaryCommand.ExecuteAsync(null);
        Assert.Equal(AgentClientState.Foreign, cursor.Probe.State);
        Assert.Contains("did not write (npx other)", _dialogs.Confirmations[^1], StringComparison.Ordinal);
        _dialogs.ConfirmAnswer = true;
        await cursor.PrimaryCommand.ExecuteAsync(null);
        Assert.True(cursor.IsCurrent);
    }

    [Fact]
    public async Task CopyPutsTheSetupOnTheClipboardWithTheTokenOnlyAfterConfirming()
    {
        await _clients.RefreshAsync(force: true);
        var cursor = _clients.Items.Single(i => i.Id == "cursor");
        await cursor.CopyCommand.ExecuteAsync(null);
        Assert.StartsWith("Add to ", _dialogs.Clipboard[^1], StringComparison.Ordinal);
        Assert.Empty(_dialogs.Confirmations);

        var openai = _clients.Items.Single(i => i.Id == "openai-compatible");
        _dialogs.ConfirmAnswer = false;
        await openai.CopyCommand.ExecuteAsync(null);
        Assert.Contains("Token: the content of " + _box.Context.TokenFile, _dialogs.Clipboard[^1], StringComparison.Ordinal);
        Assert.StartsWith("Copy the setup for OpenAI-compatible harness: Put the agent token", _dialogs.Confirmations[^1], StringComparison.Ordinal);
        _dialogs.ConfirmAnswer = true;
        await openai.CopyCommand.ExecuteAsync(null);
        Assert.Contains("Token: tok_secret", _dialogs.Clipboard[^1], StringComparison.Ordinal);
        Assert.StartsWith("Copied the setup for OpenAI-compatible harness", _clients.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDiscoveryUrlsAreListedWithCopy()
    {
        await _clients.LoadAsync();
        Assert.Equal(
            [
                "http://localhost:47390/v1/tools?format=openai", "http://localhost:47390/v1/tools?format=anthropic",
                "http://localhost:47390/v1/tools?format=gemini", "http://localhost:47390/v1/tools?format=markdown",
                "http://localhost:47390/v1/openapi.json", "http://localhost:47390/v1/instructions",
            ],
            _clients.DiscoveryUrls.Select(u => u.Url));
        Assert.Equal("Tools (openai)", _clients.DiscoveryUrls[0].Label);
        await _clients.DiscoveryUrls[4].CopyCommand.ExecuteAsync(null);
        Assert.Equal("http://localhost:47390/v1/openapi.json", _dialogs.Clipboard[^1]);

        var closed = 0;
        _clients.CloseRequested += (_, _) => closed++;
        _clients.CloseCommand.Execute(null);
        Assert.Equal(1, closed);
    }
}
