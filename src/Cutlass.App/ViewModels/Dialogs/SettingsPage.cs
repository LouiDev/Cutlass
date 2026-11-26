namespace Cutlass.App.ViewModels.Dialogs;

/// <summary>
/// The pages of the Settings dialog (DESIGN 10), in tab order; the value is the tab index. AGENT_INTEGRATION 6.1 adds
/// Agents between Playback and About. The spec writes <c>Agents = 4</c>; the dialog and its callers treat the value as
/// the tab index, so Agents takes index 3 and About moves to 4 (the tab order the spec asks for).
/// </summary>
public enum SettingsPage
{
    /// <summary>Autosave, default durations, track deletion, export defaults, recent projects.</summary>
    General = 0,

    /// <summary>ffmpeg location, proxy policy, cache.</summary>
    Media = 1,

    /// <summary>Preview quality, audio device, proxies.</summary>
    Playback = 2,

    /// <summary>Agent connections, port, lease, folders, token and MCP setup (AGENT_INTEGRATION 6.1).</summary>
    Agents = 3,

    /// <summary>Versions, the ffmpeg build and license notes.</summary>
    About = 4,
}
