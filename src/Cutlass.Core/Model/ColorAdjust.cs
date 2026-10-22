namespace Cutlass.Core.Model;

/// <summary>Colour adjustments applied with ffmpeg's <c>eq</c> filter. Neutral is 0, 1, 1, 1.</summary>
/// <param name="Brightness">Brightness, -1 to 1 (neutral 0).</param>
/// <param name="Contrast">Contrast, 0 to 2 (neutral 1).</param>
/// <param name="Saturation">Saturation, 0 to 3 (neutral 1).</param>
/// <param name="Gamma">Gamma, 0.1 to 4 (neutral 1).</param>
public sealed record ColorAdjust(double Brightness, double Contrast, double Saturation, double Gamma)
{
    /// <summary>The neutral adjustment (no change).</summary>
    public static ColorAdjust Neutral { get; } = new(0, 1, 1, 1);
}
