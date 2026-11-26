namespace Cutlass.Agent.Api.Dto.Project;

/// <summary>Static audio effects of an audio clip. In requests every field is optional (null keeps the value).</summary>
public sealed class AudioEffectsDto
{
    /// <summary>High-pass cut-off in Hz, 0 or 20 to 2000 (0 = off).</summary>
    public double? HighPassHz { get; set; }

    /// <summary>Low-pass cut-off in Hz, 0 or 200 to 20000 (0 = off).</summary>
    public double? LowPassHz { get; set; }

    /// <summary>Bass shelf gain in dB at 100 Hz, -20 to 20 (0).</summary>
    public double? BassDb { get; set; }

    /// <summary>Treble shelf gain in dB at 3 kHz, -20 to 20 (0).</summary>
    public double? TrebleDb { get; set; }

    /// <summary>Compressor on (false).</summary>
    public bool? Compressor { get; set; }

    /// <summary>Compressor threshold in dBFS, -60 to 0 (-20).</summary>
    public double? CompressorThresholdDb { get; set; }

    /// <summary>Compressor ratio, 1 to 20 (4).</summary>
    public double? CompressorRatio { get; set; }

    /// <summary>Noise gate on (false).</summary>
    public bool? Gate { get; set; }

    /// <summary>Gate threshold in dBFS, -80 to 0 (-40).</summary>
    public double? GateThresholdDb { get; set; }

    /// <summary>Constant-power pan, -1 left to 1 right (0).</summary>
    public double? Pan { get; set; }

    /// <summary>Sum both channels to mono (false).</summary>
    public bool? Mono { get; set; }
}
