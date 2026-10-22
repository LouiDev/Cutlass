using System.Text.Json.Nodes;

namespace Cutlass.Agent.Mcp.Tools;

/// <summary>
/// The project group: <c>cutlass_save_project</c> saves in place, or saves as when given a path (5.2), so it maps to
/// <c>project.save</c> or <c>project.save-as</c>. The describe text is returned as plain text by the default shape.
/// </summary>
public sealed partial class CutlassTools
{
    private static void AddProjectAdapters(Dictionary<string, ToolAdapter> adapters)
    {
        adapters["cutlass_save_project"] = new ToolAdapter(
            MapArguments: args =>
            {
                if (args.TryGetPropertyValue("path", out var path) && path is not null)
                {
                    return "project.save-as";
                }

                args.Remove("overwrite");
                return "project.save";
            },
            ShapeResult: null,
            ExtendSchema: schema =>
            {
                AddProperty(schema, "path", "string", "Save as this new .cutlass path (inside the write folders). Omit to save to the current path.");
                AddProperty(schema, "overwrite", "boolean", "With path: replace an existing file (false).");
            });
    }
}
