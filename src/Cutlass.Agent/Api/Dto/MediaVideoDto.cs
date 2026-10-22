namespace Cutlass.Agent.Api.Dto;

/// <summary>The video stream of a media asset.</summary>
public sealed class MediaVideoDto
{
    /// <summary>The codec.</summary>
    public string Codec { get; set; } = string.Empty;

    /// <summary>Coded width.</summary>
    public int Width { get; set; }

    /// <summary>Coded height.</summary>
    public int Height { get; set; }

    /// <summary>Displayed width.</summary>
    public int DisplayWidth { get; set; }

    /// <summary>Displayed height.</summary>
    public int DisplayHeight { get; set; }

    /// <summary>Rotation in degrees.</summary>
    public int Rotation { get; set; }

    /// <summary>The frame rate as a ratio.</summary>
    public string FrameRate { get; set; } = string.Empty;

    /// <summary>Variable frame rate.</summary>
    public bool Vfr { get; set; }

    /// <summary>HDR transfer.</summary>
    public bool Hdr { get; set; }
}
