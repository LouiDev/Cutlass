using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Cutlass.Agent.Api.Export;

/// <summary>
/// <c>GET /v1/tools?format=markdown</c> (AGENT_INTEGRATION 9.2): a reference for people and models. One <c>##</c>
/// section per tool with an MCP name: its description, catalog name, route, lock and result, a parameter table
/// (nested fields as <c>title.text</c> and <c>ranges[].start</c>) and one example <c>/v1/call</c> body built from the
/// schema's required fields. Pure, with <c>\n</c> line ends.
/// </summary>
public static class MarkdownToolExporter
{
    private const string Fence = "```";

    /// <summary>Exports the tools.</summary>
    /// <param name="tools">The tools, or null for the catalog; tools without an MCP name are left out.</param>
    /// <returns>The Markdown text.</returns>
    public static string Export(IEnumerable<ToolInfo>? tools = null)
    {
        var sb = new StringBuilder();
        sb.Append("# Cutlass tools\n\n");
        sb.Append("Call any tool with `POST /v1/call`, the header `Authorization: Bearer <token>` and the body ");
        sb.Append("`{\"tool\": \"<name>\", \"args\": {...}}`. After `cutlass_acquire_lock`, send its `lockId` as the header ");
        sb.Append("`X-Cutlass-Lock`. Every reply is the envelope `{\"revision\", \"warnings\", \"result\"}` or `{\"revision\", \"error\"}`. ");
        sb.Append("Image tools return `pngBase64` in `result`. Positions are integer frames; source times are flicks ");
        sb.Append("(705600000 per second) or seconds.\n");
        foreach (var tool in ExportJson.VendorTools(tools))
        {
            var schema = ExportJson.Schema(tool);
            sb.Append("\n## ").Append(tool.McpName).Append("\n\n");
            sb.Append(tool.Description).Append("\n\n");
            sb.Append("- Catalog name `").Append(tool.Name).Append("`, route `").Append(tool.Method).Append(' ').Append(tool.Route).Append("`.\n");
            sb.Append("- ").Append(tool.NeedsLock ? "Needs the lock" : "No lock needed")
                .Append(tool.Mutating ? "; changes the project (one undo step)" : string.Empty)
                .Append(tool.Heavy ? "; a render or analysis" : string.Empty)
                .Append("; returns ").Append(ResultText(tool.Result)).Append(".\n\n");
            var rows = new List<string>();
            Rows(schema, string.Empty, rows);
            if (rows.Count == 0)
            {
                sb.Append("No parameters.\n\n");
            }
            else
            {
                sb.Append("| Parameter | Type | Required | Description |\n|---|---|---|---|\n");
                foreach (var row in rows)
                {
                    sb.Append(row).Append('\n');
                }

                sb.Append('\n');
            }

            sb.Append("Example call:\n\n").Append(Fence).Append("json\n");
            sb.Append(ExportJson.Write(w =>
            {
                w.WriteStartObject();
                w.WriteString("tool", tool.McpName);
                w.WritePropertyName("args");
                WriteExample(w, schema, null);
                w.WriteEndObject();
            }));
            sb.Append('\n').Append(Fence).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Writes example arguments for a schema: the required fields with placeholder values.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="schema">The schema.</param>
    /// <param name="name">The property name (for string placeholders), or null at the root.</param>
    public static void WriteExample(Utf8JsonWriter w, JsonElement schema, string? name)
    {
        ArgumentNullException.ThrowIfNull(w);
        switch (TypeOf(schema))
        {
            case "object":
                w.WriteStartObject();
                if (schema.TryGetProperty("properties", out var props) && schema.TryGetProperty("required", out var required))
                {
                    foreach (var r in required.EnumerateArray().Select(r => r.GetString()!))
                    {
                        if (props.TryGetProperty(r, out var property))
                        {
                            w.WritePropertyName(r);
                            WriteExample(w, property, r);
                        }
                    }
                }

                w.WriteEndObject();
                break;
            case "array":
                w.WriteStartArray();
                if (schema.TryGetProperty("items", out var items))
                {
                    WriteExample(w, items, name is null ? null : Singular(name));
                }

                w.WriteEndArray();
                break;
            case "integer":
                w.WriteNumberValue(schema.TryGetProperty("minimum", out var min) ? min.GetInt64() : 0);
                break;
            case "number":
                w.WriteNumberValue(0);
                break;
            case "boolean":
                w.WriteBooleanValue(true);
                break;
            case "null":
                w.WriteNullValue();
                break;
            default:
                if (schema.TryGetProperty("enum", out var values) && values.GetArrayLength() > 0)
                {
                    values[0].WriteTo(w);
                }
                else
                {
                    w.WriteStringValue("<" + (name ?? "value") + ">");
                }

                break;
        }
    }

    private static string ResultText(ToolResultKind kind) => kind switch
    {
        ToolResultKind.Png => "an image (`pngBase64` through `/v1/call`)",
        ToolResultKind.Text => "text (`result.text` through `/v1/call`)",
        ToolResultKind.Document => "a document",
        _ => "JSON",
    };

    private static void Rows(JsonElement schema, string prefix, List<string> rows)
    {
        if (!schema.TryGetProperty("properties", out var props))
        {
            return;
        }

        var required = schema.TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(r => r.GetString()).ToHashSet(StringComparer.Ordinal)
            : [];
        foreach (var p in props.EnumerateObject())
        {
            var name = prefix + p.Name;
            var description = p.Value.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty;
            rows.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"| `{name}` | {Cell(TypeText(p.Value))} | {(required.Contains(p.Name) ? "yes" : "no")} | {Cell(description)} |"));
            var type = TypeOf(p.Value);
            if (type == "object")
            {
                Rows(p.Value, name + ".", rows);
            }
            else if (type == "array" && p.Value.TryGetProperty("items", out var items) && TypeOf(items) == "object")
            {
                Rows(items, name + "[].", rows);
            }
        }
    }

    private static string TypeOf(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type))
        {
            return schema.TryGetProperty("properties", out _) ? "object" : "string";
        }

        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(t => t.GetString()).FirstOrDefault(t => t != "null") ?? "null"
            : type.GetString() ?? "string";
    }

    private static string TypeText(JsonElement schema)
    {
        var text = schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Array
            ? string.Join(" or ", type.EnumerateArray().Select(t => t.GetString()))
            : TypeOf(schema);
        if (TypeOf(schema) == "array" && schema.TryGetProperty("items", out var items))
        {
            text = "array of " + TypeOf(items);
        }

        if (schema.TryGetProperty("enum", out var values))
        {
            text += ": " + string.Join(", ", values.EnumerateArray().Select(v => v.GetString()));
        }

        var min = schema.TryGetProperty("minimum", out var lo) ? lo.GetRawText() : null;
        var max = schema.TryGetProperty("maximum", out var hi) ? hi.GetRawText() : null;
        if (min is not null || max is not null)
        {
            text += max is null ? ", at least " + min : min is null ? ", at most " + max : ", " + min + " to " + max;
        }

        if (schema.TryGetProperty("maxItems", out var most))
        {
            text += ", at most " + most.GetRawText() + " items";
        }

        return text;
    }

    private static string Singular(string name) =>
        name.EndsWith("Ids", StringComparison.Ordinal) ? name[..^1]
        : name.EndsWith('s') && name.Length > 1 ? name[..^1]
        : name;

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
