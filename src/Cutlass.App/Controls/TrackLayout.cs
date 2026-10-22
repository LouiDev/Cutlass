using Cutlass.Core.Model;

namespace Cutlass.App.Controls;

/// <summary>
/// The vertical layout of the track rows (DESIGN 3): video tracks stack upward from the divider with V1 nearest it,
/// audio tracks stack downward from it with A1 nearest it. Coordinates are content pixels below the ruler.
/// </summary>
public sealed class TrackLayout
{
    /// <summary>Creates a layout.</summary>
    /// <param name="rows">The rows in display order, top to bottom.</param>
    /// <param name="dividerTop">The top of the divider between video and audio tracks.</param>
    /// <param name="contentHeight">The total height including the centering offset.</param>
    public TrackLayout(IReadOnlyList<TrackRow> rows, double dividerTop, double contentHeight)
    {
        Rows = rows;
        DividerTop = dividerTop;
        ContentHeight = contentHeight;
    }

    /// <summary>The rows in display order, top to bottom.</summary>
    public IReadOnlyList<TrackRow> Rows { get; }

    /// <summary>The top of the video/audio divider in content pixels.</summary>
    public double DividerTop { get; }

    /// <summary>The total content height in pixels.</summary>
    public double ContentHeight { get; }

    /// <summary>The row of a track, or null.</summary>
    /// <param name="trackId">The track id.</param>
    /// <returns>The row, or null.</returns>
    public TrackRow? Row(string trackId)
    {
        foreach (var row in Rows)
        {
            if (row.TrackId == trackId)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>The row under a content y coordinate, or null.</summary>
    /// <param name="contentY">The content y.</param>
    /// <returns>The row, or null over the divider or outside the tracks.</returns>
    public TrackRow? RowAt(double contentY)
    {
        foreach (var row in Rows)
        {
            if (row.Contains(contentY))
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// The row of <paramref name="kind"/> nearest to a content y coordinate (vertical moves stay within one kind).
    /// </summary>
    /// <param name="contentY">The content y.</param>
    /// <param name="kind">The track kind.</param>
    /// <returns>The nearest row, or null when there is no track of that kind.</returns>
    public TrackRow? NearestRowOfKind(double contentY, TrackKind kind)
    {
        TrackRow? best = null;
        var bestDistance = double.MaxValue;
        foreach (var row in Rows)
        {
            if (row.Kind != kind)
            {
                continue;
            }

            var distance = row.Contains(contentY) ? 0 : Math.Min(Math.Abs(contentY - row.Top), Math.Abs(contentY - row.Bottom));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = row;
            }
        }

        return best;
    }

    /// <summary>The row of a kind at a kind index, or null.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="kindIndex">The kind index (V1 and A1 are 0).</param>
    /// <returns>The row, or null.</returns>
    public TrackRow? RowOfKind(TrackKind kind, int kindIndex)
    {
        foreach (var row in Rows)
        {
            if (row.Kind == kind && row.KindIndex == kindIndex)
            {
                return row;
            }
        }

        return null;
    }
}
