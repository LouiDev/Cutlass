using System.Globalization;
using System.Text.Json.Nodes;
using Cutlass.Core.Model;

namespace Cutlass.Core.Serialization;

/// <summary>
/// Upgrades project JSON from older <c>schemaVersion</c>s to <see cref="Project.CurrentSchemaVersion"/> before it is
/// mapped to the model (TECH_BRIEF 3.5). Each step upgrades one version in place.
/// </summary>
/// <remarks>
/// Schema 0 is the pre-release draft format (files without <c>schemaVersion</c>): it had no <c>format</c> marker,
/// stored the sequence rate as <c>fps</c> (a number or a rational string), could omit a track's <c>transitions</c>
/// and the sequence's <c>markers</c>, and wrote clips without <c>type</c>. The brief leaves the history of the format
/// open; this is the simplest consistent definition of the one step that exists.
/// </remarks>
public static class ProjectMigrator
{
    /// <summary>Reads the schema version of a project document: <c>schemaVersion</c>, or 0 when missing.</summary>
    /// <param name="root">The parsed document.</param>
    /// <returns>The version.</returns>
    /// <exception cref="InvalidDataException">When the version is not an integer.</exception>
    public static int ReadVersion(JsonNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var node = root["schemaVersion"];
        if (node is null)
        {
            return 0;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            throw new InvalidDataException("The project's schemaVersion is not a whole number.", ex);
        }
    }

    /// <summary>Upgrades a project document to the current schema, in place.</summary>
    /// <param name="root">The parsed document (a JSON object).</param>
    /// <returns>The same node, upgraded.</returns>
    /// <exception cref="InvalidDataException">
    /// When the document is not an object, or was written by a newer Cutlass (a version above the current one).
    /// </exception>
    public static JsonNode Migrate(JsonNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root is not JsonObject obj)
        {
            throw new InvalidDataException("A project file must contain a JSON object.");
        }

        var version = ReadVersion(obj);
        if (version > Project.CurrentSchemaVersion)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"This project was saved by a newer version of Cutlass (schema {version}; this version reads up to {Project.CurrentSchemaVersion})."));
        }

        if (version < 0)
        {
            throw new InvalidDataException("The project's schemaVersion is negative.");
        }

        if (version == 0)
        {
            V0ToV1(obj);
        }

        return obj;
    }

    private static void V0ToV1(JsonObject root)
    {
        root["format"] ??= ProjectSerializer.FormatName;
        if (root["sequence"] is JsonObject sequence)
        {
            if (sequence["frameRate"] is null && sequence["fps"] is { } fps)
            {
                sequence.Remove("fps");
                sequence["frameRate"] = fps.GetValueKind() switch
                {
                    System.Text.Json.JsonValueKind.Number => fps.GetValue<double>().ToString("R", CultureInfo.InvariantCulture),
                    System.Text.Json.JsonValueKind.String => fps.GetValue<string>(),
                    _ => throw new InvalidDataException("The project's fps is neither a number nor a text."),
                };
            }

            sequence["markers"] ??= new JsonArray();
            if (sequence["tracks"] is JsonArray tracks)
            {
                foreach (var track in tracks.OfType<JsonObject>())
                {
                    track["transitions"] ??= new JsonArray();
                    if (track["clips"] is JsonArray clips)
                    {
                        foreach (var clip in clips.OfType<JsonObject>())
                        {
                            clip["type"] ??= clip["mediaId"] is null ? "title" : "media";
                        }
                    }
                }
            }
        }

        root["schemaVersion"] = 1;
    }
}
