using System.Text.RegularExpressions;
using Cutlass.Tests.Compiler;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// AGENT_INTEGRATION 9.3: nothing in <c>src/Cutlass.App</c> names an agent vendor or client outside the profile file
/// (<c>Assets/agent-clients.json</c>); clients are data, not code paths. The scan covers every C#, XAML, Markdown,
/// JSON and project file of the App (not <c>bin</c> or <c>obj</c>). "Cursor" is allowed only as the Avalonia pointer
/// API (<c>Cursor = ...</c>, <c>new Cursor(...)</c>, <c>Cursor="Hand"</c>). The same scan covers
/// <c>src/Cutlass.Agent</c> and <c>src/Cutlass.Agent.Mcp</c> with a narrower allowance (the tool format ids).
/// </summary>
public sealed partial class VendorNeutralityTests
{
    private static readonly string[] Extensions = [".cs", ".axaml", ".md", ".json", ".csproj"];

    [Fact]
    public void TheAppSourceNamesNoVendorOutsideTheProfileFile()
    {
        var root = Path.Combine(Golden.RepositoryRoot(), "src", "Cutlass.App");
        var allowed = Path.Combine(root, "Assets", "agent-clients.json");
        var files = SourceFiles(root)
            .Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(allowed), StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Contains(files, f => f.EndsWith("MainWindowViewModel.Mcp.cs", StringComparison.Ordinal));
        var hits = Scan(root, files, _ => false);
        Assert.True(hits.Count == 0, "Vendor names in the App source (move them into Assets/agent-clients.json):\n" + string.Join("\n", hits));
    }

    /// <summary>
    /// The engine and the MCP exe (server instructions, prompts, resources, <c>Registration.cs</c>, the usage text)
    /// name no client either. The only vendor words allowed there are the tool format ids <c>openai</c>,
    /// <c>anthropic</c> and <c>gemini</c> (AGENT_INTEGRATION 9.2), and only in the exporters
    /// (<c>Api/Export/</c>) and the catalog entry of <c>discovery.tools</c> (<c>Api/ToolCatalog.cs</c>), plus the repository
    /// file name <c>CLAUDE.md</c> in comments.
    /// </summary>
    [Theory]
    [InlineData("Cutlass.Agent")]
    [InlineData("Cutlass.Agent.Mcp")]
    public void TheAgentSourceNamesNoClient(string project)
    {
        var root = Path.Combine(Golden.RepositoryRoot(), "src", project);
        var files = SourceFiles(root).ToList();
        Assert.NotEmpty(files);
        if (project == "Cutlass.Agent.Mcp")
        {
            Assert.Contains(files, f => f.EndsWith("Registration.cs", StringComparison.Ordinal));
            Assert.Contains(files, f => f.EndsWith("ServerInstructions.cs", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains(files, f => f.EndsWith("AgentInstructions.cs", StringComparison.Ordinal));
        }

        var hits = Scan(root, files, IsFormatFile);
        Assert.True(hits.Count == 0, "Vendor or client names in " + project + ":\n" + string.Join("\n", hits));

        static bool IsFormatFile(string relative) =>
            relative.StartsWith("Api/Export/", StringComparison.Ordinal) || relative == "Api/ToolCatalog.cs";
    }

    [Theory]
    [InlineData("// see CLAUDE.md \"Processes\"", false, false)]
    [InlineData("case \"openai\": text = OpenAiToolExporter.Export();", true, false)]
    [InlineData("case \"openai\":", false, true)]
    [InlineData("the Anthropic format for Claude", true, true)]
    public void TheAgentAllowanceCoversOnlyFormatIdsAndTheRepoFile(string line, bool formatFile, bool hit) =>
        Assert.Equal(hit, Vendor().IsMatch(Allow(line, formatFile)));

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !IsBuildOutput(root, f));

    private static List<string> Scan(string root, IEnumerable<string> files, Func<string, bool> isFormatFile)
    {
        var hits = new List<string>();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var formatFile = isFormatFile(relative);
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = Allow(lines[i], formatFile);
                if (Vendor().Match(line) is { Success: true } vendor)
                {
                    hits.Add(relative + ":" + (i + 1) + ": " + vendor.Value);
                }
                else if (CursorWord().Match(line) is { Success: true } cursor)
                {
                    hits.Add(relative + ":" + (i + 1) + ": " + cursor.Value);
                }
            }
        }

        return hits;
    }

    /// <summary>Removes the allowed words: the file name <c>CLAUDE.md</c>, and the format ids in a format file.</summary>
    private static string Allow(string line, bool formatFile)
    {
        line = line.Replace("CLAUDE.md", string.Empty, StringComparison.Ordinal);
        return formatFile ? FormatId().Replace(line, string.Empty) : line;
    }

    [Theory]
    [InlineData("// Registers with Claude Code.", true)]
    [InlineData("var x = \"anthropic\";", true)]
    [InlineData("<TextBlock Text=\"VS Code\" />", true)]
    [InlineData("the Zed editor", true)]
    [InlineData("~/.codex/config.toml", true)]
    [InlineData("an MCP client", false)]
    [InlineData("Continue without saving", false)]
    [InlineData("recognized and authorized", false)]
    public void TheVendorPatternFindsVendorsOnly(string line, bool hit) =>
        Assert.Equal(hit, Vendor().IsMatch(line));

    [Theory]
    [InlineData("Cursor = new Cursor(StandardCursorType.Hand);", false)]
    [InlineData("public static readonly Cursor Arrow = new(StandardCursorType.Arrow);", false)]
    [InlineData("Cursor = Cursors.Arrow;", false)]
    [InlineData("<Setter Property=\"Cursor\" Value=\"Hand\" />", false)]
    [InlineData("<Panel Cursor=\"Hand\" />", false)]
    [InlineData("An MCP client (Cursor, ...) starts the server.", true)]
    [InlineData("the cursor tail", true)]
    public void TheCursorPatternAllowsOnlyThePointerApi(string line, bool hit) =>
        Assert.Equal(hit, CursorWord().IsMatch(line));

    private static bool IsBuildOutput(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        return relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"claude|anthropic|openai|gemini|windsurf|codeium|codex|copilot|vs ?code|visual studio code|\bzed\b|continue\.dev", RegexOptions.IgnoreCase)]
    private static partial Regex Vendor();

    [GeneratedRegex(@"openai|anthropic|gemini", RegexOptions.IgnoreCase)]
    private static partial Regex FormatId();

    [GeneratedRegex(@"(?<!new |readonly |Property="")\b[Cc]ursor\b(?!\s*=|\(|"")")]
    private static partial Regex CursorWord();
}
