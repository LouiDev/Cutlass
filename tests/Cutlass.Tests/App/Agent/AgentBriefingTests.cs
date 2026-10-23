using System.Text;
using Cutlass.Agent.Api;
using Cutlass.Agent.Lock;
using Cutlass.App.Services.Agent;
using Cutlass.App.Services.Agent.Clients;
using Cutlass.App.Services.Agent.Clients.Profiles;
using Cutlass.Core.Model;
using Cutlass.Tests.Compiler;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// AGENT_INTEGRATION 6.6 and 9.4 (MCP_HOSTING_CONCEPT 5): the briefing is the neutral body plus a per-target tail,
/// rendered with the live values of a fixed project against goldens in <c>tests/Cutlass.Tests/App/Agent/Goldens/</c>:
/// the generic tail, an MCP client with a CLI and one with a configuration file (their registration as the fallback),
/// a tool-calling harness (base URL, tools URL, the <c>/v1/call</c> contract, the example script) and plain HTTP (the
/// <c>curl</c> examples, the token only after the confirmation). The body names no vendor, a user body and a user tail
/// replace the built-in ones, and <see cref="ProjectDescriber.Summary"/> lists the tracks. Under de-DE, so the numbers
/// prove the invariant culture. <c>CUTLASS_UPDATE_GOLDEN=1</c> rewrites the goldens.
/// </summary>
[UseCulture("de-DE")]
public sealed class AgentBriefingTests : IDisposable
{
    private const string Exe = "C:/Program Files/Cutlass/Cutlass.Agent.Mcp.exe";
    private const string TokenFile = "C:/Users/me/AppData/Local/Cutlass/agent/token";
    private const string Url = "http://localhost:47390/";
    private const string Docs = "C:/Program Files/Cutlass/AGENT_INTEGRATION.md";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cutlass-briefing-" + Guid.NewGuid().ToString("N"));
    private readonly ClientSandbox _box = new();

    /// <summary>Removes the temporary folders.</summary>
    public void Dispose()
    {
        _box.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written.
        }
    }

    [Fact]
    public void TheGenericBriefingMatchesTheGolden()
    {
        var text = Render(null, AgentClientBriefingKind.Generic, token: null);
        Match("briefing-generic.txt", text);
        Assert.Empty(AgentBriefing.Unfilled(text));
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
        Assert.Contains("- Project: \"interview\" (D:/Edits/interview.cutlass), unsaved changes", text, StringComparison.Ordinal);
        Assert.Contains("- Lock: held by Claude Code", text, StringComparison.Ordinal);
        Assert.Contains("cutlass_acquire_lock", text, StringComparison.Ordinal);
        Assert.Contains(Url + "v1/instructions", text, StringComparison.Ordinal);
        Assert.Contains("Agent > Connect an agent client...", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnMcpClientWithACliGetsItsCommandAsTheFallback()
    {
        var text = Render(await _box.ProfileAsync("claude-code"), AgentClientBriefingKind.Mcp, token: null);
        Match("briefing-mcp-cli.txt", text);
        Assert.Empty(AgentBriefing.Unfilled(text));
        Assert.Contains("the MCP server \"cutlass\" in Claude Code", text, StringComparison.Ordinal);
        Assert.Contains("\n  claude mcp add cutlass --scope user -- \"" + Exe + "\"\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnMcpClientWithAConfigFileGetsItsEntryAsTheFallback()
    {
        var cursor = await _box.ProfileAsync("cursor");
        var text = Render(cursor, AgentClientBriefingKind.Mcp, token: null).Replace(_box.Registry.ConfigPath(cursor)!, "<home>/.cursor/mcp.json", StringComparison.Ordinal);
        Match("briefing-mcp-config.txt", text);
        Assert.Contains("  Add to <home>/.cursor/mcp.json:\n  {\n    \"mcpServers\": {", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHarnessGetsTheHttpContractAndItsExample()
    {
        var profile = await _box.ProfileAsync("openai-compatible");
        var text = Render(profile, AgentClientBriefingKind.Harness, token: null);
        Match("briefing-harness.txt", text);
        Assert.Empty(AgentBriefing.Unfilled(text));
        Assert.Contains("- Tool definitions in your harness's format: " + Url + "v1/tools?format=openai", text, StringComparison.Ordinal);
        Assert.Contains("POST " + Url + "v1/call with {\"tool\": \"<name>\", \"args\": { ... }}", text, StringComparison.Ordinal);
        Assert.Contains("build/examples/agent-openai-compatible.py", text, StringComparison.Ordinal);
        Assert.Contains("Token: read it from " + TokenFile, text, StringComparison.Ordinal);
        Assert.Contains("Token: tok_secret", Render(profile, AgentClientBriefingKind.Harness, token: "tok_secret"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheHttpBriefingWithoutTheTokenPointsAtTheTokenFile()
    {
        var text = Render(null, AgentClientBriefingKind.Http, token: null);
        Match("briefing-http.txt", text);
        Assert.Empty(AgentBriefing.Unfilled(text));
        Assert.Contains("export CUTLASS_TOKEN=\"$(cat '" + TokenFile + "')\"", text, StringComparison.Ordinal);
        Assert.Contains("curl -s -H \"Authorization: Bearer $CUTLASS_TOKEN\" " + Url + "v1/status", text, StringComparison.Ordinal);
        foreach (var route in new[] { "v1/lock/acquire", "v1/project/describe", "v1/edit/place", "v1/frame/30", "v1/lock/release", "v1/call", "v1/tools?format=markdown" })
        {
            Assert.Contains(Url + route, text, StringComparison.Ordinal);
        }

        Assert.Contains("-o frame.png", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tok_", text, StringComparison.Ordinal);
        var confirmed = Render(null, AgentClientBriefingKind.Http, token: "tok_secret");
        Assert.Contains("export CUTLASS_TOKEN='tok_secret'", confirmed, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenFile, confirmed, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBodyAndTheTailTemplatesNameNoVendor()
    {
        string[] vendors = ["Claude", "Anthropic", "OpenAI", "Gemini", "Cursor", "Windsurf", "Codex", "Copilot", "VS Code", "Zed", "Continue"];
        var templates = new[] { AgentBriefing.DefaultBody() }
            .Concat(Enum.GetValues<AgentClientBriefingKind>().Select(AgentBriefing.DefaultTail));
        foreach (var template in templates)
        {
            foreach (var vendor in vendors)
            {
                Assert.DoesNotContain(vendor, template, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void AnUntitledEmptySessionStillFillsEveryPlaceholder()
    {
        var project = new ProjectBuilder().Build() with { Media = [] };
        var values = AgentBriefing.Values(project, false, null, false, AgentLockState.Free, Url, Exe, "  x", AgentBriefing.TokenLine(null, TokenFile), Docs);
        foreach (var kind in Enum.GetValues<AgentClientBriefingKind>())
        {
            var text = AgentBriefing.Render(AgentBriefing.Compose(AgentBriefing.DefaultBody(), AgentBriefing.DefaultTail(kind)), values);
            Assert.Empty(AgentBriefing.Unfilled(text));
            Assert.Contains("- Project: none is open", text, StringComparison.Ordinal);
            Assert.Contains("- Media: none imported yet", text, StringComparison.Ordinal);
            Assert.Contains("- Lock: free", text, StringComparison.Ordinal);
        }

        Assert.Equal("an untitled project", AgentBriefing.ProjectText(true, null, false));
    }

    [Fact]
    public void UnknownPlaceholdersAndJsonBracesStay()
    {
        var text = AgentBriefing.Render("{project} {unknown} -d '{\"a\":1}' {}", new Dictionary<string, string> { ["project"] = "P" });
        Assert.Equal("P {unknown} -d '{\"a\":1}' {}", text);
        Assert.Equal(["{unknown}"], AgentBriefing.Unfilled(text));
        Assert.Equal("  a\n\n  b", AgentBriefing.Indent("a\r\n\r\nb"));
        Assert.Equal("Body\n\nTail\n", AgentBriefing.Compose("Body\n\n\n", "Tail"));
    }

    [Fact]
    public async Task UserTemplatesReplaceTheBodyAndOneTargetsTail()
    {
        Directory.CreateDirectory(_folder);
        var builtIn = await AgentBriefing.LoadTemplateAsync(_folder, null, "cursor", AgentClientBriefingKind.Mcp, CancellationToken.None);
        Assert.Equal("built-in", builtIn.BodySource);
        Assert.Equal("built-in", builtIn.TailSource);
        Assert.Equal(AgentBriefing.Compose(AgentBriefing.DefaultBody(), AgentBriefing.DefaultTail(AgentClientBriefingKind.Mcp)), builtIn.Text);

        var body = Path.Combine(_folder, AgentBriefing.TemplateFileName);
        await File.WriteAllTextAsync(body, "Mine: {project}\r\n");
        var tail = Path.Combine(_folder, AgentBriefing.TailFileName("cursor"));
        await File.WriteAllTextAsync(tail, "Cursor tail {registration}");
        var user = await AgentBriefing.LoadTemplateAsync(_folder, null, "cursor", AgentClientBriefingKind.Mcp, CancellationToken.None);
        Assert.Equal(body, user.BodySource);
        Assert.Equal(tail, user.TailSource);
        Assert.Equal("Mine: {project}\n\nCursor tail {registration}\n", user.Text);

        // Another target keeps the built-in tail; the setting's body file wins over the agent folder.
        var other = await AgentBriefing.LoadTemplateAsync(_folder, null, "zed", AgentClientBriefingKind.Mcp, CancellationToken.None);
        Assert.Equal("built-in", other.TailSource);
        var custom = Path.Combine(_folder, "custom.md");
        await File.WriteAllTextAsync(custom, "Custom {lock}");
        Assert.Equal("Custom {lock}", (await AgentBriefing.LoadTemplateAsync(_folder, custom, "zed", AgentClientBriefingKind.Mcp, CancellationToken.None)).Body);
        Assert.Equal("Mine: {project}\n", (await AgentBriefing.LoadTemplateAsync(_folder, Path.Combine(_folder, "gone.md"), "zed", AgentClientBriefingKind.Mcp, CancellationToken.None)).Body);
        Assert.Equal("briefing-tail-http.md", AgentBriefing.TailFileName("http"));
    }

    [Fact]
    public void TheTrackSummaryListsTracksTopToBottomAndCountsTheRest()
    {
        var project = Sample();
        Assert.Equal(
            "V2: 1 clip\nV1: 2 clips, 1 transition\nA1: 1 clip\nA2: 1 clip",
            ProjectDescriber.Summary(project, 8));
        Assert.Equal("V2: 1 clip\nV1: 2 clips, 1 transition\n... 2 more tracks", ProjectDescriber.Summary(project, 3));
        Assert.Equal("... 4 more tracks", ProjectDescriber.Summary(project, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectDescriber.Summary(project, 0));
    }

    [Theory]
    [InlineData("free", "free")]
    [InlineData("held", "held by Claude Code")]
    [InlineData("paused", "paused, held by Claude Code")]
    [InlineData("pending", "free, Cursor is waiting")]
    public void TheLockTextNamesTheHolder(string phase, string expected)
    {
        var state = phase switch
        {
            "held" => AgentLockState.Free with { Phase = LockPhase.Held, AgentName = "Claude Code" },
            "paused" => AgentLockState.Free with { Phase = LockPhase.Paused, AgentName = "Claude Code" },
            "pending" => AgentLockState.Free with { Phase = LockPhase.Pending, PendingAgent = "Cursor" },
            _ => AgentLockState.Free,
        };
        Assert.Equal(expected, AgentBriefing.LockText(state));
    }

    private static Project Sample()
    {
        var music = A("c_music", 0, 300, media: Music);
        return new ProjectBuilder()
            .With(V1, V("c_01", 0, 150, sourceFrame: 50, link: "l_01"), V("c_05", 150, 300, sourceFrame: 400))
            .With(A1, A("c_11", 0, 150, sourceFrame: 50, link: "l_01"))
            .With(A2, music)
            .With(V2, Title("c_02", 0, 120, "Meet Ana"))
            .Transition(V1, "x_01", "c_01", "c_05", 30)
            .Build();
    }

    // What MainWindowViewModel.BuildBriefingAsync does, with the machine-specific values fixed.
    private string Render(AgentClientProfile? profile, AgentClientBriefingKind kind, string? token)
    {
        var state = AgentLockState.Free with { Phase = LockPhase.Held, AgentName = "Claude Code", Reason = "Remove silences" };
        var context = new AgentClientContext(Exe, Url, TokenFile, token, "D:/Edits");
        var registration = profile is { IsTextOnly: false }
            ? AgentBriefing.Indent(_box.Registry.RegistrationText(profile, context))
            : "  In Cutlass: Agent > Connect an agent client...";
        var values = AgentBriefing.Values(
            Sample(),
            hasProject: true,
            "D:/Edits/interview.cutlass",
            dirty: true,
            state,
            Url,
            Exe,
            registration,
            kind == AgentClientBriefingKind.Http ? AgentBriefing.TokenLine(token, TokenFile) : AgentBriefing.TokenText(token, TokenFile),
            Docs,
            profile?.Name ?? "your agent client",
            profile?.ToolsFormat is { } format ? Url + "v1/tools?format=" + format : null,
            profile?.Example);
        return AgentBriefing.Render(AgentBriefing.Compose(AgentBriefing.DefaultBody(), AgentBriefing.DefaultTail(kind)), values);
    }

    private static void Match(string fileName, string actual)
    {
        var dir = Path.Combine(Golden.RepositoryRoot(), "tests", "Cutlass.Tests", "App", "Agent", "Goldens");
        var path = Path.Combine(dir, fileName);
        if (Golden.Updating)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, Utf8NoBom.GetBytes(actual));
            return;
        }

        Assert.True(File.Exists(path), $"Golden file {fileName} is missing. Run with CUTLASS_UPDATE_GOLDEN=1. Actual:\n{actual}");
        Assert.Equal(Utf8NoBom.GetString(File.ReadAllBytes(path)).ReplaceLineEndings("\n"), actual);
    }
}
