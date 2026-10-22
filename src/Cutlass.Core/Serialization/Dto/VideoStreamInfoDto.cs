namespace Cutlass.Core.Serialization.Dto;

/// <summary>Video stream probe results; rationals are written as <c>"num/den"</c>.</summary>
internal sealed class VideoStreamInfoDto
{
    public int Index { get; set; }

    public string Codec { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public int DisplayWidth { get; set; }

    public int DisplayHeight { get; set; }

    public int Rotation { get; set; }

    public string Sar { get; set; } = "1/1";

    public string RFrameRate { get; set; } = "0/1";

    public string AvgFrameRate { get; set; } = "0/1";

    public string TimeBase { get; set; } = "1/1";

    public long StartPts { get; set; }

    public string PixFmt { get; set; } = string.Empty;

    public bool Vfr { get; set; }

    public bool Hdr { get; set; }

    public long BitRate { get; set; }
}
