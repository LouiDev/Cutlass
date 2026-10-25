namespace Cutlass.Core.Serialization.Dto;

/// <summary>Static audio effects in the project file. Every initializer is the neutral value, so a missing field reads as "no effect".</summary>
internal sealed class AudioEffectsDto
{
    public double HighPassHz { get; set; }

    public double LowPassHz { get; set; }

    public double BassDb { get; set; }

    public double TrebleDb { get; set; }

    public bool Compressor { get; set; }

    public double CompressorThresholdDb { get; set; } = -20;

    public double CompressorRatio { get; set; } = 4;

    public bool Gate { get; set; }

    public double GateThresholdDb { get; set; } = -40;

    public double Pan { get; set; }

    public bool Mono { get; set; }
}
