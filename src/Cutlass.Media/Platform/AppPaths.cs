namespace Cutlass.Media.Platform;

/// <summary>
/// Where Cutlass keeps its settings, data and cache on this machine (TECH_BRIEF 5.1).
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>Windows</term><description><c>%APPDATA%\Cutlass\settings.json</c>, <c>%LOCALAPPDATA%\Cutlass\</c>, <c>%LOCALAPPDATA%\Cutlass\Cache\v1\</c></description></item>
/// <item><term>Linux</term><description><c>${XDG_CONFIG_HOME:-~/.config}/cutlass/settings.json</c>, <c>${XDG_DATA_HOME:-~/.local/share}/cutlass/</c>, <c>${XDG_CACHE_HOME:-~/.cache}/cutlass/v1/</c></description></item>
/// </list>
/// Every path is normalised with <see cref="Path.GetFullPath(string)"/> and stored without a trailing separator.
/// Paths compare case-insensitively only on Windows.
/// </remarks>
public sealed class AppPaths
{
    /// <summary>Creates the path set from already resolved locations.</summary>
    /// <param name="settingsFile">Full path of <c>settings.json</c>.</param>
    /// <param name="dataDirectory">Directory for recent.json and untitled autosaves.</param>
    /// <param name="cacheRoot">The versioned cache root.</param>
    /// <param name="isWindows">True to compare paths case-insensitively.</param>
    public AppPaths(string settingsFile, string dataDirectory, string cacheRoot, bool isWindows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        IsWindows = isWindows;
        SettingsFile = Normalize(settingsFile);
        DataDirectory = Normalize(dataDirectory);
        CacheRoot = Normalize(cacheRoot);
    }

    /// <summary>True when the paths follow Windows rules (case-insensitive comparison).</summary>
    public bool IsWindows { get; }

    /// <summary>Full path of <c>settings.json</c>.</summary>
    public string SettingsFile { get; }

    /// <summary>Directory holding <c>settings.json</c>.</summary>
    public string SettingsDirectory => Path.GetDirectoryName(SettingsFile)!;

    /// <summary>Directory for <c>recent.json</c> and untitled autosaves.</summary>
    public string DataDirectory { get; }

    /// <summary>The versioned cache root (<c>...\Cache\v1</c> or <c>.../cutlass/v1</c>).</summary>
    public string CacheRoot { get; }

    /// <summary>
    /// Where untitled projects autosave (DESIGN 11): <c>&lt;data&gt;/autosave</c>, holding
    /// <c>untitled-&lt;id&gt;.cutlass</c> files.
    /// </summary>
    public string AutosaveDirectory => Path.Combine(DataDirectory, "autosave");

    /// <summary>The crash and diagnostics logs, <c>&lt;data&gt;/logs</c> (<c>cutlass-&lt;date&gt;.log</c>).</summary>
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>The agent API files, <c>&lt;data&gt;/agent</c>: the bearer token and <c>endpoint.json</c> (AGENT_INTEGRATION 4.1).</summary>
    public string AgentDirectory => Path.Combine(DataDirectory, "agent");

    /// <summary>The ffmpeg work directory, <c>&lt;cache&gt;/work</c>, where every ffmpeg process runs.</summary>
    public string WorkDirectory => Path.Combine(CacheRoot, "work");

    /// <summary>
    /// The legacy shared child process file, <c>&lt;cache&gt;/pids.json</c>. Instances now write their own
    /// <c>pids-&lt;pid&gt;-&lt;start&gt;.json</c> in <see cref="CacheRoot"/>, and the startup sweep deletes this one (TECH_BRIEF 5.4).
    /// </summary>
    public string PidsFile => Path.Combine(CacheRoot, "pids.json");

    /// <summary>The capability probe cache, <c>&lt;cache&gt;/capabilities.json</c> (TECH_BRIEF 5.2).</summary>
    public string CapabilitiesFile => Path.Combine(CacheRoot, "capabilities.json");

    /// <summary>The string comparison to use for paths on this platform.</summary>
    public StringComparison PathComparison => IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The string comparer to use for path keyed collections on this platform.</summary>
    public StringComparer PathComparer => IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Resolves the paths for the operating system this process runs on.</summary>
    /// <returns>The resolved paths.</returns>
    public static AppPaths ForCurrentPlatform() =>
        Resolve(OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable, Environment.GetFolderPath);

    /// <summary>
    /// Resolves the paths from an environment. Testable: pass fake lookups to resolve another platform's layout.
    /// </summary>
    /// <param name="isWindows">True for the Windows layout, false for the XDG layout (Linux and others).</param>
    /// <param name="getEnvironmentVariable">Looks up an environment variable, returning null when unset.</param>
    /// <param name="getFolderPath">Resolves a special folder (ApplicationData, LocalApplicationData, UserProfile).</param>
    /// <returns>The resolved paths.</returns>
    public static AppPaths Resolve(
        bool isWindows,
        Func<string, string?> getEnvironmentVariable,
        Func<Environment.SpecialFolder, string> getFolderPath)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(getFolderPath);

        if (isWindows)
        {
            var roaming = getFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = getFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new AppPaths(
                Path.Combine(roaming, "Cutlass", "settings.json"),
                Path.Combine(local, "Cutlass"),
                Path.Combine(local, "Cutlass", "Cache", "v1"),
                isWindows: true);
        }

        // XDG: LocalApplicationData is not the cache on Linux, so every variable is resolved explicitly.
        var home = getFolderPath(Environment.SpecialFolder.UserProfile);
        var config = XdgDirectory(getEnvironmentVariable("XDG_CONFIG_HOME"), home, ".config");
        var data = XdgDirectory(getEnvironmentVariable("XDG_DATA_HOME"), home, Path.Combine(".local", "share"));
        var cache = XdgDirectory(getEnvironmentVariable("XDG_CACHE_HOME"), home, ".cache");
        return new AppPaths(
            Path.Combine(config, "cutlass", "settings.json"),
            Path.Combine(data, "cutlass"),
            Path.Combine(cache, "cutlass", "v1"),
            isWindows: false);
    }

    /// <summary>Normalises a path with <see cref="Path.GetFullPath(string)"/> and trims a trailing separator.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The normalised full path.</returns>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return full.Length > (root?.Length ?? 0) ? Path.TrimEndingDirectorySeparator(full) : full;
    }

    /// <summary>Compares two paths after normalisation, case-insensitively only on Windows.</summary>
    /// <param name="a">First path.</param>
    /// <param name="b">Second path.</param>
    /// <returns>True when both refer to the same location.</returns>
    public bool PathsEqual(string a, string b) => string.Equals(Normalize(a), Normalize(b), PathComparison);

    /// <summary>Creates the settings, data, cache and work directories if they do not exist.</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(WorkDirectory);
    }

    private static string XdgDirectory(string? value, string home, string fallbackRelative)
    {
        // The XDG spec says relative values are invalid and must be ignored, like unset or empty ones.
        return !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value)
            ? value
            : Path.Combine(home, fallbackRelative);
    }
}
