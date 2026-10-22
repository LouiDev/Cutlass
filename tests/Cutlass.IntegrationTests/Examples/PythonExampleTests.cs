using Cutlass.Agent.Api.Export;
using Cutlass.IntegrationTests.Infrastructure;

namespace Cutlass.IntegrationTests.Examples;

/// <summary>
/// The Python reference harnesses of AGENT_INTEGRATION 9.5 without a model, a network or an SDK: they parse, stay
/// under 150 lines, and their tool loading and call handling work on the real vendor documents. <c>check_harness.py</c>
/// loads each harness with a stub SDK module and a fake transport that serves the <see cref="ToolExport"/> rendering
/// of its vendor format, exactly what <c>GET /v1/tools?format=...</c> serves. Python's own parser runs through
/// <c>ast.parse</c>, which, unlike <c>py_compile</c>, writes no <c>__pycache__</c> into the repository.
/// </summary>
public sealed class PythonExampleTests
{
    private static readonly string[] Harnesses = ["agent-openai-compatible.py", "agent-anthropic.py"];

    [PythonFact]
    public async Task TheHarnessesParse()
    {
        const string check = "import ast, sys\nfor p in sys.argv[1:]:\n    ast.parse(open(p, encoding='utf-8').read(), p)\nprint('parsed', len(sys.argv) - 1)";
        var args = new List<string> { "-I", "-c", check };
        args.AddRange(Harnesses.Select(ExampleScripts.PathOf));
        var result = await TestProcess.RunAsync(ExampleScripts.Python!, args, ExampleScripts.Directory, TimeSpan.FromMinutes(1));
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Contains("parsed 2", result.Stdout, StringComparison.Ordinal);
    }

    [PythonTheory]
    [InlineData("agent-openai-compatible.py", "openai")]
    [InlineData("agent-anthropic.py", "anthropic")]
    public async Task TheHarnessLoadsItsVendorToolsAndHandlesCalls(string harness, string format)
    {
        using var temp = new TempDirectory("cutlass-harness-check");
        Assert.True(ToolExport.TryRender(format, out var tools, out _));
        var toolsFile = temp.Combine(format + "-tools.json");
        await File.WriteAllTextAsync(toolsFile, tools);
        var checker = Path.Combine(ExampleScripts.RepositoryRoot, "tests", "Cutlass.IntegrationTests", "Examples", "check_harness.py");

        var result = await TestProcess.RunAsync(
            ExampleScripts.Python!,
            ["-I", checker, ExampleScripts.PathOf(harness), format, toolsFile, temp.Combine("png")],
            temp.Path,
            TimeSpan.FromMinutes(1));

        Assert.True(result.ExitCode == 0, result.Stdout + result.Stderr);
        Assert.Contains("harness check passed", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHarnessesStayShortAndImportOnlyTheirSdk()
    {
        foreach (var name in Harnesses)
        {
            var lines = File.ReadAllLines(ExampleScripts.PathOf(name));
            Assert.True(lines.Length < 150, $"{name} has {lines.Length} lines.");
            var imports = lines.Where(l => l.StartsWith("import ", StringComparison.Ordinal) || l.StartsWith("from ", StringComparison.Ordinal)).ToList();
            Assert.DoesNotContain(imports, l => l.Contains("requests", StringComparison.Ordinal));
            Assert.Single(imports, l => l.Contains("openai", StringComparison.Ordinal) || l.Contains("anthropic", StringComparison.Ordinal));
        }
    }
}
