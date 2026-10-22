using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Export;
using Cutlass.Agent.Http;
using Cutlass.Tests.Compiler;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Agent;

/// <summary>
/// The discovery documents of AGENT_INTEGRATION 9.2 against goldens in <c>tests/Cutlass.Tests/Agent/Goldens/</c>
/// (<c>tools-*.json</c>, <c>tools-markdown.md</c>, <c>openapi.json</c>), plus the shape each vendor expects and the
/// structure of the OpenAPI document, under de-DE. <c>CUTLASS_UPDATE_GOLDEN=1</c> rewrites the goldens.
/// </summary>
[UseCulture("de-DE")]
public sealed partial class ToolExportTests
{
    private const string GoldenVersion = "1.2.3";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static void Match(string fileName, string actual)
    {
        var dir = Path.Combine(Golden.RepositoryRoot(), "tests", "Cutlass.Tests", "Agent", "Goldens");
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

    private static int McpToolCount => ToolCatalog.McpTools.Count();

    [Theory]
    [InlineData("json", "tools-json.json")]
    [InlineData("openai", "tools-openai.json")]
    [InlineData("anthropic", "tools-anthropic.json")]
    [InlineData("gemini", "tools-gemini.json")]
    [InlineData("markdown", "tools-markdown.md")]
    public void EveryFormatMatchesItsGolden(string format, string golden)
    {
        Assert.True(ToolExport.TryRender(format, out var text, out var mediaType));
        Assert.Equal(format == "markdown" ? "text/markdown" : "application/json", mediaType);
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain(((char)0x2014).ToString(), text, StringComparison.Ordinal);
        Match(golden, text);
    }

    [Fact]
    public void TheOpenApiDocumentMatchesItsGolden()
    {
        var text = OpenApiDocument.Build(GoldenVersion);
        Assert.DoesNotContain('\r', text);
        Match("openapi.json", text);
    }

    [Fact]
    public void ExportsAreDeterministicAndUnknownFormatsFail()
    {
        Assert.Equal(OpenAiToolExporter.Export(), OpenAiToolExporter.Export());
        Assert.Equal(OpenApiDocument.Build(GoldenVersion), OpenApiDocument.Build(GoldenVersion));
        Assert.True(ToolExport.TryRender(null, out var plain, out _));
        Assert.Equal(ToolsDocument.Build(), plain);
        Assert.True(ToolExport.TryRender("OpenAI", out var upper, out _));
        Assert.Equal(OpenAiToolExporter.Export(), upper);
        Assert.False(ToolExport.TryRender("yaml", out _, out _));
    }

    [Fact]
    public void ThePlainDocumentIsTheWholeCatalog()
    {
        using var doc = JsonDocument.Parse(ToolsDocument.Build());
        var tools = doc.RootElement.GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(ToolCatalog.All.Length, tools.Count);
        for (var i = 0; i < tools.Count; i++)
        {
            var info = ToolCatalog.All[i];
            var entry = tools[i];
            Assert.Equal(info.Name, entry.GetProperty("name").GetString());
            Assert.Equal(info.McpName, entry.GetProperty("mcpName").GetString());
            Assert.Equal(info.Description, entry.GetProperty("description").GetString());
            Assert.Equal(info.NeedsLock, entry.GetProperty("needsLock").GetBoolean());
            Assert.Equal(info.Mutating, entry.GetProperty("mutating").GetBoolean());
            Assert.Equal(info.Heavy, entry.GetProperty("heavy").GetBoolean());
            Assert.Contains(entry.GetProperty("result").GetString(), new[] { "json", "png", "text", "document" });
            Assert.Equal("object", entry.GetProperty("parameters").GetProperty("type").GetString());
        }

        // Tools without an MCP name (events, the discovery routes) are in the plain document only.
        Assert.Contains(tools, t => t.GetProperty("name").GetString() == "events" && t.GetProperty("mcpName").ValueKind == JsonValueKind.Null);
        Assert.Contains(tools, t => t.GetProperty("name").GetString() == "frame.render" && t.GetProperty("result").GetString() == "png");
    }

    [Fact]
    public void OpenAiEntriesAreFunctionsWithObjectParameters()
    {
        using var doc = JsonDocument.Parse(OpenAiToolExporter.Export());
        var entries = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(McpToolCount, entries.Count);
        foreach (var entry in entries)
        {
            Assert.Equal("function", entry.GetProperty("type").GetString());
            var function = entry.GetProperty("function");
            Assert.Matches(OpenAiName(), function.GetProperty("name").GetString()!);
            Assert.False(string.IsNullOrWhiteSpace(function.GetProperty("description").GetString()));
            Assert.Equal("object", function.GetProperty("parameters").GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, function.GetProperty("parameters").GetProperty("properties").ValueKind);
        }

        Assert.DoesNotContain(entries, e => e.GetProperty("function").GetProperty("name").GetString() == "events");
    }

    [Fact]
    public void AnthropicEntriesHaveAnInputSchema()
    {
        using var doc = JsonDocument.Parse(AnthropicToolExporter.Export());
        var entries = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(McpToolCount, entries.Count);
        foreach (var entry in entries)
        {
            Assert.StartsWith("cutlass_", entry.GetProperty("name").GetString(), StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("description").GetString()));
            Assert.Equal("object", entry.GetProperty("input_schema").GetProperty("type").GetString());
            Assert.Equal(3, entry.EnumerateObject().Count());
        }

        // The schema passes through unchanged.
        var split = entries.Single(e => e.GetProperty("name").GetString() == "cutlass_split");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(ToolCatalog.Find("edit.split")!.Schema), JsonNode.Parse(split.GetProperty("input_schema").GetRawText())));
    }

    [Fact]
    public void GeminiHasOneDeclarationListAndNoUnsupportedKeywords()
    {
        var text = GeminiToolExporter.Export();
        using var doc = JsonDocument.Parse(text);
        var tools = doc.RootElement.EnumerateArray().ToList();
        var declarations = Assert.Single(tools).GetProperty("functionDeclarations").EnumerateArray().ToList();
        Assert.Equal(McpToolCount, declarations.Count);
        Assert.DoesNotContain("additionalProperties", text, StringComparison.Ordinal);
        Assert.DoesNotContain("$ref", text, StringComparison.Ordinal);
        foreach (var declaration in declarations)
        {
            Assert.StartsWith("cutlass_", declaration.GetProperty("name").GetString(), StringComparison.Ordinal);
            if (declaration.TryGetProperty("parameters", out var parameters))
            {
                Assert.Equal("object", parameters.GetProperty("type").GetString());
                Assert.True(parameters.GetProperty("properties").EnumerateObject().Any());
                AssertGeminiSchema(parameters);
            }
        }

        // A tool without arguments has no parameters; a nullable integer becomes type plus nullable.
        Assert.False(declarations.Single(d => d.GetProperty("name").GetString() == "cutlass_status").TryGetProperty("parameters", out _));
        var inOut = declarations.Single(d => d.GetProperty("name").GetString() == "cutlass_set_in_out").GetProperty("parameters").GetProperty("properties").GetProperty("inFrame");
        Assert.Equal("integer", inOut.GetProperty("type").GetString());
        Assert.True(inOut.GetProperty("nullable").GetBoolean());
    }

    [Fact]
    public void GeminiReductionDropsWhatGeminiRejects()
    {
        const string schema = """
            {"type":"object","additionalProperties":false,"$schema":"x","properties":{
              "a":{"type":["string","null"],"default":"x","description":"A."},
              "b":{"type":"array","items":{"$ref":"#/x","type":"object","properties":{},"additionalProperties":{"type":"string"}}},
              "c":{"type":"object","properties":{"d":{"type":"integer","minimum":1,"const":1}}}},
             "required":["a","ghost"]}
            """;
        using var parsed = JsonDocument.Parse(schema);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            GeminiToolExporter.WriteReduced(w, parsed.RootElement);
        }

        using var reduced = JsonDocument.Parse(stream.ToArray());
        var root = reduced.RootElement;
        AssertGeminiSchema(root);
        Assert.False(root.TryGetProperty("$schema", out _));
        Assert.Equal(["a"], root.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        var a = root.GetProperty("properties").GetProperty("a");
        Assert.Equal("string", a.GetProperty("type").GetString());
        Assert.True(a.GetProperty("nullable").GetBoolean());
        Assert.False(a.TryGetProperty("default", out _));
        var items = root.GetProperty("properties").GetProperty("b").GetProperty("items");
        Assert.False(items.TryGetProperty("properties", out _));
        Assert.False(items.TryGetProperty("$ref", out _));
        var d = root.GetProperty("properties").GetProperty("c").GetProperty("properties").GetProperty("d");
        Assert.Equal(1, d.GetProperty("minimum").GetInt32());
        Assert.False(d.TryGetProperty("const", out _));
    }

    [Fact]
    public void MarkdownHasOneHeadingAndOneExamplePerTool()
    {
        var text = MarkdownToolExporter.Export();
        var headings = text.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).Select(l => l[3..]).ToList();
        Assert.Equal(ToolCatalog.McpTools.Select(t => t.McpName!), headings);
        Assert.Single(text.Split('\n'), l => l.StartsWith("# ", StringComparison.Ordinal));
        Assert.Equal(McpToolCount, Regex.Matches(text, "^```json$", RegexOptions.Multiline).Count);

        // Every example parses and carries exactly the required fields.
        var examples = Regex.Matches(text, "```json\n(.*?)\n```", RegexOptions.Singleline);
        foreach (Match m in examples)
        {
            using var example = JsonDocument.Parse(m.Groups[1].Value);
            var info = ToolCatalog.FindByMcpName(example.RootElement.GetProperty("tool").GetString()!)!;
            using var schema = JsonDocument.Parse(info.Schema);
            var required = schema.RootElement.TryGetProperty("required", out var r) ? r.EnumerateArray().Select(x => x.GetString()).ToList() : [];
            Assert.Equal(required, example.RootElement.GetProperty("args").EnumerateObject().Select(p => p.Name));
        }

        Assert.Contains("| `title.text` | string | yes |", text, StringComparison.Ordinal);
        Assert.Contains("| `ranges[].start` | integer, at least 0 | yes |", text, StringComparison.Ordinal);
        Assert.Contains("\"clipIds\": [\n      \"<clipId>\"\n    ]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOpenApiDocumentIsStructurallyValid()
    {
        using var doc = JsonDocument.Parse(OpenApiDocument.Build(GoldenVersion));
        var root = doc.RootElement;
        Assert.Equal("3.1.0", root.GetProperty("openapi").GetString());
        Assert.Equal(GoldenVersion, root.GetProperty("info").GetProperty("version").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("info").GetProperty("title").GetString()));
        Assert.Equal("bearer", root.GetProperty("components").GetProperty("securitySchemes").GetProperty("bearerAuth").GetProperty("scheme").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("security")[0].GetProperty("bearerAuth").ValueKind);

        var paths = root.GetProperty("paths");
        var operationIds = new HashSet<string>(StringComparer.Ordinal);
        var methods = new[] { "get", "put", "post", "delete", "options", "head", "patch", "trace" };
        foreach (var path in paths.EnumerateObject())
        {
            Assert.StartsWith("/v1/", path.Name, StringComparison.Ordinal);
            var segments = Regex.Matches(path.Name, "\\{([^}]+)\\}").Select(m => m.Groups[1].Value).ToList();
            foreach (var op in path.Value.EnumerateObject())
            {
                Assert.Contains(op.Name, methods);
                Assert.True(operationIds.Add(op.Value.GetProperty("operationId").GetString()!), op.Value.GetProperty("operationId").GetString());
                Assert.True(op.Value.GetProperty("responses").TryGetProperty("200", out var ok));
                Assert.False(string.IsNullOrWhiteSpace(ok.GetProperty("description").GetString()));
                Assert.True(op.Value.GetProperty("responses").TryGetProperty("default", out _));
                var pathParams = op.Value.GetProperty("parameters").EnumerateArray()
                    .Where(p => p.TryGetProperty("in", out var location) && location.GetString() == "path")
                    .ToList();
                Assert.Equal(segments, pathParams.Select(p => p.GetProperty("name").GetString()));
                Assert.All(pathParams, p => Assert.True(p.GetProperty("required").GetBoolean()));
                Assert.All(pathParams, p => Assert.True(p.GetProperty("schema").TryGetProperty("type", out _)));
                if (op.Name is "get" or "delete")
                {
                    Assert.False(op.Value.TryGetProperty("requestBody", out _));
                }
            }
        }

        // Every catalog route has its operation, with the extensions and the lock header where the lock is needed.
        foreach (var tool in ToolCatalog.All)
        {
            Assert.True(paths.TryGetProperty(tool.Route, out var item), tool.Route);
            Assert.True(item.TryGetProperty(tool.Method.ToLowerInvariant(), out var op), tool.Method + " " + tool.Route);
            Assert.Equal(tool.Name, op.GetProperty("x-cutlass-tool").GetString());
            Assert.Equal(tool.NeedsLock, op.GetProperty("x-cutlass-needs-lock").GetBoolean());
            Assert.Equal(tool.Mutating, op.GetProperty("x-cutlass-mutating").GetBoolean());
            Assert.Equal(tool.NeedsLock, op.GetProperty("parameters").EnumerateArray().Any(p => p.TryGetProperty("$ref", out var r) && r.GetString() == "#/components/parameters/LockHeader"));
            Assert.Equal(tool.Method == "POST", op.TryGetProperty("requestBody", out _));
        }

        Assert.True(paths.GetProperty(Router.CallRoute).TryGetProperty("post", out _));

        // Query parameters of GET routes come from the schema; arrays are comma separated.
        var project = paths.GetProperty("/v1/project").GetProperty("get").GetProperty("parameters").EnumerateArray().ToList();
        var trackIds = project.Single(p => p.TryGetProperty("name", out var n) && n.GetString() == "trackIds");
        Assert.Equal("query", trackIds.GetProperty("in").GetString());
        Assert.Equal("form", trackIds.GetProperty("style").GetString());
        Assert.False(trackIds.GetProperty("explode").GetBoolean());

        // Every $ref resolves inside the document.
        var refs = new List<string>();
        CollectRefs(root, refs);
        Assert.NotEmpty(refs);
        foreach (var reference in refs.Distinct(StringComparer.Ordinal))
        {
            Assert.StartsWith("#/", reference, StringComparison.Ordinal);
            var node = root;
            foreach (var part in reference[2..].Split('/'))
            {
                Assert.True(node.TryGetProperty(part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal), out node), reference);
            }
        }
    }

    [Fact]
    public void TheMcpInstructionsAreTheNormativeTextOfTheSpec()
    {
        var spec = File.ReadAllText(Path.Combine(Golden.RepositoryRoot(), "docs", "AGENT_INTEGRATION.md")).ReplaceLineEndings("\n");
        var section = spec[spec.IndexOf("### 5.4 ", StringComparison.Ordinal)..];
        var start = section.IndexOf("```\n", StringComparison.Ordinal) + 4;
        var end = section.IndexOf("\n```", start, StringComparison.Ordinal);
        Assert.Equal(section[start..end], AgentInstructions.Mcp());
    }

    [Fact]
    public void TheHttpInstructionsCarryTheCallContractAndNameNoVendor()
    {
        var text = AgentInstructions.Http();
        Assert.DoesNotContain('\r', text);
        Assert.Contains("wait (waitMs)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("waitSeconds", text, StringComparison.Ordinal);
        Assert.Contains("POST <baseUrl>v1/call", text, StringComparison.Ordinal);
        Assert.Contains("X-Cutlass-Lock", text, StringComparison.Ordinal);
        Assert.Contains("pngBase64", text, StringComparison.Ordinal);
        Assert.StartsWith(AgentInstructions.Mcp().Split('\n')[0], text, StringComparison.Ordinal);
        foreach (var vendor in new[] { "Claude", "Anthropic", "OpenAI", "Gemini", "Cursor", "Copilot" })
        {
            Assert.DoesNotContain(vendor, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertGeminiSchema(JsonElement schema)
    {
        foreach (var p in schema.EnumerateObject())
        {
            Assert.True(GeminiToolExporter.Keywords.Contains(p.Name), p.Name);
            Assert.NotEqual(JsonValueKind.Array, p.Name == "type" ? p.Value.ValueKind : JsonValueKind.Undefined);
            if (p.Name == "properties")
            {
                Assert.True(p.Value.EnumerateObject().Any());
                foreach (var child in p.Value.EnumerateObject())
                {
                    AssertGeminiSchema(child.Value);
                }
            }
            else if (p.Name == "items")
            {
                AssertGeminiSchema(p.Value);
            }
        }
    }

    private static void CollectRefs(JsonElement element, List<string> refs)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                {
                    if (p.Name == "$ref")
                    {
                        refs.Add(p.Value.GetString()!);
                    }
                    else
                    {
                        CollectRefs(p.Value, refs);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectRefs(item, refs);
                }

                break;
        }
    }

    [GeneratedRegex("^[a-zA-Z0-9_-]{1,64}$")]
    private static partial Regex OpenAiName();
}
