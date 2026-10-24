namespace Cutlass.Core.Model;

/// <summary>Static per-clip video effects (effects batch 2). Neutral is <see cref="None"/>; the compiler emits nothing for a neutral value.</summary>
/// <param name="FlipH">Mirror horizontally (neutral false).</param>
/// <param name="FlipV">Mirror vertically (neutral false).</param>
/// <param name="RotationDegrees">User rotation in degrees, -180 to 180 (neutral 0, clockwise positive). The frame keeps its size and the corners clip.</param>
/// <param name="HueDegrees">Hue shift in degrees, -180 to 180 (neutral 0).</param>
/// <param name="TemperatureKelvin">Colour temperature in kelvin, 1000 to 40000 (neutral 6500).</param>
/// <param name="Tint">Green to magenta tint, -1 to 1 (neutral 0, positive towards magenta).</param>
/// <param name="Blur">Gaussian blur sigma in sequence pixels, 0 to 50 (neutral 0).</param>
/// <param name="Sharpen">Sharpen amount, 0 to 1 (neutral 0).</param>
/// <param name="Vignette">Vignette strength, 0 to 1 (neutral 0).</param>
/// <param name="Grayscale">Drop the colour (neutral false).</param>
/// <param name="KeyEnabled">Chroma key on (neutral false).</param>
/// <param name="KeyColor">Key colour as "#RRGGBB" (opaque only; neutral "#00FF00").</param>
/// <param name="KeySimilarity">Key similarity, 0.01 to 1 (neutral 0.1).</param>
/// <param name="KeyBlend">Key edge blend, 0 to 1 (neutral 0).</param>
public sealed record VideoEffects(
    bool FlipH,
    bool FlipV,
    double RotationDegrees,
    double HueDegrees,
    double TemperatureKelvin,
    double Tint,
    double Blur,
    double Sharpen,
    double Vignette,
    bool Grayscale,
    bool KeyEnabled,
    string KeyColor,
    double KeySimilarity,
    double KeyBlend)
{
    /// <summary>No effects (every field neutral).</summary>
    public static VideoEffects None { get; } = new(false, false, 0, 0, 6500, 0, 0, 0, 0, false, false, "#00FF00", 0.1, 0);
}
