using System.Collections.Immutable;

namespace Cutlass.Media.Preview;

/// <summary>
/// Peak levels since the last <see cref="AudioMixer.ReadAndResetPeaks"/> (TECH_BRIEF 8.2), linear in 0..1 of full
/// scale. Track peaks are measured after mute, solo and gain (what is heard), the master peaks on the clamped output.
/// </summary>
/// <param name="Tracks">The peak per track channel pair, in sequence order.</param>
/// <param name="MasterLeft">The left master peak.</param>
/// <param name="MasterRight">The right master peak.</param>
public sealed record MixerPeaks(ImmutableArray<float> Tracks, float MasterLeft, float MasterRight)
{
    /// <summary>The lowest level <see cref="ToDbfs"/> reports, for silence.</summary>
    public const double FloorDbfs = -96.0;

    /// <summary>Silence on every channel.</summary>
    public static MixerPeaks Silent { get; } = new([], 0f, 0f);

    /// <summary>Converts a linear peak to dBFS, floored at <see cref="FloorDbfs"/>.</summary>
    /// <param name="peak">The linear peak (1 is full scale).</param>
    /// <returns>The level in dBFS.</returns>
    public static double ToDbfs(float peak) =>
        peak <= 0f ? FloorDbfs : Math.Max(FloorDbfs, 20.0 * Math.Log10(peak));
}
