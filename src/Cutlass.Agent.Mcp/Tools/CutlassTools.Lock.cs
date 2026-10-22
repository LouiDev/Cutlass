using System.Text.Json.Nodes;

namespace Cutlass.Agent.Mcp.Tools;

/// <summary>The lock group: <c>cutlass_acquire_lock</c> takes <c>waitSeconds</c> (5.2) where the route takes <c>waitMs</c>.</summary>
public sealed partial class CutlassTools
{
    /// <summary>The longest acquire wait the route accepts (3.2).</summary>
    private const int MaxAcquireWaitMs = 120_000;

    private static void AddLockAdapters(Dictionary<string, ToolAdapter> adapters)
    {
        adapters["cutlass_acquire_lock"] = new ToolAdapter(
            MapArguments: args =>
            {
                SecondsToMilliseconds(args, "waitSeconds", "waitMs", MaxAcquireWaitMs);
                return "lock.acquire";
            },
            ShapeResult: null,
            ExtendSchema: schema =>
            {
                RemoveProperty(schema, "waitMs");
                AddProperty(
                    schema,
                    "waitSeconds",
                    "number",
                    "When the user is busy or another agent holds the project, wait up to this many seconds (at most 120) instead of failing with busy.",
                    0,
                    MaxAcquireWaitMs / 1000);
            });
    }
}
