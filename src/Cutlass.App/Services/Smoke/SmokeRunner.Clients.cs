using System.Globalization;
using Cutlass.App.Services.Agent.Clients;
using Cutlass.App.ViewModels.Agent;

namespace Cutlass.App.Services.Smoke;

/// <summary>
/// The agent client smoke commands (AGENT_INTEGRATION 9.3 and 9.4). A smoke run's client registry is always a sandbox
/// (a fake home and a CLI runner that starts nothing, set up by the app), so these commands never touch a real client
/// configuration. <c>settings agent-home=&lt;dir&gt;</c> replaces the sandbox with an empty fake home inside the
/// script folder and sets <c>$clienthome</c> and <c>$clientconfig</c> (the fake per-user configuration folder);
/// <c>write-file</c> seeds client files there; <c>agent-clients</c> checks every client and compares states;
/// <c>agent-client</c> runs Register, Update or Remove on one row.
/// </summary>
public sealed partial class SmokeRunner
{
    private async Task<bool> ExecuteClientsAsync(SmokeStep step)
    {
        switch (step.Command)
        {
            case "write-file":
                await WriteFileAsync(step);
                return true;
            case "agent-clients":
                await AgentClientsAsync(step);
                return true;
            case "agent-client":
                await AgentClientAsync(step);
                return true;
            default:
                return false;
        }
    }

    // settings agent-home=<dir>: an empty sandbox home inside the script folder for the client registry.
    private async Task UseAgentHomeAsync(string dir)
    {
        var home = ResolvePath(dir);
        var root = Path.GetFullPath(_baseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Expect(home.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "agent-home must be a folder inside the script folder, not " + home);
        await Task.Run(() =>
        {
            if (Directory.Exists(home))
            {
                Directory.Delete(home, recursive: true);
            }

            Directory.CreateDirectory(home);
        });
        var registry = AgentClientRegistry.ForSandbox(home, new SmokeCliRunner(_log.Write));
        _vm.ClientRegistry = registry;
        await _vm.EnsureClientsLoadedAsync();
        _agentVars["clienthome"] = registry.Environment.Home.Replace('\\', '/');
        _agentVars["clientconfig"] = registry.Environment.AppData.Replace('\\', '/');
        _log.Write("agent clients: sandbox home " + registry.Environment.Home + " (" + registry.Profiles.Count.ToString(CultureInfo.InvariantCulture) + " profiles)");
    }

    // write-file <path> <text> [json]: \n in the text is a line end, $var a saved value; json turns single quotes into double ones.
    private async Task WriteFileAsync(SmokeStep step)
    {
        // The text is the second argument as it is: it may hold '=' (TOML), which Positional() would drop.
        Expect(step.Args.Count >= 2, "write-file needs <path> <text>");
        var path = ResolvePath(Substitute(step.Args[0]));
        var text = step.Flag("json")
            ? ToJson(step.Args[1], _agentVars)
            : Substitute(step.Args[1]).Replace("\\n", "\n", StringComparison.Ordinal);
        await Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        });
        _log.Write("wrote " + path + " (" + text.Length.ToString(CultureInfo.InvariantCulture) + " characters)");
    }

    // agent-clients [expect=<id>:<state>,...]: checks every client again and logs the rows in their order.
    private async Task AgentClientsAsync(SmokeStep step)
    {
        var clients = _vm.Clients ?? throw Fail("no agent client list in this run");
        await clients.RefreshAsync(force: true);
        foreach (var row in clients.Items)
        {
            _log.Write("client " + row.Id + ": " + row.StateText + (row.Probe.Detail.Length > 0 ? " (" + row.Probe.Detail + ")" : string.Empty));
        }

        _log.Write("clients: " + clients.SummaryText + " | panel: " + _vm.Agent.ClientsText);
        foreach (var pair in (step.Option("expect") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = pair.IndexOf(':', StringComparison.Ordinal);
            Expect(colon > 0, "expect= takes <id>:<state> pairs, not " + pair);
            ExpectState(Row(clients, pair[..colon]), pair[(colon + 1)..]);
        }

        if (step.Option("current") is { } current)
        {
            Expect(clients.CurrentCount.ToString(CultureInfo.InvariantCulture) == current, "clients current: " + clients.CurrentCount.ToString(CultureInfo.InvariantCulture) + ", expected " + current);
        }
    }

    // agent-client <id> register|update|remove [expect=<state>]: the row's buttons (the smoke dialog service confirms).
    private async Task AgentClientAsync(SmokeStep step)
    {
        var clients = _vm.Clients ?? throw Fail("no agent client list in this run");
        await clients.RefreshAsync(force: false);
        var row = Row(clients, Arg(step, 0));
        var action = Arg(step, 1).ToLowerInvariant();
        switch (action)
        {
            case "register" or "update":
                Expect(row.PrimaryCommand.CanExecute(null) && row.ShowPrimary, row.Id + " offers no " + action + " (it is " + row.StateText + ")");
                await row.PrimaryCommand.ExecuteAsync(null);
                break;
            case "remove":
                Expect(row.RemoveCommand.CanExecute(null) && row.ShowRemove, row.Id + " offers no remove (it is " + row.StateText + ")");
                await row.RemoveCommand.ExecuteAsync(null);
                break;
            default:
                throw Fail("agent-client takes register, update or remove, not " + action);
        }

        _log.Write("agent-client " + row.Id + " " + action + ": " + row.StateText + " | " + clients.OutputText.Replace('\n', ' '));
        if (step.Option("expect") is { } expected)
        {
            ExpectState(row, expected);
        }
    }

    private AgentClientItemViewModel Row(ConnectClientsViewModel clients, string id) =>
        clients.Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)) ?? throw Fail("no agent client " + id);

    private void ExpectState(AgentClientItemViewModel row, string expected)
    {
        Expect(
            Enum.TryParse<AgentClientState>(expected.Replace("-", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var state),
            "unknown client state " + expected);
        Expect(row.Probe.State == state, "client " + row.Id + " is " + row.Probe.State + " (" + row.Probe.Detail + "), expected " + state);
    }

    private string Substitute(string text)
    {
        foreach (var (name, value) in _agentVars.OrderByDescending(p => p.Key.Length))
        {
            text = text.Replace("$" + name, value, StringComparison.Ordinal);
        }

        return text;
    }
}
