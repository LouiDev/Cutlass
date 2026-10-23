using System.Text.Json;
using Cutlass.App.Services;
using Cutlass.App.Services.Agent.Clients;
using Cutlass.App.Services.Agent.Clients.Profiles;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// AGENT_INTEGRATION 9.3: the shipped profiles load (and the embedded copy matches the file), user profiles merge by
/// id, detection follows the fake PATH and the fake home, the placeholders and the dropped empty options, the
/// registration text of each kind, and the CLI kind end to end against a fake CLI (the real <c>claude mcp get</c>
/// output shapes: current in user scope, stale in local scope, not registered, the CLI missing, a list fallback,
/// register with the stale entry removed from its scope first, remove). Everything runs in a fake home.
/// </summary>
public sealed class AgentClientRegistryTests
{
    private const string Exe = ClientSandbox.Exe;

    // The shape of claude 2.1 output for a registered stdio server.
    private const string GetUser = "cutlass:\n  Scope: User config (available in all your projects)\n  Status: \u2714 Connected\n  Type: stdio\n  Command: " + Exe + "\n  Args: \n  Environment:\n\nTo remove this server, run: claude mcp remove cutlass -s user\n";

    private const string GetLocalStale = "cutlass:\r\n  Scope: Local config (private to you in this project)\r\n  Status: \u2714 Connected\r\n  Type: stdio\r\n  Command: C:\\Users\\me\\old\\Cutlass.Agent.Mcp.exe\r\n  Args:\r\n";

    private const string Missing = "No MCP server named \"cutlass\". Configured servers: claude.ai Claude Docs";

    [Fact]
    public async Task TheShippedProfilesLoadInOrderAndTheEmbeddedCopyMatches()
    {
        using var box = new ClientSandbox();
        var profiles = await box.Registry.LoadAsync(CancellationToken.None);
        Assert.Equal(
            ["claude-code", "claude-desktop", "cursor", "windsurf", "vscode", "gemini-cli", "codex-cli", "continue", "zed", "openai-compatible", "anthropic-sdk", "http"],
            profiles.Select(p => p.Id));
        Assert.Empty(box.Registry.LoadProblems);
        Assert.Equal(["openai", "anthropic", "gemini", "markdown"], box.Registry.ToolFormats);

        Assert.Equal(AgentClientKind.Cli, box.Registry.Find("claude-code")!.Kind);
        Assert.Equal(["user", "local"], box.Registry.Find("claude-code")!.Scopes);
        Assert.Equal(AgentClientKind.ConfigFile, box.Registry.Find("cursor")!.Kind);
        Assert.True(box.Registry.Find("codex-cli")!.IsToml);
        Assert.False(box.Registry.Find("cursor")!.IsToml);
        foreach (var id in new[] { "openai-compatible", "anthropic-sdk", "http" })
        {
            var profile = box.Registry.Find(id)!;
            Assert.True(profile.IsTextOnly, id);
            Assert.True(profile.UsesHttp, id);
        }

        Assert.Equal(AgentClientBriefingKind.Mcp, box.Registry.Find("zed")!.BriefingKind);
        Assert.Equal(AgentClientBriefingKind.Harness, box.Registry.Find("openai-compatible")!.BriefingKind);
        Assert.Equal("openai", box.Registry.Find("openai-compatible")!.ToolsFormat);
        Assert.Equal(AgentClientBriefingKind.Http, box.Registry.Find("http")!.BriefingKind);

        // The Python harnesses take the task as --task (build/examples), and the text names the example it runs.
        foreach (var id in new[] { "openai-compatible", "anthropic-sdk" })
        {
            var profile = box.Registry.Find(id)!;
            Assert.Contains("python " + profile.Example, profile.Text, StringComparison.Ordinal);
            Assert.Contains("--task \"<task>\"", profile.Text, StringComparison.Ordinal);
        }

        // The per-platform paths: Claude Desktop and VS Code differ, Cursor does not.
        var desktop = box.Registry.Find("claude-desktop")!.ConfigFile!;
        Assert.Equal("{appdata}/Claude/claude_desktop_config.json", desktop.For(AgentClientPlatform.Windows));
        Assert.Equal("{home}/Library/Application Support/Claude/claude_desktop_config.json", desktop.For(AgentClientPlatform.MacOS));
        Assert.Equal("{home}/.config/Claude/claude_desktop_config.json", desktop.For(AgentClientPlatform.Linux));
        Assert.Equal("{home}/.config/Code/User/mcp.json", box.Registry.Find("vscode")!.ConfigFile!.For(AgentClientPlatform.Linux));
        Assert.Equal("{home}/.cursor/mcp.json", box.Registry.Find("cursor")!.ConfigFile!.For(AgentClientPlatform.MacOS));

        var embedded = AgentClientRegistry.ParseCatalog(AgentClientRegistry.EmbeddedProfiles());
        Assert.Equal(profiles.Select(p => p.Id), embedded.Clients.Select(p => p.Id));
    }

    /// <summary>AGENT_INTEGRATION 9.6: the README client matrix has one row per shipped profile, in order, with its id.</summary>
    [Fact]
    public async Task TheReadmeClientMatrixListsEveryProfile()
    {
        using var box = new ClientSandbox();
        var profiles = await box.Registry.LoadAsync(CancellationToken.None);
        var readme = await File.ReadAllLinesAsync(Path.Combine(Cutlass.Tests.Compiler.Golden.RepositoryRoot(), "README.md"));
        var start = Array.FindIndex(readme, l => l.StartsWith("| Client | Profile id |", StringComparison.Ordinal));
        Assert.True(start >= 0, "README.md has no client matrix");
        var rows = readme.Skip(start + 2).TakeWhile(l => l.StartsWith('|'))
            .Select(l => l.Split('|', StringSplitOptions.TrimEntries))
            .Select(cells => (Name: cells[1], Id: cells[2].Trim('`')))
            .ToList();
        Assert.Equal(profiles.Select(p => (p.Name, p.Id)), rows);
    }

    [Fact]
    public async Task UserProfilesReplaceShippedOnesByIdAndAddNewOnes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cutlass-userprofiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "a.json"), """
                // My Cursor lives elsewhere.
                { "id": "cursor", "name": "Cursor (portable)", "detect": { "file": "{home}/portable/cursor" },
                  "configFile": "{home}/portable/cursor/mcp.json",
                  "configEntry": { "path": "mcpServers.cutlass", "value": { "command": "{exe}", "args": [] } }, }
                """);
            File.WriteAllText(Path.Combine(folder, "b.json"), """[ { "id": "my-harness", "name": "My harness", "transport": "http", "text": "{url}" }, { "name": "no id" } ]""");
            File.WriteAllText(Path.Combine(folder, "c.json"), "{ not json");
            using var box = new ClientSandbox(userProfiles: folder);
            var profiles = await box.Registry.LoadAsync(CancellationToken.None);

            Assert.Equal(13, profiles.Count);
            Assert.Equal("cursor", profiles[2].Id);
            Assert.Equal("Cursor (portable)", profiles[2].Name);
            Assert.Equal("my-harness", profiles[^1].Id);
            Assert.True(profiles[^1].IsTextOnly);
            Assert.Equal(Path.GetFullPath(Path.Combine(box.Home, "portable", "cursor", "mcp.json")), box.Registry.ConfigPath(profiles[2]));
            Assert.Equal(2, box.Registry.LoadProblems.Count);
            Assert.Contains(box.Registry.LoadProblems, p => p.Contains("c.json", StringComparison.Ordinal));
            Assert.Contains(box.Registry.LoadProblems, p => p.Contains("without an id", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ParseProfilesTakesOneProfileAnArrayOrACatalog()
    {
        Assert.Single(AgentClientRegistry.ParseProfiles("""{ "id": "x", "name": "X" }"""));
        Assert.Equal(2, AgentClientRegistry.ParseProfiles("""[ { "id": "x", "name": "X" }, { "id": "y", "name": "Y" } ]""").Count);
        Assert.Equal("z", AgentClientRegistry.ParseProfiles("""{ "version": 1, "clients": [ { "id": "z", "name": "Z" } ] }""")[0].Id);
        var path = AgentClientRegistry.ParseProfiles("""{ "id": "p", "name": "P", "configFile": { "windows": "a", "linux": "b" } }""")[0].ConfigFile!;
        Assert.Equal("a", path.For(AgentClientPlatform.Windows));
        Assert.Null(path.For(AgentClientPlatform.MacOS));
        Assert.Throws<JsonException>(() => AgentClientRegistry.ParseProfiles("[ 1 ]"));
    }

    [Fact]
    public async Task DetectionFollowsTheFakePathAndHome()
    {
        using var box = new ClientSandbox();
        Assert.False(await box.Registry.DetectAsync(await box.ProfileAsync("claude-code")));
        Assert.False(await box.Registry.DetectAsync(await box.ProfileAsync("cursor")));
        Assert.False(await box.Registry.DetectAsync(await box.ProfileAsync("gemini-cli")));

        box.InstallCommand("claude");
        Directory.CreateDirectory(Path.Combine(box.Home, ".cursor"));
        box.InstallCommand("gemini");
        Assert.True(await box.Registry.DetectAsync(await box.ProfileAsync("claude-code")));
        Assert.True(await box.Registry.DetectAsync(await box.ProfileAsync("cursor")));
        Assert.True(await box.Registry.DetectAsync(await box.ProfileAsync("gemini-cli")));

        // A configuration file alone counts as installed.
        Assert.False(await box.Registry.DetectAsync(await box.ProfileAsync("codex-cli")));
        box.Write(".codex/config.toml", "model = \"o3\"\n");
        Assert.True(await box.Registry.DetectAsync(await box.ProfileAsync("codex-cli")));

        // The per-user configuration folder is inside the fake home.
        var desktop = box.Registry.ConfigPath(await box.ProfileAsync("claude-desktop"))!;
        Assert.StartsWith(box.Home, desktop, StringComparison.Ordinal);
        Assert.EndsWith("claude_desktop_config.json", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceholdersFillAndEmptyOptionsDrop()
    {
        var values = new Dictionary<string, string> { ["exe"] = "/opt/c/mcp", ["scope"] = string.Empty, ["url"] = "http://h/" };
        Assert.Equal("/opt/c/mcp {unknown} {\"a\":1} http://h/v1", AgentClientRegistry.Expand("{exe} {unknown} {\"a\":1} {url}v1", values));
        Assert.Equal(["mcp", "remove", "cutlass"], AgentClientRegistry.ExpandArgs(["mcp", "remove", "cutlass", "-s", "{scope}"], values));
        Assert.Equal(["mcp", "add", "cutlass", "--", "/opt/c/mcp"], AgentClientRegistry.ExpandArgs(["mcp", "add", "cutlass", "--scope", "{scope}", "--", "{exe}"], values));
        Assert.Equal("claude mcp add cutlass -- \"C:\\Program Files\\x.exe\"", AgentClientRegistry.CommandLine("claude", ["mcp", "add", "cutlass", "--", @"C:\Program Files\x.exe"]));
    }

    [Fact]
    public async Task TheRegistrationTextOfEachKind()
    {
        using var box = new ClientSandbox();
        Assert.Equal("claude mcp add cutlass --scope user -- \"" + Exe + "\"", box.Registry.RegistrationText(await box.ProfileAsync("claude-code"), box.Context));
        Assert.Equal("claude mcp add cutlass --scope local -- \"" + Exe + "\"", box.Registry.RegistrationText(await box.ProfileAsync("claude-code"), box.Context, "local"));

        var cursor = box.Registry.RegistrationText(await box.ProfileAsync("cursor"), box.Context);
        Assert.Equal(
            "Add to " + Path.GetFullPath(Path.Combine(box.Home, ".cursor", "mcp.json")) + ":\n{\n  \"mcpServers\": {\n    \"cutlass\": {\n      \"command\": \"C:\\\\Program Files\\\\Cutlass\\\\Cutlass.Agent.Mcp.exe\",\n      \"args\": []\n    }\n  }\n}",
            cursor);
        var codex = box.Registry.RegistrationText(await box.ProfileAsync("codex-cli"), box.Context);
        Assert.EndsWith("config.toml:\n[mcp_servers.cutlass]\ncommand = \"C:\\\\Program Files\\\\Cutlass\\\\Cutlass.Agent.Mcp.exe\"\nargs = []", codex, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"stdio\"", box.Registry.RegistrationText(await box.ProfileAsync("vscode"), box.Context), StringComparison.Ordinal);

        // A harness text names the token file unless the context carries the confirmed token.
        var openai = await box.ProfileAsync("openai-compatible");
        var text = box.Registry.RegistrationText(openai, box.Context);
        Assert.Contains("http://localhost:47390/v1/tools?format=openai", text, StringComparison.Ordinal);
        Assert.Contains("Token: the content of " + box.Context.TokenFile, text, StringComparison.Ordinal);
        Assert.Contains("build/examples/agent-openai-compatible.py", text, StringComparison.Ordinal);
        Assert.Contains("Token: tok_1", box.Registry.RegistrationText(openai, box.Context with { Token = "tok_1" }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCliOutputIsReadTolerantly()
    {
        using var box = new ClientSandbox();
        var spec = (await box.ProfileAsync("claude-code")).Probe!;
        var timeout = TimeSpan.FromSeconds(30);

        var user = AgentClientCliParser.Parse(new CliResult(0, GetUser, string.Empty), spec, Exe, timeout);
        Assert.Equal(AgentClientState.Current, user.State);
        Assert.Equal("user", user.Scope);
        Assert.Equal(Exe, user.Detail);
        Assert.Equal("current (user scope)", user.Text);

        var stale = AgentClientCliParser.Parse(new CliResult(0, GetLocalStale, string.Empty), spec, Exe, timeout);
        Assert.Equal(AgentClientState.Stale, stale.State);
        Assert.Equal("local", stale.Scope);
        Assert.Equal(@"C:\Users\me\old\Cutlass.Agent.Mcp.exe", stale.Detail);
        Assert.True(stale.IsMissing);

        Assert.Equal(AgentClientState.NotRegistered, AgentClientCliParser.Parse(new CliResult(1, string.Empty, Missing), spec, Exe, timeout).State);
        Assert.Equal(AgentClientState.NotInstalled, AgentClientCliParser.Parse(CliResult.Missing("claude was not found on PATH."), spec, Exe, timeout).State);

        var foreign = AgentClientCliParser.Parse(new CliResult(0, "cutlass:\n  Scope: User config\n  Command: npx\n", string.Empty), spec, Exe, timeout);
        Assert.Equal(AgentClientState.Foreign, foreign.State);
        Assert.Equal("foreign entry", foreign.Text);

        var list = "Checking MCP server health...\n\ncutlass: " + Exe + " - \u2714 Connected\nother: npx thing - \u2717 Failed\n";
        Assert.Equal(AgentClientState.Current, AgentClientCliParser.Parse(new CliResult(0, list, string.Empty), spec, Exe, timeout).State);
        Assert.Equal(AgentClientState.Stale, AgentClientCliParser.Parse(new CliResult(0, "cutlass: D:\\old\\Cutlass.Agent.Mcp.exe --headless - \u2714 Connected\n", string.Empty), spec, Exe, timeout).State);
        Assert.Equal(AgentClientState.NotRegistered, AgentClientCliParser.Parse(new CliResult(0, "other: npx thing - \u2714 Connected\n", string.Empty), spec, Exe, timeout).State);
        Assert.Equal(AgentClientState.Current, AgentClientCliParser.Parse(new CliResult(0, "server cutlass -> \"" + Exe + "\" (stdio)", string.Empty), spec, Exe, timeout).State);

        var failed = AgentClientCliParser.Parse(new CliResult(2, string.Empty, "error: unknown command 'get'\nusage..."), spec, Exe, timeout);
        Assert.Equal(AgentClientState.Error, failed.State);
        Assert.Equal("error: unknown command 'get'", failed.Detail);
        Assert.Equal(AgentClientState.Error, AgentClientCliParser.Parse(new CliResult(-1, string.Empty, string.Empty, TimedOut: true), spec, Exe, timeout).State);
    }

    [Fact]
    public async Task ACliProbeIsNotInstalledWithoutTheCommandAndFallsBackToTheList()
    {
        using var box = new ClientSandbox(args => args[1] == "get"
            ? new CliResult(1, string.Empty, "error: unknown command 'get'")
            : new CliResult(0, "cutlass: " + Exe + " - \u2714 Connected", string.Empty));
        var profile = await box.ProfileAsync("claude-code");
        var missing = await box.Registry.ProbeAsync(profile, box.Context, CancellationToken.None);
        Assert.Equal(AgentClientState.NotInstalled, missing.State);
        Assert.False(missing.Installed);
        Assert.Empty(box.Runner.Calls);

        box.InstallCommand("claude");
        var probe = await box.Registry.ProbeAsync(profile, box.Context, CancellationToken.None);
        Assert.Equal(AgentClientState.Current, probe.State);
        Assert.Equal(["claude mcp get cutlass", "claude mcp list"], box.Runner.Calls);
        Assert.Equal(["D:/Edits", "D:/Edits"], box.Runner.Directories);
    }

    [Fact]
    public async Task RegisterRemovesTheStaleEntryFromItsScopeThenAdds()
    {
        var added = false;
        using var box = new ClientSandbox(args => args[1] switch
        {
            "get" => new CliResult(0, added ? GetUser : GetLocalStale, string.Empty),
            "remove" => new CliResult(0, "Removed MCP server cutlass from local config", string.Empty),
            "add" => Add(),
            _ => new CliResult(1, string.Empty, "unexpected"),
        });
        box.InstallCommand("claude");

        var result = await box.Registry.RegisterAsync(await box.ProfileAsync("claude-code"), box.Context, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AgentClientState.Current, result.Probe.State);
        Assert.Equal(
            ["claude mcp get cutlass", "claude mcp remove cutlass -s local", "claude mcp add cutlass --scope user -- " + Exe, "claude mcp get cutlass"],
            box.Runner.Calls);
        Assert.Contains("> claude mcp add cutlass --scope user -- \"" + Exe + "\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("Added stdio MCP server cutlass", result.Output, StringComparison.Ordinal);

        CliResult Add()
        {
            added = true;
            return new CliResult(0, "Added stdio MCP server cutlass with command: " + Exe + " to user config", string.Empty);
        }
    }

    [Fact]
    public async Task RegisterInLocalScopeAndRemoveFromTheReportedScope()
    {
        var registered = false;
        using var box = new ClientSandbox(args => args[1] switch
        {
            "get" => registered ? new CliResult(0, "cutlass:\n  Scope: Local config\n  Command: " + Exe + "\n", string.Empty) : new CliResult(1, string.Empty, Missing),
            "add" => Set(true),
            "remove" => Set(false),
            _ => new CliResult(1, string.Empty, "unexpected"),
        });
        box.InstallCommand("claude");
        var profile = await box.ProfileAsync("claude-code");

        var add = await box.Registry.RegisterAsync(profile, box.Context, "local", CancellationToken.None);
        Assert.True(add.Succeeded);
        Assert.Equal("current (local scope)", add.Probe.Text);
        Assert.Contains("claude mcp add cutlass --scope local -- " + Exe, box.Runner.Calls);

        var remove = await box.Registry.RemoveAsync(profile, box.Context, CancellationToken.None);
        Assert.True(remove.Succeeded);
        Assert.Equal(AgentClientState.NotRegistered, remove.Probe.State);
        Assert.Contains("claude mcp remove cutlass -s local", box.Runner.Calls);

        // Nothing left: remove runs no command.
        var calls = box.Runner.Calls.Count;
        var again = await box.Registry.RemoveAsync(profile, box.Context, CancellationToken.None);
        Assert.True(again.Succeeded);
        Assert.Equal(calls + 1, box.Runner.Calls.Count);

        CliResult Set(bool value)
        {
            registered = value;
            return new CliResult(0, "ok", string.Empty);
        }
    }

    [Fact]
    public async Task RegisterWithoutTheCliRunsNothing()
    {
        using var box = new ClientSandbox();
        var result = await box.Registry.RegisterAsync(await box.ProfileAsync("claude-code"), box.Context, null, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(AgentClientState.NotInstalled, result.Probe.State);
        Assert.Contains("not found on PATH", result.Output, StringComparison.Ordinal);
        Assert.Empty(box.Runner.Calls);
    }

    [Fact]
    public void ASandboxRefusesTheRealProcessRunner()
    {
        var home = Path.Combine(Path.GetTempPath(), "cutlass-never-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<ArgumentException>(() => new AgentClientRegistry(AgentClientEnvironment.Sandbox(home), new CliProcess(), null, null, null));
        Assert.False(Directory.Exists(home));
    }

    [Fact]
    public async Task TheLastRegisteredClientIsRemembered()
    {
        using var box = new ClientSandbox();
        Assert.Null(await box.Registry.LoadLastRegisteredAsync(CancellationToken.None));
        await box.Registry.SaveLastRegisteredAsync("cursor", CancellationToken.None);
        var again = new AgentClientRegistry(box.Environment, box.Runner, ClientSandbox.ShippedFile, null, Path.Combine(box.Home, "state.json"));
        Assert.Equal("cursor", await again.LoadLastRegisteredAsync(CancellationToken.None));
    }

    [Fact]
    public void TheProcessHelperFindsToolsOnPathWithWindowsExtensions()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cutlass-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "claude.cmd"), "@echo off");
            var path = "Z:\\nowhere;" + folder;
            Assert.Equal(Path.Combine(folder, "claude.cmd"), CliProcess.Resolve("claude", path, windows: true));
            Assert.Null(CliProcess.Resolve("claude", path.Replace(';', ':'), windows: false));
            Assert.Null(CliProcess.Resolve("nothere", path, windows: true));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task TheProcessHelperReportsAMissingTool()
    {
        var result = await new CliProcess().RunAsync("cutlass-no-such-tool-" + Guid.NewGuid().ToString("N"), ["--version"], null, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(result.NotFound);
        Assert.False(result.Succeeded);
    }
}
