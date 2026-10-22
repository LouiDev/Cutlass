namespace Cutlass.Core.Model;

/// <summary>Placement of a video clip on the canvas.</summary>
/// <param name="X">Horizontal offset in pixels from the canvas centre.</param>
/// <param name="Y">Vertical offset in pixels from the canvas centre.</param>
/// <param name="Scale">Scale relative to fit (1 fits the canvas).</param>
/// <param name="CropLeft">Left crop as a fraction of the source width, 0 to 0.9.</param>
/// <param name="CropTop">Top crop as a fraction of the source height, 0 to 0.9.</param>
/// <param name="CropRight">Right crop as a fraction of the source width, 0 to 0.9.</param>
/// <param name="CropBottom">Bottom crop as a fraction of the source height, 0 to 0.9.</param>
public sealed record Transform(
    double X,
    double Y,
    double Scale,
    double CropLeft,
    double CropTop,
    double CropRight,
    double CropBottom)
{
    /// <summary>Centred, fit to canvas, uncropped.</summary>
    public static Transform Identity { get; } = new(0, 0, 1, 0, 0, 0, 0);
}
