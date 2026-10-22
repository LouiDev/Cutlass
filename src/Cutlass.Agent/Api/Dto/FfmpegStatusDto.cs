namespace Cutlass.Agent.Api.Dto;

/// <summary>The <c>ffmpeg</c> part of the status.</summary>
public sealed class FfmpegStatusDto
{
    /// <summary>True when ffmpeg is ready.</summary>
    public bool Ready { get; set; }

    /// <summary>The ffmpeg version, or null.</summary>
    public string? Version { get; set; }

    /// <summary>Why ffmpeg is not ready, or null.</summary>
    public string? Reason { get; set; }
}
