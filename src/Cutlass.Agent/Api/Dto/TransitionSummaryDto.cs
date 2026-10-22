namespace Cutlass.Agent.Api.Dto;

/// <summary>One transition of the project summary.</summary>
public sealed class TransitionSummaryDto
{
    /// <summary>The id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The kind, for example <c>crossfade</c>.</summary>
    public string Kind { get; set; } = "crossfade";

    /// <summary>The outgoing clip.</summary>
    public string LeftClipId { get; set; } = string.Empty;

    /// <summary>The incoming clip.</summary>
    public string RightClipId { get; set; } = string.Empty;

    /// <summary>Length in frames.</summary>
    public long DurationFrames { get; set; }

    /// <summary>The cut frame.</summary>
    public long? CutFrame { get; set; }
}
