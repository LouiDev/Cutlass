using System.Text.Json.Serialization;

namespace Cutlass.Core.Serialization.Dto;

/// <summary>
/// A clip in the project file. <c>type</c> is <c>media</c> or <c>title</c>; fields that do not apply to the type are
/// left out. A media clip writes <c>video</c> or <c>audio</c> to match its stream.
/// </summary>
internal sealed class ClipDto
{
    public string Type { get; set; } = "media";

    public string Id { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stream { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaId { get; set; }

    public long Start { get; set; }

    public long Duration { get; set; }

    public bool Enabled { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LinkId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SourceInFlicks { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SpeedPercent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VideoPropsDto? Video { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AudioPropsDto? Audio { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TitleSpecDto? Title { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Opacity { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? FadeIn { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? FadeOut { get; set; }
}
