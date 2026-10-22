using System.Globalization;

namespace Cutlass.App.Services;

/// <summary>
/// The payload of a drag out of the Source monitor (DESIGN 4.3), published under <see cref="DragFormats.SourceRange"/>:
/// the media, the Source In and Out in flicks and the streams to place. <see cref="Format"/> and
/// <see cref="TryParse"/> use invariant culture, so the text is the same on every machine.
/// </summary>
/// <param name="AssetId">The media id.</param>
/// <param name="InFlicks">The source In in flicks (0 without a Source In).</param>
/// <param name="OutFlicks">The source Out (exclusive) in flicks, or null for the rest of the media.</param>
/// <param name="Streams">Video and audio, video only or audio only.</param>
public sealed record SourceDrag(string AssetId, long InFlicks, long? OutFlicks, SourceStreams Streams)
{
    /// <summary>The drag text: id, In, Out (empty for none) and streams, separated by '\n'.</summary>
    /// <returns>The text.</returns>
    public string Format() => string.Join(
        '\n',
        AssetId,
        InFlicks.ToString(CultureInfo.InvariantCulture),
        OutFlicks?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        Streams.ToString());

    /// <summary>Parses the text of <see cref="Format"/>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="drag">The payload, or null.</param>
    /// <returns>True when the text was valid.</returns>
    public static bool TryParse(string? text, out SourceDrag? drag)
    {
        drag = null;
        var parts = text?.Split('\n');
        if (parts is not { Length: 4 } || parts[0].Length == 0
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var inFlicks)
            || !Enum.TryParse<SourceStreams>(parts[3], ignoreCase: false, out var streams) || !Enum.IsDefined(streams))
        {
            return false;
        }

        long? outFlicks = null;
        if (parts[2].Length > 0)
        {
            if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var o))
            {
                return false;
            }

            outFlicks = o;
        }

        drag = new SourceDrag(parts[0], inFlicks, outFlicks, streams);
        return true;
    }
}
