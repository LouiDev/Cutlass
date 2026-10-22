namespace Cutlass.Agent.Api.Export;

/// <summary>
/// <c>GET /v1/tools?format=openai</c> (AGENT_INTEGRATION 9.2): <c>[{"type": "function", "function": {"name",
/// "description", "parameters"}}]</c>, the function-calling shape of OpenAI and every OpenAI-compatible endpoint
/// (Azure OpenAI, Ollama, LM Studio, vLLM, Groq, Mistral, DeepSeek). Names are the MCP names; the catalog schema
/// passes through unchanged. Pure.
/// </summary>
public static class OpenAiToolExporter
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
                w.WriteString("type", "function");
                w.WriteStartObject("function");
                w.WriteString("name", tool.McpName);
                w.WriteString("description", tool.Description);
                w.WritePropertyName("parameters");
                ExportJson.Schema(tool).WriteTo(w);
                w.WriteEndObject();
                w.WriteEndObject();
            }

            w.WriteEndArray();
        });
}
