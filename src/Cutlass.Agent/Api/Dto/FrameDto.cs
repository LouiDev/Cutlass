using System.Text.Json.Serialization;

namespace Cutlass.Agent.Api.Dto;

/// <summary>The metadata of a rendered frame; with <c>format=base64</c> also the PNG.</summary>
public sealed class FrameDto
{
    /// <summary>Image width.</summary>
    public int Width { get; set; }

    /// <summary>Image height.</summary>
    public int Height { get; set; }

    /// <summary>The timeline frame.</summary>
    public long Frame { get; set; }

    /// <summary>The frame's timecode.</summary>
    public string Timecode { get; set; } = string.Empty;

    /// <summary>The PNG in base64, only with <c>format=base64</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PngBase64 { get; set; }
}
