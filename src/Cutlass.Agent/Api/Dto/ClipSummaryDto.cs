using System.Text.Json.Serialization;

namespace Cutlass.Agent.Api.Dto;

/// <summary>
/// One clip of the project summary. Media clips carry the media fields and <c>video</c> or <c>audio</c>; titles carry
/// <c>title</c>, <c>opacity</c> and their fades; generators carry <c>generator</c> and <c>video</c> (matte and bars)
/// or <c>audio</c> (tone).
/// </summary>
public sealed class ClipSummaryDto
{
    /// <summary>The id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><c>media</c>, <c>title</c> or <c>generator</c>.</summary>
    public string Type { get; set; } = "media";

    /// <summary>First frame.</summary>
    public long Start { get; set; }

    /// <summary>Length in frames.</summary>
    public long Duration { get; set; }

    /// <summary>Start plus duration.</summary>
    public long End { get; set; }

    /// <summary>Enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>The link group, or null.</summary>
    public string? LinkId { get; set; }

    /// <summary>Media clips: the media id.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaId { get; set; }

    /// <summary>Media clips: <c>video</c> or <c>audio</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stream { get; set; }

    /// <summary>The media file name, the title's first line or the generator name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Media clips: true when the file is missing.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Offline { get; set; }

    /// <summary>Media clips: the source position at the clip start, in flicks.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SourceInFlicks { get; set; }

    /// <summary>Media clips: the source position at the clip end, in flicks (derived, speed included; read-only).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? SourceOutFlicks { get; set; }

    /// <summary>Media clips: <see cref="SourceInFlicks"/> in seconds.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SourceInSeconds { get; set; }

    /// <summary>Media clips: <see cref="SourceOutFlicks"/> in seconds.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SourceOutSeconds { get; set; }

    /// <summary>Media clips: speed in percent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SpeedPercent { get; set; }

    /// <summary>Media clips: true for a freeze frame (the source in point held for the whole clip); written only when true.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Freeze { get; set; }

    /// <summary>Video media clips and video generators: the video properties.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClipVideoDto? Video { get; set; }

    /// <summary>Audio media clips and tones: the audio properties.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClipAudioDto? Audio { get; set; }

    /// <summary>Generators (effects batch 5): the kind, the matte colour and the tone frequency.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeneratorDto? Generator { get; set; }

    /// <summary>Titles: text and style.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TitleDto? Title { get; set; }

    /// <summary>Titles: opacity.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Opacity { get; set; }

    /// <summary>Titles: fade-in frames.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? FadeInFrames { get; set; }

    /// <summary>Titles: fade-out frames.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? FadeOutFrames { get; set; }
}
