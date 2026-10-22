using System.Globalization;
using Cutlass.App.Services.Agent;
using Cutlass.App.Services.Agent.Clients;
using Cutlass.App.ViewModels;

namespace Cutlass.App.Services;

/// <summary>
/// The Agent menu smoke commands (AGENT_INTEGRATION 6.6 and 9.4): <c>agent-briefing [http|target=&lt;id&gt;]</c> runs
/// Agent > Copy briefing (the plain item, a "Copy briefing for" item, or its HTTP only item) through the command
/// registry, takes the text the smoke dialog service put on the clipboard and checks that every placeholder is filled
/// and that it names the project and the target's tail (the tool names, the routes, the harness contract or the
/// client's registration); <c>menu-stop-mcp</c> and <c>menu-start-mcp</c> run Agent > Start/Stop MCP server the same
/// way (the smoke dialog service answers the first-start dialog with Start and registers nothing) and wait for the
/// server and the badge; <c>show-menu</c> opens a top-level menu in the window (and, with <c>submenu=</c>, a submenu
/// in it) and renders it into a PNG.
/// </summary>
public sealed partial class SmokeRunner
{
    private static readonly TimeSpan MenuWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Opens a top-level menu by its header (for example <c>Agent</c>) and the submenus given in turn (for example
    /// <c>Copy briefing for</c>), renders the window with the open menus into a PNG file and closes them (set by the app;
    /// the runner touches no controls). The arguments are the header, the PNG file and the submenu headers. Without it
    /// <c>show-menu</c> fails.
    /// </summary>
    public Func<string, string, IReadOnlyList<string>, Task>? CaptureMenu { get; init; }

    private async Task<bool> ExecuteMcpAsync(SmokeStep step)
    {
        switch (step.Command)
        {
            case "agent-briefing":
                await AgentBriefingAsync(step);
                return true;
            case "menu-start-mcp":
                await MenuServerAsync(start: true);
                return true;
            case "menu-stop-mcp":
                await MenuServerAsync(start: false);
                return true;
            case "show-menu":
                Expect(CaptureMenu is not null, "show-menu needs a menu capture");
                var shot = step.Option("screenshot") is { Length: > 0 } file ? ResolvePath(file) : throw Fail("show-menu needs screenshot=<file.png>");
                var submenus = step.Option("submenu") is { Length: > 0 } chain
                    ? chain.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : [];
                await CaptureMenu!(Arg(step, 0), shot, submenus);
                _log.Write("screenshot of the " + string.Join(" > ", [Arg(step, 0), .. submenus]) + " menu: " + shot);
                return true;
            default:
                return false;
        }
    }

    // agent-briefing [http|target=<id>] [save=<file>]: Agent > Copy briefing, checked on the clipboard text.
    private async Task AgentBriefingAsync(SmokeStep step)
    {
        Expect(_dialogs is not null, "agent-briefing needs the smoke dialog service");
        await _vm.EnsureClientsLoadedAsync();
        var targetId = step.Flag("http") ? MainWindowViewModel.HttpTarget : step.Option("target");
        var commandId = targetId is null ? CommandIds.AgentCopyBriefing
            : string.Equals(targetId, MainWindowViewModel.HttpTarget, StringComparison.OrdinalIgnoreCase) ? CommandIds.AgentCopyBriefingHttp
            : CommandIds.AgentCopyBriefingFor(targetId);
        var (profile, _, kind) = _vm.ResolveBriefingTarget(targetId);
        var before = _dialogs!.LastClipboardText;
        RunRegistered(commandId);
        var deadline = DateTime.UtcNow + MenuWait;
        while (ReferenceEquals(_dialogs.LastClipboardText, before) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        var text = _dialogs.LastClipboardText is { } copied && !ReferenceEquals(copied, before)
            ? copied
            : throw Fail("the briefing did not reach the clipboard");
        var unfilled = AgentBriefing.Unfilled(text);
        Expect(unfilled.Count == 0, "the briefing still holds " + string.Join(", ", unfilled));
        Expect(kind != AgentClientBriefingKind.Generic || !text.Contains('{', StringComparison.Ordinal), "the generic briefing still holds a '{'");

        // An unsaved project is described as "an untitled project" (AgentBriefing.ProjectText); a saved one by its quoted name.
        var namesProject = _vm.ProjectPath is null
            ? text.Contains("an untitled project", StringComparison.Ordinal)
            : text.Contains("\"" + _vm.ProjectName + "\"", StringComparison.Ordinal);
        Expect(namesProject, "the briefing does not name the project " + _vm.ProjectName);
        var url = AgentServer?.BaseUrl ?? _vm.ClientContext().Url;
        string[] tools = ["cutlass_status", "cutlass_acquire_lock", "cutlass_describe_project", "cutlass_begin_batch", "cutlass_render_frame", "cutlass_release_lock"];
        IEnumerable<string> steps = kind switch
        {
            AgentClientBriefingKind.Http => ["v1/status", "v1/lock/acquire", "v1/project/describe", "v1/edit/place", "v1/frame/", "v1/lock/release", "export CUTLASS_TOKEN='", url + "v1/status", url + "v1/call"],
            AgentClientBriefingKind.Harness => [.. tools, url + "v1/call", url + "v1/tools?format=" + profile!.ToolsFormat, url + "v1/instructions", profile.Example ?? "example"],
            AgentClientBriefingKind.Mcp when _vm.ClientRegistry is { } registry => [.. tools, profile!.Name, AgentBriefing.Indent(registry.RegistrationText(profile, _vm.ClientContext()))],
            _ => [.. tools, url + "v1/instructions", "Connect an agent client"],
        };
        foreach (var expected in steps)
        {
            Expect(text.Contains(expected, StringComparison.Ordinal), "the briefing does not name " + expected);
        }

        if (step.Option("save") is { Length: > 0 } save)
        {
            var path = ResolvePath(save);

            // The smoke dialog service confirmed the token; the saved copy masks it.
            var token = AgentServer is { } server ? await server.ReadTokenAsync(CancellationToken.None) : null;
            var masked = token is { Length: > 0 } ? text.Replace(token, "<token>", StringComparison.Ordinal) : text;
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, masked);
            });
            _log.Write("saved the briefing to " + path);
        }

        _log.Write(string.Create(CultureInfo.InvariantCulture, $"agent-briefing {targetId ?? "(last registered or generic)"}: {kind} tail{(profile is null ? string.Empty : " for " + profile.Name)}, {text.Split('\n').Length} lines, every placeholder filled"));
    }

    // menu-start-mcp / menu-stop-mcp: Agent > Start/Stop MCP server through RunCommand, then the server and the badge.
    private async Task MenuServerAsync(bool start)
    {
        var server = AgentServer ?? throw Fail("no agent server in this run");
        Expect(server.IsSwitchedOn != start, start ? "the MCP server is already on" : "the MCP server is already off");
        Expect(_vm.Agent.ServerMenuText == (start ? "_Start MCP server" : "_Stop MCP server"), "the Agent menu reads " + _vm.Agent.ServerMenuText);
        Expect(_vm.RunCommandCommand.CanExecute(CommandIds.AgentToggleServer), "Agent > Start/Stop MCP server is disabled");
        _vm.RunCommandCommand.Execute(CommandIds.AgentToggleServer);
        var deadline = DateTime.UtcNow + MenuWait;
        bool Done() => start
            ? server.Status is AgentServerStatus.Listening or AgentServerStatus.PortInUse or AgentServerStatus.Error
            : server.Status == AgentServerStatus.Off;
        while (!Done() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Expect(Done(), "the server did not " + (start ? "start" : "stop") + "; it is " + server.Status + ": " + server.StatusText);
        Expect(!start || server.Status == AgentServerStatus.Listening, "the server did not start: " + server.StatusText);
        await _vm.Agent.RefreshExeAsync();
        var badge = !start ? "MCP off" : File.Exists(server.McpExecutablePath) ? "MCP ready" : "MCP: exe missing";
        Expect(_vm.Agent.BadgeText == badge, "the badge reads " + _vm.Agent.BadgeText + ", expected " + badge);
        Expect(_vm.Agent.ServerMenuText == (start ? "_Stop MCP server" : "_Start MCP server"), "the Agent menu still reads " + _vm.Agent.ServerMenuText);
        _log.Write((start ? "menu-start-mcp: " : "menu-stop-mcp: ") + server.StatusText + " | badge " + _vm.Agent.BadgeText + " | clients " + _vm.Agent.ClientsText);
    }
}
