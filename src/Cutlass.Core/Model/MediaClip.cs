namespace Cutlass.Core.Model;

/// <summary>
/// A clip showing one stream of a media asset. Source time is in flicks from the media's <c>format.start_time</c>.
/// </summary>
/// <param name="Id">Clip id (prefix <c>c_</c>).</param>
/// <param name="Start">First timeline frame.</param>
/// <param name="Duration">Length in frames, at least 1.</param>
/// <param name="Enabled">Disabled clips are not rendered.</param>
/// <param name="LinkId">Link group id, or null.</param>
/// <param name="MediaId">The <see cref="MediaAsset.Id"/> this clip plays.</param>
/// <param name="Stream">Which stream of the media this clip uses.</param>
/// <param name="SourceInFlicks">Source position shown at <see cref="Clip.Start"/>, in flicks.</param>
/// <param name="SpeedPercent">Playback speed, 25 to 400 (100 is normal).</param>
/// <param name="Video">Video properties (ignored for audio clips).</param>
/// <param name="Audio">Audio properties (ignored for video clips).</param>
public sealed record MediaClip(
    string Id,
    long Start,
    long Duration,
    bool Enabled,
    string? LinkId,
    string MediaId,
    StreamKind Stream,
    long SourceInFlicks,
    int SpeedPercent,
    VideoProps Video,
    AudioProps Audio) : Clip(Id, Start, Duration, Enabled, LinkId);
