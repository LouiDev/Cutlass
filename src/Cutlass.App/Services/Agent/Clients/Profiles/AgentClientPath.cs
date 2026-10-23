using System.Text.Json.Serialization;

namespace Cutlass.App.Services.Agent.Clients.Profiles;

/// <summary>
/// A path in a client profile (AGENT_INTEGRATION 9.3): one text for every platform, or one per platform where the
/// client keeps its files in different places (for example the per-user application data folder). In JSON it is a
/// string or an object with <c>windows</c>, <c>macos</c>, <c>linux</c> and an optional <c>default</c>. The text may hold
/// the placeholders <c>{home}</c> and <c>{appdata}</c>.
/// </summary>
/// <param name="Default">The path for platforms without their own entry, or null.</param>
/// <param name="Windows">The Windows path, or null.</param>
/// <param name="MacOS">The macOS path, or null.</param>
/// <param name="Linux">The Linux path, or null.</param>
[JsonConverter(typeof(AgentClientPathConverter))]
public sealed record AgentClientPath(string? Default, string? Windows = null, string? MacOS = null, string? Linux = null)
{
    /// <summary>The path for a platform: its own entry, else the default.</summary>
    /// <param name="platform">The platform.</param>
    /// <returns>The unexpanded path, or null when the profile has none for this platform.</returns>
    public string? For(AgentClientPlatform platform) => platform switch
    {
        AgentClientPlatform.Windows => Windows ?? Default,
        AgentClientPlatform.MacOS => MacOS ?? Default,
        _ => Linux ?? Default,
    };
}
