using System.Collections.Immutable;

namespace Cutlass.Core.Model;

/// <summary>Per-clip audio properties.</summary>
/// <param name="GainDb">Clip gain in dB.</param>
/// <param name="FadeInFrames">Fade-in length in frames.</param>
/// <param name="FadeOutFrames">Fade-out length in frames.</param>
/// <param name="Keys">Volume keyframes sorted by frame, each in <c>[0, Duration)</c>.</param>
public sealed record AudioProps(double GainDb, long FadeInFrames, long FadeOutFrames, ImmutableArray<VolumeKey> Keys)
{
    /// <summary>0 dB, no fades and no keyframes.</summary>
    public static AudioProps Default { get; } = new(0, 0, 0, ImmutableArray<VolumeKey>.Empty);
}
