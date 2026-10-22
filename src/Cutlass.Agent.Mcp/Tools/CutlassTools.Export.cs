using System.Text.Json.Nodes;

namespace Cutlass.Agent.Mcp.Tools;

/// <summary>The export group: <c>cutlass_export_status</c> takes <c>waitSeconds</c> (5.2) where the route takes <c>waitMs</c>.</summary>
public sealed partial class CutlassTools
{
    /// <summary>The longest export status wait the route accepts (4.4), below client timeouts.</summary>
    private const int MaxExportWaitMs = 25_000;

    private static void AddExportAdapters(Dictionary<string, ToolAdapter> adapters)
    {
        adapters["cutlass_export_status"] = new ToolAdapter(
            MapArguments: args =>
            {
                SecondsToMilliseconds(args, "waitSeconds", "waitMs", MaxExportWaitMs);
                return "exports.get";
            },
            ShapeResult: null,
            ExtendSchema: schema =>
            {
                RemoveProperty(schema, "waitMs");
                AddProperty(
                    schema,
                    "waitSeconds",
                    "number",
                    "Wait up to this many seconds (at most 25) for the state to change before answering. Poll again until completed, failed or cancelled.",
                    0,
                    MaxExportWaitMs / 1000);
            });
    }
}
