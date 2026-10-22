using System.Globalization;

namespace Cutlass.Media.Parsing;

/// <summary>
/// Parses the packet listing behind the frame index (TECH_BRIEF 6.4):
/// <c>ffprobe -v error -select_streams &lt;v&gt; -show_entries packet=pts,dts,flags -of csv=p=0 &lt;file&gt;</c>,
/// one <c>pts,dts,flags</c> line per packet.
/// </summary>
public static class PacketIndexParser
{
    /// <summary>More than this share of unusable lines means the file gets no index (CFR fallback).</summary>
    public const double MaxUnusableFraction = 0.05;

    /// <summary>
    /// More packets than this without a pts means the file gets no index (CFR fallback): their dts is not the decoded
    /// timestamp. In AVI with B-frames (mpeg4 <c>-bf 2</c>) a third of the packets lack a pts, and the dts-built index
    /// ran one frame ahead of the decoder's timestamps.
    /// </summary>
    public const int MaxDtsFallbackPackets = 4;

    /// <summary>Builds the index arguments for ffprobe.</summary>
    /// <param name="videoStreamIndex">The absolute video stream index.</param>
    /// <param name="file">The media file (absolute path).</param>
    /// <returns>The arguments, without the executable.</returns>
    public static IReadOnlyList<string> BuildArguments(int videoStreamIndex, string file) =>
    [
        "-v", "error", "-select_streams", videoStreamIndex.ToString(CultureInfo.InvariantCulture),
        "-show_entries", "packet=pts,dts,flags", "-of", "csv=p=0", file,
    ];

    /// <summary>
    /// Parses the listing: drops packets whose flags contain <c>D</c> (discard), uses <c>dts</c> when <c>pts</c> is
    /// <c>N/A</c> (at most <see cref="MaxDtsFallbackPackets"/> times), sorts ascending and removes duplicates.
    /// </summary>
    /// <remarks>
    /// "Unusable" lines are packet lines without any usable timestamp or that cannot be parsed. Discarded (<c>D</c>)
    /// packets are dropped on purpose and do not count against the 5% limit. Blank lines are ignored.
    /// </remarks>
    /// <param name="csv">The ffprobe stdout.</param>
    /// <returns>The ascending distinct pts values in stream time base units, or null when no packet was usable or
    /// more than <see cref="MaxUnusableFraction"/> of the lines were unusable, or more than
    /// <see cref="MaxDtsFallbackPackets"/> packets needed the dts fallback.</returns>
    public static long[]? Parse(string? csv)
    {
        if (string.IsNullOrEmpty(csv))
        {
            return null;
        }

        var values = new List<long>();
        var total = 0;
        var unusable = 0;
        var dtsFallbacks = 0;
        foreach (var raw in csv.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            total++;
            var fields = line.Split(',');
            if (fields.Length < 3)
            {
                unusable++;
                continue;
            }

            var flags = fields[2].Trim();
            if (flags.Contains('D', StringComparison.Ordinal))
            {
                continue;
            }

            if (TryParseTimestamp(fields[0], out var pts))
            {
                values.Add(pts);
            }
            else if (TryParseTimestamp(fields[1], out pts))
            {
                values.Add(pts);
                dtsFallbacks++;
            }
            else
            {
                unusable++;
            }
        }

        if (values.Count == 0 || unusable > total * MaxUnusableFraction || dtsFallbacks > MaxDtsFallbackPackets)
        {
            return null;
        }

        values.Sort();
        var distinct = new List<long>(values.Count);
        foreach (var value in values)
        {
            if (distinct.Count == 0 || distinct[^1] != value)
            {
                distinct.Add(value);
            }
        }

        return [.. distinct];
    }

    private static bool TryParseTimestamp(string text, out long value) =>
        long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
}
