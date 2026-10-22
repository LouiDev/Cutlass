namespace Cutlass.IntegrationTests.Infrastructure;

/// <summary>
/// Finds the ffmpeg and ffprobe the integration tests run against: <c>CUTLASS_FFMPEG</c> (path to the ffmpeg
/// executable) when set, else the first <c>PATH</c> entry holding both tools. Only file existence is checked here,
/// so <see cref="FfmpegFactAttribute"/> can decide quickly whether to skip.
/// </summary>
public static class TestFfmpeg
{
    private static readonly Lazy<(string Ffmpeg, string Ffprobe)?> Located = new(Locate);

    /// <summary>True when ffmpeg and ffprobe were found.</summary>
    public static bool IsAvailable => Located.Value is not null;

    /// <summary>Full path of ffmpeg.</summary>
    /// <exception cref="InvalidOperationException">When ffmpeg is not available.</exception>
    public static string FfmpegPath => Located.Value?.Ffmpeg
        ?? throw new InvalidOperationException("ffmpeg was not found. Set CUTLASS_FFMPEG or add it to PATH.");

    /// <summary>Full path of ffprobe (next to ffmpeg).</summary>
    /// <exception cref="InvalidOperationException">When ffprobe is not available.</exception>
    public static string FfprobePath => Located.Value?.Ffprobe
        ?? throw new InvalidOperationException("ffprobe was not found. Set CUTLASS_FFMPEG or add it to PATH.");

    private static (string Ffmpeg, string Ffprobe)? Locate()
    {
        var exe = OperatingSystem.IsWindows() ? ".exe" : string.Empty;

        var overridePath = Environment.GetEnvironmentVariable("CUTLASS_FFMPEG");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(overridePath));
            return dir is not null ? Pair(dir, exe) : null;
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string dir;
            try
            {
                dir = Path.GetFullPath(entry.Trim().Trim('"'));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            var pair = Pair(dir, exe);
            if (pair is not null)
            {
                return pair;
            }
        }

        return null;
    }

    private static (string Ffmpeg, string Ffprobe)? Pair(string dir, string exe)
    {
        var ffmpeg = Path.Combine(dir, "ffmpeg" + exe);
        var ffprobe = Path.Combine(dir, "ffprobe" + exe);
        return File.Exists(ffmpeg) && File.Exists(ffprobe) ? (ffmpeg, ffprobe) : null;
    }
}
