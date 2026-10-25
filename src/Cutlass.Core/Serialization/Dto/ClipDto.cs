using System.Text.Json.Serialization;

namespace Cutlass.Core.Serialization.Dto;

/// <summary>
/// A clip in the project file. <c>type</c> is <c>media</c>, <c>title</c> or <c>generator</c>; fields that do not apply
/// to the type are left out. A media clip writes <c>video</c> or <c>audio</c> to match its stream, a generator to match
/// its kind (<c>video</c> for the matte and the bars, <c>audio</c> for the tone).
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

    /// <summary>Written only when true (a freeze frame, effects batch 4); missing means false.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Freeze { get; set; }

    /// <summary>The generator kind in camelCase (effects batch 5); written only for generator clips.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Generator { get; set; }

    /// <summary>The matte colour <c>#RRGGBB</c>; written only for a colour matte.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Color { get; set; }

    /// <summary>The tone frequency in Hz; written only for a tone.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ToneHz { get; set; }

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
