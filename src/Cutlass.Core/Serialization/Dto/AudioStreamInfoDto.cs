namespace Cutlass.Core.Serialization.Dto;

/// <summary>Audio stream probe results.</summary>
internal sealed class AudioStreamInfoDto
{
    public int Index { get; set; }

    public string Codec { get; set; } = string.Empty;

    public int SampleRate { get; set; }

    public int Channels { get; set; }
}
