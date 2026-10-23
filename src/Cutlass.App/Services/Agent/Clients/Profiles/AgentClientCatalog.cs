namespace Cutlass.App.Services.Agent.Clients.Profiles;

/// <summary>
/// The root of a profile file (AGENT_INTEGRATION 9.3): the client profiles and the <c>/v1/tools?format=</c> values the
/// Settings page lists as discovery URLs. A user file may instead hold a single profile or an array of profiles.
/// </summary>
/// <param name="Version">The file format version (1).</param>
/// <param name="Clients">The profiles in display order.</param>
/// <param name="ToolFormats">The tool definition formats the server offers, or null.</param>
public sealed record AgentClientCatalog(int Version, IReadOnlyList<AgentClientProfile> Clients, IReadOnlyList<string>? ToolFormats = null)
{
    /// <summary>An empty catalog.</summary>
    public static AgentClientCatalog Empty { get; } = new(1, []);
}
