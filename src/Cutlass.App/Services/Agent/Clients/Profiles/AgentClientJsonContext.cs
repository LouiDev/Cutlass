using System.Text.Json.Serialization;

namespace Cutlass.App.Services.Agent.Clients.Profiles;

/// <summary>
/// The generated JSON reader for the client profile files (AGENT_INTEGRATION 9.3) and the small client state file:
/// camelCase names, comments and trailing commas allowed, unknown members ignored.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AgentClientCatalog))]
[JsonSerializable(typeof(AgentClientProfile))]
[JsonSerializable(typeof(AgentClientProfile[]))]
[JsonSerializable(typeof(AgentClientStateFile))]
public sealed partial class AgentClientJsonContext : JsonSerializerContext;
