using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cutlass.Agent.Api.Export;

/// <summary>
/// Shared plumbing of the tool exporters (AGENT_INTEGRATION 9.2): indented JSON with <c>\n</c> line ends and the
/// relaxed encoder, so every document is byte-identical on every platform and readable in a golden diff.
/// </summary>
internal static class ExportJson
{
    /// <summary>Writer options of every exported JSON document.</summary>
    public static JsonWriterOptions WriterOptions => new() { Encoder = AgentJsonContext.Encoder, Indented = true, NewLine = "\n" };

    /// <summary>Writes a document.</summary>
    /// <param name="write">Writes the root value.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, WriterOptions))
        {
            write(w);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes a node tree.</summary>
    /// <param name="node">The root.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(JsonNode node) => Write(w => node.WriteTo(w));

    /// <summary>The tools a vendor format lists: those with an MCP name, in catalog order.</summary>
    /// <param name="tools">The tools, or null for the whole catalog.</param>
    /// <returns>The tools.</returns>
    public static IEnumerable<ToolInfo> VendorTools(IEnumerable<ToolInfo>? tools) =>
        (tools ?? ToolCatalog.All).Where(t => t.McpName is not null);

    /// <summary>Parses a tool's argument schema.</summary>
    /// <param name="tool">The tool.</param>
    /// <returns>The schema.</returns>
    public static JsonElement Schema(ToolInfo tool)
    {
        using var doc = JsonDocument.Parse(tool.Schema);
        return doc.RootElement.Clone();
    }

    /// <summary>The wire name of a result kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns><c>json</c>, <c>png</c>, <c>text</c> or <c>document</c>.</returns>
    public static string ResultName(ToolResultKind kind) => kind switch
    {
        ToolResultKind.Png => "png",
        ToolResultKind.Text => "text",
        ToolResultKind.Document => "document",
        _ => "json",
    };
}
