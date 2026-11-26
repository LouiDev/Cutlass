using System.Text.Json.Serialization;

namespace Cutlass.Agent.Api.Dto.Project;

/// <summary>One track of the project summary. Video tracks carry <c>hidden</c>; audio tracks <c>muted</c>, <c>solo</c> and <c>gainDb</c>.</summary>
public sealed class TrackSummaryDto
{
    /// <summary>The id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><c>video</c> or <c>audio</c>.</summary>
    public string Kind { get; set; } = "video";

    /// <summary>The name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The kind index (1 is V1 or A1).</summary>
    public int Index { get; set; }

    /// <summary>Video: hidden.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Hidden { get; set; }

    /// <summary>Audio: muted.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Muted { get; set; }

    /// <summary>Audio: solo.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Solo { get; set; }

    /// <summary>Locked against edits.</summary>
    public bool Locked { get; set; }

    /// <summary>Targeted for placing.</summary>
    public bool Targeted { get; set; }

    /// <summary>Audio: track gain in dB.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? GainDb { get; set; }

    /// <summary>The clips by start.</summary>
    public List<ClipSummaryDto> Clips { get; set; } = [];

    /// <summary>The transitions.</summary>
    public List<TransitionSummaryDto> Transitions { get; set; } = [];
}
