namespace Cutlass.Agent.Api.Export;

/// <summary>
/// <c>GET /v1/tools?format=anthropic</c> (AGENT_INTEGRATION 9.2): <c>[{"name", "description", "input_schema"}]</c>,
/// the <c>tools</c> list of the Anthropic Messages API. Names are the MCP names; the catalog schema passes through
/// unchanged. Pure.
/// </summary>
public static class AnthropicToolExporter
{
    /// <summary>Exports the tools.</summary>
    /// <param name="tools">The tools, or null for the catalog; tools without an MCP name are left out.</param>
    /// <returns>The JSON text.</returns>
    public static string Export(IEnumerable<ToolInfo>? tools = null) =>
        ExportJson.Write(w =>
        {
            w.WriteStartArray();
            foreach (var tool in ExportJson.VendorTools(tools))
            {
                w.WriteStartObject();
                w.WriteString("name", tool.McpName);
                w.WriteString("description", tool.Description);
                w.WritePropertyName("input_schema");
                ExportJson.Schema(tool).WriteTo(w);
                w.WriteEndObject();
            }

            w.WriteEndArray();
        });
}
