using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Timeline;

/// <summary>One track row of the timeline layout, in content coordinates (before vertical scrolling).</summary>
/// <param name="TrackId">The track id.</param>
/// <param name="Kind">Video or audio.</param>
/// <param name="KindIndex">The index among the tracks of the same kind (V1 and A1 are 0).</param>
/// <param name="Top">The top of the row in content pixels.</param>
/// <param name="Height">The row height in pixels.</param>
public sealed record TrackRow(string TrackId, TrackKind Kind, int KindIndex, double Top, double Height)
{
    /// <summary>The bottom of the row in content pixels.</summary>
    public double Bottom => Top + Height;

    /// <summary>True when <paramref name="contentY"/> lies inside the row.</summary>
    /// <param name="contentY">A content y coordinate.</param>
    /// <returns>True when inside.</returns>
    public bool Contains(double contentY) => contentY >= Top && contentY < Bottom;
}
