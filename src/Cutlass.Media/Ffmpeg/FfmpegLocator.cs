namespace Cutlass.Media.Ffmpeg;

/// <summary>
/// Finds ffmpeg (TECH_BRIEF 5.2). Search order: the settings path; <c>&lt;appdir&gt;/ffmpeg/</c>, then
/// <c>&lt;appdir&gt;</c>; each <c>PATH</c> entry; then well-known folders. A candidate counts only when ffprobe sits
/// next to it. The file system is reached through delegates so the order can be tested without real files.
/// </summary>
public sealed class FfmpegLocator
{
    private readonly string _appDirectory;
    private readonly bool _isWindows;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string, bool> _isExecutableFile;
    private readonly Func<string, bool> _directoryExists;

    /// <summary>Creates a locator.</summary>
    /// <param name="appDirectory">The folder the app runs from.</param>
    /// <param name="isWindows">True for Windows names (<c>ffmpeg.exe</c>) and folders.</param>
    /// <param name="getEnvironmentVariable">Looks up an environment variable, null when unset.</param>
    /// <param name="isExecutableFile">True when a path is an existing executable file.</param>
    /// <param name="directoryExists">True when a path is an existing folder.</param>
    public FfmpegLocator(
        string appDirectory,
        bool isWindows,
        Func<string, string?> getEnvironmentVariable,
        Func<string, bool> isExecutableFile,
        Func<string, bool> directoryExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(isExecutableFile);
        ArgumentNullException.ThrowIfNull(directoryExists);
        _appDirectory = appDirectory;
        _isWindows = isWindows;
        _getEnvironmentVariable = getEnvironmentVariable;
        _isExecutableFile = isExecutableFile;
        _directoryExists = directoryExists;
    }

    /// <summary>The executable name searched for: <c>ffmpeg.exe</c> on Windows, else <c>ffmpeg</c>.</summary>
    public string FfmpegFileName => _isWindows ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>Creates a locator for this process: the real file system, environment and app folder.</summary>
    /// <returns>The locator.</returns>
    public static FfmpegLocator ForCurrentPlatform() => new(
        AppContext.BaseDirectory,
        OperatingSystem.IsWindows(),
        Environment.GetEnvironmentVariable,
        IsExecutableOnThisPlatform,
        Directory.Exists);

    /// <summary>The ffprobe path that belongs to an ffmpeg path: the same folder and extension.</summary>
    /// <param name="ffmpegPath">The ffmpeg path.</param>
    /// <returns>The ffprobe path.</returns>
    public static string ProbePathFor(string ffmpegPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        var dir = Path.GetDirectoryName(ffmpegPath) ?? string.Empty;
        return Path.Combine(dir, "ffprobe" + Path.GetExtension(ffmpegPath));
    }

    /// <summary>
    /// The folders searched after the settings path, in order: <c>&lt;appdir&gt;/ffmpeg</c>, <c>&lt;appdir&gt;</c>,
    /// each <c>PATH</c> entry, then the well-known folders of this OS.
    /// </summary>
    /// <returns>The folders (not checked for existence, duplicates kept).</returns>
    public IReadOnlyList<string> SearchDirectories()
    {
        var dirs = new List<string>
        {
            Path.Combine(_appDirectory, "ffmpeg"),
            _appDirectory,
        };

        var pathVar = _getEnvironmentVariable("PATH") ?? string.Empty;
        var separator = _isWindows ? ';' : ':';
        foreach (var entry in pathVar.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            dirs.Add(entry.Trim('"'));
        }

        if (_isWindows)
        {
            if (_getEnvironmentVariable("LOCALAPPDATA") is { Length: > 0 } local)
            {
                dirs.Add(Path.Combine(local, "Microsoft", "WinGet", "Links"));
            }

            if (_getEnvironmentVariable("USERPROFILE") is { Length: > 0 } profile)
            {
                dirs.Add(Path.Combine(profile, "scoop", "shims"));
            }

            dirs.Add(@"C:\ProgramData\chocolatey\bin");
            dirs.Add(@"C:\ffmpeg\bin");
        }
        else
        {
            // No /snap/bin: the snap's ffprobe is named ffmpeg.ffprobe, and a confined snap cannot read ~/.cache.
            dirs.Add("/usr/bin");
            dirs.Add("/usr/local/bin");
            dirs.Add("/opt/ffmpeg/bin");
        }

        return dirs;
    }

    /// <summary>
    /// Lists every ffmpeg that exists, is executable and has ffprobe next to it, in search order, without duplicates.
    /// </summary>
    /// <param name="settingsPath">The ffmpeg executable or its folder from the settings, or null.</param>
    /// <returns>The ffmpeg paths.</returns>
    public IReadOnlyList<string> FindCandidates(string? settingsPath)
    {
        var comparer = _isWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        var result = new List<string>();

        void Consider(string ffmpeg)
        {
            string full;
            try
            {
                full = Path.GetFullPath(ffmpeg);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return;
            }

            if (seen.Add(full) && _isExecutableFile(full) && _isExecutableFile(ProbePathFor(full)))
            {
                result.Add(full);
            }
        }

        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            // The setting may name the executable itself or the folder holding it.
            Consider(_directoryExists(settingsPath) ? Path.Combine(settingsPath, FfmpegFileName) : settingsPath);
        }

        foreach (var dir in SearchDirectories())
        {
            Consider(Path.Combine(dir, FfmpegFileName));
        }

        return result;
    }

    /// <summary>
    /// Returns the first candidate (<see cref="FindCandidates"/>) that passes <see cref="FfmpegToolset.ValidateAsync"/>.
    /// </summary>
    /// <param name="settingsPath">The ffmpeg path from the settings, or null.</param>
    /// <param name="runner">Runs the validation.</param>
    /// <param name="workingDirectory">The work directory to run in.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The toolset, or null when no usable ffmpeg was found.</returns>
    public async Task<FfmpegToolset?> FindAsync(
        string? settingsPath,
        ProcessRunner runner,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        foreach (var candidate in FindCandidates(settingsPath))
        {
            try
            {
                return await FfmpegToolset.ValidateAsync(candidate, runner, workingDirectory, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (FfmpegException)
            {
                // Unusable (broken, too old): try the next one.
            }
        }

        return null;
    }

    /// <summary>
    /// The TECH_BRIEF 5.2 rule for a picked (settings) or bundled ffmpeg or ffprobe: true when the file is executable,
    /// after giving it back lost execute bits (<see cref="TryMakeExecutable"/>) unless it lies in a system folder
    /// (<c>/usr</c>, <c>/bin</c>, <c>/sbin</c>, <c>/lib</c>, <c>/etc</c>, <c>/snap</c>, <c>/nix</c>), which is never
    /// changed. Always true on Windows for an existing file. The settings dialog calls it for a typed path.
    /// </summary>
    /// <param name="path">The executable.</param>
    /// <returns>True when the file can be run.</returns>
    public static bool EnsureExecutable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((File.GetUnixFileMode(path) & anyExecute) != 0)
            {
                return true;
            }

            return !IsSystemPath(Path.GetFullPath(path)) && TryMakeExecutable(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>True for a path inside a folder the system owns (<see cref="EnsureExecutable"/> never changes it).</summary>
    /// <param name="fullPath">The absolute path.</param>
    /// <returns>True for a system path.</returns>
    public static bool IsSystemPath(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        string[] roots = ["/usr/", "/bin/", "/sbin/", "/lib/", "/lib64/", "/etc/", "/snap/", "/nix/"];
        return roots.Any(r => fullPath.StartsWith(r, StringComparison.Ordinal));
    }

    /// <summary>
    /// Gives a file the execute bits it lacks, where its read bits allow (Linux, TECH_BRIEF 5.2): a static ffmpeg build
    /// unpacked from a zip, or a bundled one copied without its mode, loses <c>+x</c>. Only files the user owns can
    /// change; anything else is left as it is. Does nothing on Windows.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>True when the file is executable afterwards (always true on Windows for an existing file).</returns>
    public static bool TryMakeExecutable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((mode & anyExecute) != 0)
            {
                return true;
            }

            // Like chmod +x under a 022 umask: execute wherever read is allowed, and always for the owner.
            var added = UnixFileMode.UserExecute;
            if ((mode & UnixFileMode.GroupRead) != 0)
            {
                added |= UnixFileMode.GroupExecute;
            }

            if ((mode & UnixFileMode.OtherRead) != 0)
            {
                added |= UnixFileMode.OtherExecute;
            }

            File.SetUnixFileMode(path, mode | added);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsExecutableOnThisPlatform(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((File.GetUnixFileMode(path) & anyExecute) != 0)
            {
                return true;
            }

            // A bundled build next to the app (published from a zip) may have lost +x; system folders are never changed.
            return path.StartsWith(AppContext.BaseDirectory, StringComparison.Ordinal) && TryMakeExecutable(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
