using System.Text.Json;

namespace Cutlass.App.Services.Agent.Clients.Profiles;

/// <summary>
/// The entry a configuration file profile writes (AGENT_INTEGRATION 9.3): the dotted path of the server's key (for
/// example <c>mcpServers.cutlass</c>) and its value, whose strings may hold placeholders.
/// </summary>
/// <param name="Path">The dotted path of the entry from the document's root.</param>
/// <param name="Value">The entry's value (an object).</param>
public sealed record AgentClientConfigEntry(string Path, JsonElement Value)
{
    /// <summary>The path split at its dots.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> Segments => Path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
