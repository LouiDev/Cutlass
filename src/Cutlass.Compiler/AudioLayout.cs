namespace Cutlass.Compiler;

/// <summary>How the audio tracks leave the graph (TECH_BRIEF 7.6).</summary>
public enum AudioLayout
{
    /// <summary>
    /// One stereo mix (exports): muted and un-soloed tracks are left out, track gain is applied, and the optional
    /// limiter runs on the sum.
    /// </summary>
    Mixed = 0,

    /// <summary>
    /// Every audio track as its own stereo pair, merged into 2N channels (playback). Mute, solo and track gain are
    /// applied live by the mixer, not in the graph.
    /// </summary>
    PerTrack = 1,
}
