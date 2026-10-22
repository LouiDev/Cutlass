using System.Globalization;
using System.Text.RegularExpressions;

namespace Cutlass.Media.Parsing;

/// <summary>
/// Parses <c>ffmpeg -hide_banner -nostdin -version</c> (TECH_BRIEF 5.2): the build id from <c>^ffmpeg version (\S+)</c> and the
/// libavfilter version from <c>^libavfilter\s+(\d+)\.\s*(\d+)</c>. Never parses semver, because git builds print
/// <c>N-126308</c>.
/// </summary>
public static partial class VersionParser
{
    /// <summary>Parses the version output.</summary>
    /// <param name="text">The stdout of <c>-version</c>.</param>
    /// <returns>The version, or null when the build id or the libavfilter line is missing.</returns>
    public static FfmpegVersion? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var build = BuildRegex().Match(text);
        var avfilter = AvfilterRegex().Match(text);
        if (!build.Success || !avfilter.Success)
        {
            return null;
        }

        if (!int.TryParse(avfilter.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(avfilter.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return null;
        }

        return new FfmpegVersion(build.Groups[1].Value, major, minor);
    }

    [GeneratedRegex(@"^ffmpeg version (\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BuildRegex();

    [GeneratedRegex(@"^libavfilter\s+(\d+)\.\s*(\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex AvfilterRegex();
}
