namespace Cutlass.Agent.Api.Dto;

/// <summary>One media asset with probe info and online state.</summary>
public sealed class MediaDto
{
    /// <summary>The id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The file (forward slashes).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>False when the file is missing.</summary>
    public bool Online { get; set; } = true;

    /// <summary>The duration in seconds (0 for stills).</summary>
    public double DurationSeconds { get; set; }

    /// <summary>True for single images.</summary>
    public bool Still { get; set; }

    /// <summary>ffprobe's format name.</summary>
    public string FormatName { get; set; } = string.Empty;

    /// <summary>The video stream, or null.</summary>
    public MediaVideoDto? Video { get; set; }

    /// <summary>The audio stream, or null.</summary>
    public MediaAudioDto? Audio { get; set; }
}
