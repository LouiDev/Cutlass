using System.Collections.Frozen;
using System.Text.Json;

namespace Cutlass.Agent.Api.Export;

/// <summary>
/// <c>GET /v1/tools?format=gemini</c> (AGENT_INTEGRATION 9.2): <c>[{"functionDeclarations": [{"name", "description",
/// "parameters"}]}]</c>, the <c>tools</c> list of the Gemini API. Names are the MCP names. This is the only exporter
/// that rewrites schemas, because Gemini accepts an OpenAPI 3.0 subset of JSON Schema:
/// <list type="bullet">
/// <item>Only the keywords in <see cref="Keywords"/> are kept; every other one (<c>additionalProperties</c>,
/// <c>$ref</c>, <c>$schema</c>, <c>$defs</c>, <c>default</c>, <c>const</c>, <c>oneOf</c>, ...) is dropped.</item>
/// <item>A type list with <c>null</c> (<c>["integer", "null"]</c>) becomes the single type plus
/// <c>"nullable": true</c>.</item>
/// <item>An object without properties is written without <c>properties</c> (Gemini rejects an empty one), and a tool
/// without arguments has no <c>parameters</c> at all.</item>
/// <item><c>required</c> keeps only names that are properties.</item>
/// </list>
/// The catalog uses no <c>$ref</c>, so dropping it loses nothing. Pure.
/// </summary>
public static class GeminiToolExporter
{
    /// <summary>The schema keywords Gemini accepts and the export keeps.</summary>
    public static readonly FrozenSet<string> Keywords = new[]
    {
        "type", "format", "title", "description", "nullable", "enum", "items", "properties", "required",
        "minimum", "maximum", "minItems", "maxItems", "minLength", "maxLength", "pattern", "anyOf",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Exports the tools.</summary>
    /// <param name="tools">The tools, or null for the catalog; tools without an MCP name are left out.</param>
    /// <returns>The JSON text.</returns>
    public static string Export(IEnumerable<ToolInfo>? tools = null) =>
        ExportJson.Write(w =>
        {
            w.WriteStartArray();
            w.WriteStartObject();
            w.WriteStartArray("functionDeclarations");
            foreach (var tool in ExportJson.VendorTools(tools))
            {
                w.WriteStartObject();
                w.WriteString("name", tool.McpName);
                w.WriteString("description", tool.Description);
                var schema = ExportJson.Schema(tool);
                if (HasProperties(schema))
                {
                    w.WritePropertyName("parameters");
                    WriteReduced(w, schema);
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndArray();
        });

    /// <summary>Writes a schema reduced to what Gemini accepts (see the class remarks).</summary>
    /// <param name="w">The writer.</param>
    /// <param name="schema">The JSON schema.</param>
    public static void WriteReduced(Utf8JsonWriter w, JsonElement schema)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (schema.ValueKind != JsonValueKind.Object)
        {
            w.WriteStartObject();
            w.WriteEndObject();
            return;
        }

        w.WriteStartObject();
        foreach (var p in schema.EnumerateObject())
        {
            if (!Keywords.Contains(p.Name))
            {
                continue;
            }

            switch (p.Name)
            {
                case "type" when p.Value.ValueKind == JsonValueKind.Array:
                    var types = p.Value.EnumerateArray().Select(t => t.GetString()).Where(t => t is not null).ToList();
                    var single = types.FirstOrDefault(t => t != "null") ?? "string";
                    w.WriteString("type", single);
                    if (types.Contains("null"))
                    {
                        w.WriteBoolean("nullable", true);
                    }

                    break;
                case "properties":
                    if (p.Value.ValueKind == JsonValueKind.Object && p.Value.EnumerateObject().Any())
                    {
                        w.WriteStartObject("properties");
                        foreach (var property in p.Value.EnumerateObject())
                        {
                            w.WritePropertyName(property.Name);
                            WriteReduced(w, property.Value);
                        }

                        w.WriteEndObject();
                    }

                    break;
                case "items":
                    w.WritePropertyName("items");
                    WriteReduced(w, p.Value);
                    break;
                case "anyOf" when p.Value.ValueKind == JsonValueKind.Array:
                    w.WriteStartArray("anyOf");
                    foreach (var option in p.Value.EnumerateArray())
                    {
                        WriteReduced(w, option);
                    }

                    w.WriteEndArray();
                    break;
                case "required" when p.Value.ValueKind == JsonValueKind.Array:
                    var known = schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object
                        ? props.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal)
                        : [];
                    var required = p.Value.EnumerateArray().Select(r => r.GetString()).Where(r => r is not null && known.Contains(r)).ToList();
                    if (required.Count > 0)
                    {
                        w.WriteStartArray("required");
                        foreach (var r in required)
                        {
                            w.WriteStringValue(r);
                        }

                        w.WriteEndArray();
                    }

                    break;
                default:
                    p.WriteTo(w);
                    break;
            }
        }

        w.WriteEndObject();
    }

    private static bool HasProperties(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object
        && schema.TryGetProperty("properties", out var props)
        && props.ValueKind == JsonValueKind.Object
        && props.EnumerateObject().Any();
}
