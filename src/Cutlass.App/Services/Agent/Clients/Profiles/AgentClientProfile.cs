using System.Text.Json.Serialization;

namespace Cutlass.App.Services.Agent.Clients.Profiles;

/// <summary>
/// One agent client the app knows (AGENT_INTEGRATION 9.3): how to detect it, how to register the Cutlass MCP server
/// with it (its command line tool, or an entry merged into its configuration file) and which briefing tail it gets.
/// Profiles are data: the shipped ones live in <c>agent-clients.json</c> beside the exe, and user files in
/// <c>&lt;data&gt;/agent/clients/*.json</c> replace them by <see cref="Id"/>. The code never names a client.
/// </summary>
/// <param name="Id">The stable id, for example <c>my-client</c>; user profiles with the same id replace shipped ones.</param>
/// <param name="Name">The display name.</param>
/// <param name="Transport"><c>mcp-stdio</c> (the client starts the MCP exe) or <c>http</c> (it calls the HTTP API).</param>
public sealed record AgentClientProfile(string Id, string Name, string Transport = AgentClientProfile.McpStdio)
{
    /// <summary>The transport of a client that starts the MCP exe over stdio.</summary>
    public const string McpStdio = "mcp-stdio";

    /// <summary>The transport of a client that calls the HTTP API itself (it needs the token).</summary>
    public const string Http = "http";

    /// <summary>How the client is detected, or null (never detected; text rows).</summary>
    public AgentClientDetect? Detect { get; init; }

    /// <summary>The command that registers the server (CLI profiles).</summary>
    public AgentClientCommand? Register { get; init; }

    /// <summary>The command that reads the registration back (CLI profiles).</summary>
    public AgentClientProbeSpec? Probe { get; init; }

    /// <summary>The command that removes the server (CLI profiles).</summary>
    public AgentClientCommand? Remove { get; init; }

    /// <summary>The registration scopes the CLI knows, the default first (for example <c>user</c> and <c>local</c>).</summary>
    public IReadOnlyList<string>? Scopes { get; init; }

    /// <summary>The configuration file (config file profiles), per platform where it differs.</summary>
    public AgentClientPath? ConfigFile { get; init; }

    /// <summary><c>json</c> (the default; comments and trailing commas are kept) or <c>toml</c>.</summary>
    public string? ConfigFormat { get; init; }

    /// <summary>The entry merged into the configuration file.</summary>
    public AgentClientConfigEntry? ConfigEntry { get; init; }

    /// <summary>The setup text a text-only row copies (placeholders allowed).</summary>
    public string? Text { get; init; }

    /// <summary>The briefing tail: <c>mcp</c>, <c>harness</c> or <c>http</c>; derived from the transport when missing.</summary>
    public string? Briefing { get; init; }

    /// <summary>The <c>/v1/tools?format=</c> value a harness loads (harness rows).</summary>
    public string? ToolsFormat { get; init; }

    /// <summary>A runnable example in the repository for this kind of client, or null.</summary>
    public string? Example { get; init; }

    /// <summary>A hint shown after registering (for example "restart the client").</summary>
    public string? Note { get; init; }

    /// <summary>How this profile registers: its CLI, its configuration file, or not at all (text only).</summary>
    [JsonIgnore]
    public AgentClientKind Kind =>
        Register is not null ? AgentClientKind.Cli
        : ConfigFile is not null && ConfigEntry is not null ? AgentClientKind.ConfigFile
        : AgentClientKind.Text;

    /// <summary>True for rows that only copy their setup text.</summary>
    [JsonIgnore]
    public bool IsTextOnly => Kind == AgentClientKind.Text;

    /// <summary>True when the client calls the HTTP API itself, so a registration or a copied text carries the token.</summary>
    [JsonIgnore]
    public bool UsesHttp => string.Equals(Transport, Http, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the configuration file is TOML.</summary>
    [JsonIgnore]
    public bool IsToml =>
        string.Equals(ConfigFormat, "toml", StringComparison.OrdinalIgnoreCase) ||
        (ConfigFormat is null && ConfigFile?.Default?.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>The briefing tail this profile gets.</summary>
    [JsonIgnore]
    public AgentClientBriefingKind BriefingKind => Briefing?.ToLowerInvariant() switch
    {
        "mcp" => AgentClientBriefingKind.Mcp,
        "harness" => AgentClientBriefingKind.Harness,
        "http" => AgentClientBriefingKind.Http,
        "generic" => AgentClientBriefingKind.Generic,
        _ => UsesHttp ? (ToolsFormat is null ? AgentClientBriefingKind.Http : AgentClientBriefingKind.Harness) : AgentClientBriefingKind.Mcp,
    };

    /// <summary>The default scope: the first of <see cref="Scopes"/>, or empty.</summary>
    [JsonIgnore]
    public string DefaultScope => Scopes is { Count: > 0 } scopes ? scopes[0] : string.Empty;

    /// <summary>"MCP (stdio)" or "HTTP".</summary>
    [JsonIgnore]
    public string TransportText => UsesHttp ? "HTTP" : "MCP (stdio)";
}
