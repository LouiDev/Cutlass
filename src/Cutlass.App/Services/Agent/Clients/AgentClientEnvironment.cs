using Cutlass.App.Services.Agent.Clients.Profiles;

namespace Cutlass.App.Services.Agent.Clients;

/// <summary>
/// Where the client registry looks (AGENT_INTEGRATION 9.3): the home folder (<c>{home}</c>), the per-user application
/// data folder (<c>{appdata}</c>), the PATH it searches for client commands and the platform its profile paths
/// resolve for. <see cref="Current"/> is the real user; tests and smoke runs use <see cref="Sandbox"/>, so they never
/// read or write a real client configuration.
/// </summary>
/// <param name="Home">The home folder.</param>
/// <param name="AppData">The application data folder (Windows: roaming AppData; elsewhere <c>~/.config</c>).</param>
/// <param name="PathVariable">The PATH value searched for commands.</param>
/// <param name="Platform">The platform the profile paths resolve for.</param>
/// <param name="IsSandbox">True for a fake home (smoke runs and tests): only a fake CLI runner may be used with it.</param>
public sealed record AgentClientEnvironment(string Home, string AppData, string PathVariable, AgentClientPlatform Platform, bool IsSandbox = false)
{
    /// <summary>The running platform.</summary>
    public static AgentClientPlatform CurrentPlatform =>
        OperatingSystem.IsWindows() ? AgentClientPlatform.Windows
        : OperatingSystem.IsMacOS() ? AgentClientPlatform.MacOS
        : AgentClientPlatform.Linux;

    /// <summary>True when commands are looked up with the Windows rules (<c>.exe</c>, <c>.cmd</c>, <c>.bat</c>).</summary>
    public bool IsWindows => Platform == AgentClientPlatform.Windows;

    /// <summary>The real user's folders and PATH.</summary>
    /// <returns>The environment.</returns>
    public static AgentClientEnvironment Current()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var platform = CurrentPlatform;
        var appData = platform == AgentClientPlatform.Windows
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".config");
        return new AgentClientEnvironment(home, appData, Environment.GetEnvironmentVariable("PATH") ?? string.Empty, platform);
    }

    /// <summary>
    /// A fake home for smoke runs and tests: <c>{appdata}</c> is <c>&lt;home&gt;/AppData/Roaming</c> on Windows and
    /// <c>&lt;home&gt;/.config</c> elsewhere, and PATH holds only <c>&lt;home&gt;/bin</c>.
    /// </summary>
    /// <param name="home">The fake home folder.</param>
    /// <param name="platform">The platform (null: the running one).</param>
    /// <returns>The environment.</returns>
    public static AgentClientEnvironment Sandbox(string home, AgentClientPlatform? platform = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        var full = Path.GetFullPath(home);
        var p = platform ?? CurrentPlatform;
        var appData = p == AgentClientPlatform.Windows ? Path.Combine(full, "AppData", "Roaming") : Path.Combine(full, ".config");
        return new AgentClientEnvironment(full, appData, Path.Combine(full, "bin"), p, IsSandbox: true);
    }
}
