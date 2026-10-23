using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cutlass.App.Services.Agent.Clients.Profiles;

/// <summary>
/// Reads and writes <see cref="AgentClientPath"/>: a JSON string is the path for every platform; an object names the
/// platforms (<c>windows</c>, <c>macos</c>, <c>linux</c>, <c>default</c>). Unknown members are skipped.
/// </summary>
public sealed class AgentClientPathConverter : JsonConverter<AgentClientPath>
{
    /// <inheritdoc />
    public override AgentClientPath? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return new AgentClientPath(reader.GetString());
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A client path is a string or an object with windows, macos and linux.");
        }

        string? fallback = null, windows = null, macos = null, linux = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Unexpected token in a client path.");
            }

            var name = reader.GetString() ?? string.Empty;
            reader.Read();
            if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.Null))
            {
                reader.Skip();
                continue;
            }

            var value = reader.GetString();
            switch (name.ToLowerInvariant())
            {
                case "default":
                    fallback = value;
                    break;
                case "windows":
                    windows = value;
                    break;
                case "macos" or "osx" or "mac":
                    macos = value;
                    break;
                case "linux":
                    linux = value;
                    break;
            }
        }

        return new AgentClientPath(fallback, windows, macos, linux);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, AgentClientPath value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Windows is null && value.MacOS is null && value.Linux is null)
        {
            writer.WriteStringValue(value.Default);
            return;
        }

        writer.WriteStartObject();
        WriteIfSet(writer, "default", value.Default);
        WriteIfSet(writer, "windows", value.Windows);
        WriteIfSet(writer, "macos", value.MacOS);
        WriteIfSet(writer, "linux", value.Linux);
        writer.WriteEndObject();
    }

    private static void WriteIfSet(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }
}
