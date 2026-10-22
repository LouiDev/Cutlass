namespace Cutlass.Core.Serialization.Dto;

/// <summary>Audio properties of a media clip in the project file.</summary>
internal sealed class AudioPropsDto
{
    public double GainDb { get; set; }

    public long FadeIn { get; set; }

    public long FadeOut { get; set; }

    public List<VolumeKeyDto> Keys { get; set; } = [];
}
