namespace Cutlass.Core.Model;

/// <summary>Per-clip video properties.</summary>
/// <param name="Transform">Position, scale and crop.</param>
/// <param name="Opacity">Opacity 0 to 1.</param>
/// <param name="Color">Colour adjustments.</param>
/// <param name="FadeInFrames">Fade-in length in frames.</param>
/// <param name="FadeOutFrames">Fade-out length in frames.</param>
public sealed record VideoProps(Transform Transform, double Opacity, ColorAdjust Color, long FadeInFrames, long FadeOutFrames)
{
    /// <summary>Identity transform, full opacity, neutral colour and no fades.</summary>
    public static VideoProps Default { get; } = new(Transform.Identity, 1.0, ColorAdjust.Neutral, 0, 0);
}
