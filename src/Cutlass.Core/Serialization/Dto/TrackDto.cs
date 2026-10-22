using System.Text.Json.Serialization;

namespace Cutlass.Core.Serialization.Dto;

/// <summary>
/// A track in the project file. Video tracks write <c>hidden</c>; audio tracks write <c>muted</c>, <c>solo</c> and
/// <c>gainDb</c> (the others are left out as null).
/// </summary>
internal sealed class TrackDto
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = "video";

    public string Name { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Hidden { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Muted { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Solo { get; set; }

    public bool Locked { get; set; }

    public bool Targeted { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? GainDb { get; set; }

    public List<ClipDto> Clips { get; set; } = [];

    public List<TransitionDto> Transitions { get; set; } = [];
}
