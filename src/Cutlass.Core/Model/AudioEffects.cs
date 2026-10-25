namespace Cutlass.Core.Model;

/// <summary>Static per-clip audio effects (effects batch 3). Neutral is <see cref="None"/>; the compiler emits nothing for a neutral value.</summary>
/// <param name="HighPassHz">High-pass cut-off in Hz, 0 or 20 to 2000 (neutral 0 = off).</param>
/// <param name="LowPassHz">Low-pass cut-off in Hz, 0 or 200 to 20000 (neutral 0 = off).</param>
/// <param name="BassDb">Bass shelf gain in dB at 100 Hz, -20 to 20 (neutral 0).</param>
/// <param name="TrebleDb">Treble shelf gain in dB at 3 kHz, -20 to 20 (neutral 0).</param>
/// <param name="Compressor">Compressor on (neutral false).</param>
/// <param name="CompressorThresholdDb">Compressor threshold in dBFS, -60 to 0 (neutral -20).</param>
/// <param name="CompressorRatio">Compressor ratio, 1 to 20 (neutral 4).</param>
/// <param name="Gate">Noise gate on (neutral false).</param>
/// <param name="GateThresholdDb">Gate threshold in dBFS, -80 to 0 (neutral -40).</param>
/// <param name="Pan">Constant-power pan, -1 (left) to 1 (right) (neutral 0 = centre).</param>
/// <param name="Mono">Sum both channels to mono; the stream stays stereo (neutral false).</param>
public sealed record AudioEffects(
    double HighPassHz,
    double LowPassHz,
    double BassDb,
    double TrebleDb,
    bool Compressor,
    double CompressorThresholdDb,
    double CompressorRatio,
    bool Gate,
    double GateThresholdDb,
    double Pan,
    bool Mono)
{
    /// <summary>No effects (every field neutral).</summary>
    public static AudioEffects None { get; } = new(0, 0, 0, 0, false, -20, 4, false, -40, 0, false);
}
