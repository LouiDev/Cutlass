using System.Text.Json;

namespace Cutlass.Agent.Api.Export;

/// <summary>
/// <c>GET /v1/tools</c> (and <c>format=json</c>, AGENT_INTEGRATION 9.2): the catalog as is, every tool including those
/// MCP does not expose. Each entry has <c>name</c>, <c>mcpName</c> (null when MCP lacks the tool), <c>description</c>,
/// <c>method</c>, <c>route</c>, <c>needsLock</c>, <c>mutating</c>, <c>heavy</c>, <c>result</c> and the argument schema
/// under <c>parameters</c>. Pure: the same catalog gives the same bytes.
/// </summary>
public static class ToolsDocument
{
    /// <summary>The document for the whole catalog.</summary>
    /// <returns>The JSON text.</returns>
    public static string Build() => Build(ToolCatalog.All);

    /// <summary>The document for some tools.</summary>
    /// <param name="tools">The tools, in the order to list them.</param>
    /// <returns>The JSON text.</returns>
    public static string Build(IEnumerable<ToolInfo> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return ExportJson.Write(w =>
        {
            w.WriteStartObject();
            w.WriteStartArray("tools");
            foreach (var tool in tools)
            {
                w.WriteStartObject();
                w.WriteString("name", tool.Name);
                if (tool.McpName is { } mcp)
                {
                    w.WriteString("mcpName", mcp);
                }
                else
                {
                    w.WriteNull("mcpName");
                }

                w.WriteString("description", tool.Description);
                w.WriteString("method", tool.Method);
                w.WriteString("route", tool.Route);
                w.WriteBoolean("needsLock", tool.NeedsLock);
                w.WriteBoolean("mutating", tool.Mutating);
                w.WriteBoolean("heavy", tool.Heavy);
                w.WriteString("result", ExportJson.ResultName(tool.Result));
                w.WritePropertyName("parameters");
                ExportJson.Schema(tool).WriteTo(w);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        });
    }
}
