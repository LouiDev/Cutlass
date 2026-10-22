namespace Cutlass.App.Services;

/// <summary>Which streams a Source monitor drag places (DESIGN 4.3).</summary>
public enum SourceStreams
{
    /// <summary>The canvas drag: video and its linked audio.</summary>
    Both,

    /// <summary>The "Video only" handle.</summary>
    Video,

    /// <summary>The "Audio only" handle.</summary>
    Audio,
}
