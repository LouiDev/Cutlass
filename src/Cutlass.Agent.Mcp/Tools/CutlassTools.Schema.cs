using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cutlass.Agent.Api;

namespace Cutlass.Agent.Mcp.Tools;

/// <summary>Schema and argument helpers the group adapters share.</summary>
public sealed partial class CutlassTools
{
    /// <summary>Adds a property to a tool's input schema.</summary>
    /// <param name="schema">The schema object.</param>
    /// <param name="name">The property name.</param>
    /// <param name="type">The JSON type, for example <c>number</c>.</param>
    /// <param name="description">The model-facing description.</param>
    /// <param name="minimum">The minimum, or null.</param>
    /// <param name="maximum">The maximum, or null.</param>
    private static void AddProperty(JsonObject schema, string name, string type, string description, double? minimum = null, double? maximum = null)
    {
        if (schema["properties"] is not JsonObject properties)
        {
            properties = [];
            schema["properties"] = properties;
        }

        var property = new JsonObject { ["type"] = type, ["description"] = description };
        if (minimum is { } min)
        {
            property["minimum"] = min;
        }

        if (maximum is { } max)
        {
            property["maximum"] = max;
        }

        properties[name] = property;
    }

    /// <summary>Removes a property from a tool's input schema (and from its required list).</summary>
    /// <param name="schema">The schema object.</param>
    /// <param name="name">The property name.</param>
    private static void RemoveProperty(JsonObject schema, string name)
    {
        (schema["properties"] as JsonObject)?.Remove(name);
        if (schema["required"] is JsonArray required)
        {
            foreach (var item in required.Where(r => r?.GetValue<string>() == name).ToList())
            {
                required.Remove(item);
            }
        }
    }

    /// <summary>
    /// Moves a seconds argument to its milliseconds field: <paramref name="secondsField"/> is removed and
    /// <paramref name="msField"/> set to the rounded, capped value. Giving both is an <c>invalid_argument</c>.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="secondsField">For example <c>waitSeconds</c>.</param>
    /// <param name="msField">For example <c>waitMs</c>.</param>
    /// <param name="maxMs">The most milliseconds the route accepts.</param>
    private static void SecondsToMilliseconds(JsonObject args, string secondsField, string msField, int maxMs)
    {
        if (!args.TryGetPropertyValue(secondsField, out var node) || node is null)
        {
            return;
        }

        if (args.ContainsKey(msField))
        {
            throw ApiException.InvalidArgument(secondsField, $"Give {secondsField} or {msField}, not both.");
        }

        double seconds;
        try
        {
            seconds = node.GetValueKind() == JsonValueKind.String
                ? double.Parse(node.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture)
                : node.GetValue<double>();
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
        {
            throw ApiException.InvalidArgument(secondsField, $"'{secondsField}' must be a number of seconds.");
        }

        if (double.IsNaN(seconds) || seconds < 0)
        {
            throw ApiException.InvalidArgument(secondsField, $"'{secondsField}' must be 0 or more seconds.");
        }

        args.Remove(secondsField);
        args[msField] = (int)Math.Min(maxMs, Math.Round(seconds * 1000, MidpointRounding.AwayFromZero));
    }
}
