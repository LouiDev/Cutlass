namespace Cutlass.Core.Serialization.Dto;

/// <summary>A transition in the project file.</summary>
internal sealed class TransitionDto
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = "crossfade";

    public string Left { get; set; } = string.Empty;

    public string Right { get; set; } = string.Empty;

    public long Duration { get; set; }
}
