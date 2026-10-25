using System.Collections.Immutable;

namespace Cutlass.Core.Model;

/// <summary>Per-clip audio properties.</summary>
/// <param name="GainDb">Clip gain in dB.</param>
/// <param name="FadeInFrames">Fade-in length in frames.</param>
/// <param name="FadeOutFrames">Fade-out length in frames.</param>
/// <param name="Keys">Volume keyframes sorted by frame, each in <c>[0, Duration)</c>.</param>
/// <param name="Effects">Static audio effects (filters, shelves, compressor, gate, pan, mono).</param>
public sealed record AudioProps(double GainDb, long FadeInFrames, long FadeOutFrames, ImmutableArray<VolumeKey> Keys, AudioEffects Effects)
{
    /// <summary>0 dB, no fades, no keyframes and no effects.</summary>
    public static AudioProps Default { get; } = new(0, 0, 0, ImmutableArray<VolumeKey>.Empty, AudioEffects.None);
}
