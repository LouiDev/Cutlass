namespace Cutlass.Core.Model;

/// <summary>Text and styling of a <see cref="TitleClip"/>, rendered with ffmpeg's <c>drawtext</c>.</summary>
/// <param name="Text">The text, possibly multi-line.</param>
/// <param name="Font">Font reference: <c>builtin:Inter-Regular</c>, <c>builtin:Inter-SemiBold</c>, <c>builtin:Inter-Bold</c> or a font file path.</param>
/// <param name="Size">Font size in canvas pixels.</param>
/// <param name="Color">Text colour as <c>#RRGGBBAA</c>.</param>
/// <param name="OutlineWidth">Outline width in pixels (0 for none).</param>
/// <param name="OutlineColor">Outline colour as <c>#RRGGBBAA</c>.</param>
/// <param name="Box">True to draw a background box.</param>
/// <param name="BoxColor">Box colour as <c>#RRGGBBAA</c>.</param>
/// <param name="BoxPadding">Box padding in pixels.</param>
/// <param name="Anchor">Position preset.</param>
/// <param name="OffsetX">Horizontal offset from the anchor in pixels.</param>
/// <param name="OffsetY">Vertical offset from the anchor in pixels.</param>
public sealed record TitleSpec(
    string Text,
    string Font,
    double Size,
    string Color,
    double OutlineWidth,
    string OutlineColor,
    bool Box,
    string BoxColor,
    double BoxPadding,
    TitleAnchor Anchor,
    double OffsetX,
    double OffsetY)
{
    /// <summary>
    /// The style of a new title (Ctrl+T): Inter Bold 96 px white text with a 3 px translucent black outline, no box,
    /// in the lower third.
    /// </summary>
    public static TitleSpec Default { get; } = new(
        "Title", "builtin:Inter-Bold", 96, "#FFFFFFFF", 3, "#00000099", false, "#00000080", 16, TitleAnchor.LowerThird, 0, 0);
}
