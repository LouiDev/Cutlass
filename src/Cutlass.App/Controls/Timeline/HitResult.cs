namespace Cutlass.App.Controls.Timeline;

/// <summary>The result of <see cref="HitTester.Hit"/>.</summary>
/// <param name="Zone">The zone.</param>
/// <param name="TrackId">The track under the point, or null.</param>
/// <param name="ClipId">The clip of a clip zone, or null.</param>
/// <param name="TransitionId">The transition of a transition zone, or null.</param>
/// <param name="Frame">The exact (fractional) frame under the point.</param>
/// <param name="GapStart">The first frame of a gap zone.</param>
/// <param name="GapEnd">The end (exclusive) of a gap zone.</param>
/// <param name="MarkerId">The marker of a marker zone, or null.</param>
/// <param name="KeyIndex">The keyframe index of a keyframe zone, or -1.</param>
public sealed record HitResult(
    HitZone Zone,
    string? TrackId,
    string? ClipId,
    string? TransitionId,
    double Frame,
    long GapStart = 0,
    long GapEnd = 0,
    string? MarkerId = null,
    int KeyIndex = -1)
{
    /// <summary>A miss.</summary>
    public static HitResult Nothing { get; } = new(HitZone.None, null, null, null, 0);

    /// <summary>True for the clip zones (keyframes, fade handles, edges, volume line, body).</summary>
    public bool IsClip => Zone is HitZone.Keyframe or HitZone.FadeInHandle or HitZone.FadeOutHandle or HitZone.ClipStart
        or HitZone.ClipEnd or HitZone.VolumeLine or HitZone.ClipBody;

    /// <summary>True for a transition band, including its ends.</summary>
    public bool IsTransition => Zone is HitZone.Transition or HitZone.TransitionStart or HitZone.TransitionEnd;

    /// <summary>True for the header zones.</summary>
    public bool IsHeader => Zone is HitZone.Header or HitZone.HeaderTarget or HitZone.HeaderVisibility or HitZone.HeaderLock
        or HitZone.HeaderMute or HitZone.HeaderSolo or HitZone.HeaderGain or HitZone.HeaderResize;
}
