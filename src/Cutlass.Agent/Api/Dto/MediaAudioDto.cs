namespace Cutlass.Agent.Api.Dto;

/// <summary>The audio stream of a media asset.</summary>
public sealed class MediaAudioDto
{
    /// <summary>The codec.</summary>
    public string Codec { get; set; } = string.Empty;

    /// <summary>The sample rate.</summary>
    public int SampleRate { get; set; }

    /// <summary>The channels.</summary>
    public int Channels { get; set; }
}
