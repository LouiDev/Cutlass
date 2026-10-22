using System.Text;
using System.Text.Json;

namespace Cutlass.Agent.Api;

/// <summary>Builds the small JSON schemas of the tool arguments (an object with typed properties).</summary>
public sealed class SchemaBuilder
{
    private readonly List<(string Name, Action<Utf8JsonWriter> Write, bool Required)> _properties = [];

    /// <summary>An integer property.</summary>
    /// <param name="name">The name.</param>
    /// <param name="description">The description.</param>
    /// <param name="required">True when required.</param>
    /// <param name="min">The minimum, or null.</param>
    /// <param name="max">The maximum, or null.</param>
    /// <returns>This builder.</returns>
    public SchemaBuilder Int(string name, string description, bool required = false, long? min = null, long? max = null) =>
        Add(name, required, w =>
        {
            w.WriteString("type", "integer");
            w.WriteString("description", description);
            if (min is { } lo)
            {
                w.WriteNumber("minimum", lo);
            }

            if (max is { } hi)
            {
                w.WriteNumber("maximum", hi);
            }
        });

    /// <summary>A nullable integer property (<c>null</c> clears).</summary>
    /// <param name="name">The name.</param>
    /// <param name="description">The description.</param>
    /// <returns>This builder.</returns>
    public SchemaBuilder IntOrNull(string name, string description) =>
        Add(name, false, w =>
        {
            w.WriteStartArray("type");
            w.WriteStringValue("integer");
            w.WriteStringValue("null");
            w.WriteEndArray();
            w.WriteString("description", description);
        });

    /// <summary>A number property.</summary>
    /// <param name="name">The name.</param>
    /// <param name="description">The description.</param>
    /// <param name="required">True when required.</param>
    /// <returns>This builder.</returns>
    public SchemaBuilder Num(string name, string description, bool required = false) =>
        Add(name, required, w =>
        {
            w.WriteString("type", "number");
            w.WriteString("description", description);
        });

    /// <summary>A boolean property.</summary>
    /// <param name="name">The name.</param>
    /// <param name="description">The description.</param>
    /// <param name="required">True when required.</param>
    /// <returns>This builder.</returns>
    public SchemaBuilder Bool(string name, string description, bool required = false) =>
        Add(name, required, w =>
        {
            w.WriteString("type", "boolean");
            w.WriteString("description", description);
        });

    /// <summary>A string property, optionally limited to values.</summary>
    /// <param name="name">The name.</param>
    /// <param name="description">The description.</param>
    /// <param name="required">True when required.</param>
    /// <param name="values">The allowed values, or null.</param>
    /// <returns>This builder.</returns>
    public SchemaBuilder Str(string name, string description, bool required = false, params string[]? values) =>
        Add(name, required, w =>
        {
            w.WriteString("type", "string");
            w.WriteString("description", description);
            if (values is { Length: > 0 })
            {
                w.WriteStartArray("enum");
                foreach (var v in values)
                {
                    w.WriteStringValue(v);
                }

                w.WriteEndArray();
            }
        });

    /// <summary>An array of strings.</summary>
    /// <param name="name">The name.</param>
    /// <param name="description">The description.</param>
    /// <param name="required">True when required.</param>
    /// <param name="maxItems">The most items, or null.</param>
    /// <returns>This builder.</returns>
    public SchemaBuilder StrArray(string name, string description, bool required = false, int? maxItems = null) =>
        Add(name, required, w =>
        {
            w.WriteString("type", "array");
            w.WriteString("description", description);
            w.WriteStartObject("items");
            w.WriteString("type", "string");
            w.WriteEndObject();
            if (maxItems is { } max)
            {
                w.WriteNumber("maxItems", max);
            }
        });

    /// <summary>An array of integers.</summary>
    /// <param name="name">The name.</param>
    /// <param name="description">The description.</param>
    /// <param name="required">True when required.</param>
    /// <param name="maxItems">The most items, or null.</param>
    /// <returns>This builder.</returns>
    public SchemaBuilder IntArray(string name, string description, bool required = false, int? maxItems = null) =>
        Add(name, required, w =>
        {
            w.WriteString("type", "array");
            w.WriteString("description", description);
            w.WriteStartObject("items");
            w.WriteString("type", "integer");
            w.WriteEndObject();
            if (maxItems is { } max)
            {
                w.WriteNumber("maxItems", max);
            }
        });

    /// <summary>A nested object.</summary>
    /// <param name="name">The name.</param>
    /// <param name="description">The description.</param>
    /// <param name="inner">The object's properties.</param>
    /// <param name="required">True when required.</param>
    /// <returns>This builder.</returns>
    public SchemaBuilder Obj(string name, string description, SchemaBuilder inner, bool required = false) =>
        Add(name, required, w =>
        {
            w.WriteString("description", description);
            inner.WriteBody(w);
        });

    /// <summary>An array of objects.</summary>
    /// <param name="name">The name.</param>
    /// <param name="description">The description.</param>
    /// <param name="item">The item's properties.</param>
    /// <param name="required">True when required.</param>
    /// <param name="maxItems">The most items, or null.</param>
    /// <returns>This builder.</returns>
    public SchemaBuilder ObjArray(string name, string description, SchemaBuilder item, bool required = false, int? maxItems = null) =>
        Add(name, required, w =>
        {
            w.WriteString("type", "array");
            w.WriteString("description", description);
            w.WriteStartObject("items");
            item.WriteBody(w);
            w.WriteEndObject();
            if (maxItems is { } max)
            {
                w.WriteNumber("maxItems", max);
            }
        });

    /// <summary>The schema as JSON text.</summary>
    /// <returns>The text.</returns>
    public string Build()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, AgentJsonContext.WriterOptions))
        {
            w.WriteStartObject();
            WriteBody(w);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private SchemaBuilder Add(string name, bool required, Action<Utf8JsonWriter> write)
    {
        _properties.Add((name, write, required));
        return this;
    }

    private void WriteBody(Utf8JsonWriter w)
    {
        w.WriteString("type", "object");
        w.WriteStartObject("properties");
        foreach (var (name, write, _) in _properties)
        {
            w.WriteStartObject(name);
            write(w);
            w.WriteEndObject();
        }

        w.WriteEndObject();
        var required = _properties.Where(p => p.Required).Select(p => p.Name).ToList();
        if (required.Count > 0)
        {
            w.WriteStartArray("required");
            foreach (var r in required)
            {
                w.WriteStringValue(r);
            }

            w.WriteEndArray();
        }
    }
}
