namespace Cutlass.Agent.Api.Dto;

/// <summary>The audio properties of an audio media clip.</summary>
public sealed class ClipAudioDto
{
    /// <summary>Clip gain in dB.</summary>
    public double GainDb { get; set; }

    /// <summary>Fade-in frames.</summary>
    public long FadeInFrames { get; set; }

    /// <summary>Fade-out frames.</summary>
    public long FadeOutFrames { get; set; }

    /// <summary>Volume keys relative to the clip start.</summary>
    public List<VolumeKeyDto> Keys { get; set; } = [];
}
