using Cutlass.App.Controls.Timeline;

namespace Cutlass.App.Controls;

/// <summary>A button drawn in a track header, in header-local coordinates relative to the row top.</summary>
/// <param name="Zone">The hit zone it maps to.</param>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top, relative to the row top.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public sealed record HeaderButton(HitZone Zone, double X, double Y, double Width, double Height)
{
    /// <summary>True when a header-local point lies on the button.</summary>
    /// <param name="x">The x relative to the header's left edge.</param>
    /// <param name="yInRow">The y relative to the row top.</param>
    /// <returns>True when inside.</returns>
    public bool Contains(double x, double yInRow) => x >= X && x < X + Width && yInRow >= Y && yInRow < Y + Height;
}
