namespace Cutlass.Agent.Api.Dto.Status;

/// <summary>One export preset in the capabilities.</summary>
public sealed class PresetDto
{
    /// <summary>The id, for example <c>h264</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>video</c>, <c>audio</c> or <c>gif</c>.</summary>
    public string Kind { get; set; } = "video";

    /// <summary>True when this ffmpeg can encode it.</summary>
    public bool Available { get; set; }

    /// <summary>Why it is unavailable, or null.</summary>
    public string? Reason { get; set; }
}
