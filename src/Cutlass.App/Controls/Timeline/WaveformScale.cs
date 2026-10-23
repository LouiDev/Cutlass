namespace Cutlass.App.Controls.Timeline;

/// <summary>
/// The display curve of timeline waveforms (DESIGN 5.1): a square root of the normalised peak, so quiet material stays
/// readable (a -20 dBFS tone fills about a third of the half height instead of a tenth, -40 dBFS still a tenth)
/// while full scale still touches the edge. The curve is monotonic, keeps the sign and maps 0 to 0 and 1 to 1. Pure,
/// unit tested.
/// </summary>
public static class WaveformScale
{
    /// <summary>The full-scale value of a 16-bit peak.</summary>
    public const double SampleScale = 32768.0;

    /// <summary>The displayed fraction of the half height for a peak sample.</summary>
    /// <param name="sample">The 16-bit peak sample (negative for the minimum).</param>
    /// <returns>A value from -1 to 1 with the sign of <paramref name="sample"/>.</returns>
    public static double Display(double sample)
    {
        var normalized = Math.Clamp(sample / SampleScale, -1, 1);
        return Math.CopySign(Math.Sqrt(Math.Abs(normalized)), normalized);
    }
}
