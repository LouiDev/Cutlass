using Cutlass.Media.Ffmpeg;

namespace Cutlass.App.Services;

/// <summary>
/// The file rules for an ffmpeg location typed or picked in Settings (TECH_BRIEF 5.2): a full path to the executable
/// or to its folder, an executable file (on Linux with an execute bit, restored by
/// <see cref="FfmpegLocator.EnsureExecutable"/> when it was lost) and ffprobe next to it. Validation of the build
/// itself (<c>-version</c>, libavfilter, filters) follows in <see cref="FfmpegToolset.ValidateAsync"/>. The file system
/// is reached through delegates so the rules can be tested without real files.
/// </summary>
public sealed class FfmpegPathCheck
{
    private readonly bool _isWindows;
    private readonly Func<string, bool> _isExecutableFile;
    private readonly Func<string, bool> _directoryExists;

    /// <summary>Creates the check.</summary>
    /// <param name="isWindows">True for Windows names (<c>ffmpeg.exe</c>).</param>
    /// <param name="isExecutableFile">True when a path is an existing executable file.</param>
    /// <param name="directoryExists">True when a path is an existing folder.</param>
    public FfmpegPathCheck(bool isWindows, Func<string, bool> isExecutableFile, Func<string, bool> directoryExists)
    {
        ArgumentNullException.ThrowIfNull(isExecutableFile);
        ArgumentNullException.ThrowIfNull(directoryExists);
        _isWindows = isWindows;
        _isExecutableFile = isExecutableFile;
        _directoryExists = directoryExists;
    }

    /// <summary>The executable name: <c>ffmpeg.exe</c> on Windows, else <c>ffmpeg</c>.</summary>
    public string FfmpegFileName => _isWindows ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>The check for this machine's file system.</summary>
    /// <returns>The check.</returns>
    public static FfmpegPathCheck ForCurrentPlatform() => new(OperatingSystem.IsWindows(), IsExecutableOnThisPlatform, Directory.Exists);

    /// <summary>Resolves a typed location to the ffmpeg executable, or explains what is wrong with it.</summary>
    /// <param name="path">The executable or its folder (surrounding quotes and spaces are ignored).</param>
    /// <param name="ffmpegPath">The full ffmpeg path when the location is acceptable.</param>
    /// <returns>Null when acceptable, else the problem in one sentence.</returns>
    public string? Check(string? path, out string? ffmpegPath)
    {
        ffmpegPath = null;
        var text = (path ?? string.Empty).Trim().Trim('"').Trim();
        if (text.Length == 0)
        {
            return "Enter the ffmpeg location, or use PATH.";
        }

        string full;
        try
        {
            if (!Path.IsPathFullyQualified(text))
            {
                return "Enter a full path to ffmpeg or its folder.";
            }

            full = Path.GetFullPath(text);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "That is not a valid path.";
        }

        var isFolder = _directoryExists(full);
        var exe = isFolder ? Path.Combine(full, FfmpegFileName) : full;
        if (!_isExecutableFile(exe))
        {
            return isFolder ? $"There is no {FfmpegFileName} in {full}." : $"{exe} is not an executable file.";
        }

        var probe = FfmpegLocator.ProbePathFor(exe);
        if (!_isExecutableFile(probe))
        {
            return $"ffprobe was not found next to {exe}; Cutlass needs both.";
        }

        ffmpegPath = exe;
        return null;
    }

    // A picked ffmpeg that lost its execute bit (a static build unpacked from a zip) gets it back here, before the
    // check, so it is not rejected for something validation would have fixed (TECH_BRIEF 5.2). Files in system
    // folders are never changed.
    private static bool IsExecutableOnThisPlatform(string path) => FfmpegLocator.EnsureExecutable(path);
}
