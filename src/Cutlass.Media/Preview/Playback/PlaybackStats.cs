namespace Cutlass.Media.Preview.Playback;

/// <summary>A snapshot of playback health (TECH_BRIEF 8.2), for the viewer's dropped-frame readout and badge.</summary>
/// <param name="FramesPresented">Frames handed to the presenter.</param>
/// <param name="FramesDropped">Frames skipped because a newer one was already due.</param>
/// <param name="Underruns">Times the audio output ran dry and restarted.</param>
/// <param name="WindowsStarted">Render windows whose processes were started.</param>
/// <param name="ContinuousLag">How long the presenter has been behind the clock without a break; the viewer shows the
/// "Dropped frames" badge from 2 s.</param>
/// <param name="MixedAudioFallback">True when PerTrack audio failed and playback fell back to a Mixed graph, where
/// mixer changes restart playback (TECH_BRIEF 7.6).</param>
public sealed record PlaybackStats(
    long FramesPresented,
    long FramesDropped,
    int Underruns,
    int WindowsStarted,
    TimeSpan ContinuousLag,
    bool MixedAudioFallback)
{
    /// <summary>How long the lag must last before the viewer shows the badge.</summary>
    public static readonly TimeSpan BadgeDelay = TimeSpan.FromSeconds(2);

    /// <summary>Nothing played yet.</summary>
    public static PlaybackStats Empty { get; } = new(0, 0, 0, 0, TimeSpan.Zero, false);

    /// <summary>True when the "Dropped frames" badge should show.</summary>
    public bool ShowDroppedBadge => ContinuousLag >= BadgeDelay;
}
