namespace Cutlass.App.Controls;

/// <summary>What a point of the timeline control hits (DESIGN 5.4). Lane zones are listed by precedence.</summary>
public enum HitZone
{
    /// <summary>Nothing (the corner above the headers, or outside the control).</summary>
    None,

    /// <summary>The ruler: click or drag scrubs.</summary>
    Ruler,

    /// <summary>A track header outside its buttons.</summary>
    Header,

    /// <summary>The target chip of a track header.</summary>
    HeaderTarget,

    /// <summary>The eye button of a video track header.</summary>
    HeaderVisibility,

    /// <summary>The lock button of a track header.</summary>
    HeaderLock,

    /// <summary>The M button of an audio track header.</summary>
    HeaderMute,

    /// <summary>The S button of an audio track header.</summary>
    HeaderSolo,

    /// <summary>The dB gain field of an audio track header (drag to change, right-click to reset).</summary>
    HeaderGain,

    /// <summary>The bottom edge of a track header: drag to resize the track.</summary>
    HeaderResize,

    /// <summary>A marker head in the ruler: drag moves it, double-click edits it.</summary>
    Marker,

    /// <summary>A volume keyframe dot on a selected audio clip (highest lane precedence).</summary>
    Keyframe,

    /// <summary>The fade-in corner handle (8x8 px, top left of a clip).</summary>
    FadeInHandle,

    /// <summary>The fade-out corner handle (8x8 px, top right of a clip).</summary>
    FadeOutHandle,

    /// <summary>A transition band: 40% of the track height, centered on a cut.</summary>
    Transition,

    /// <summary>The left end of a transition band: dragging it changes the duration around the cut.</summary>
    TransitionStart,

    /// <summary>The right end of a transition band.</summary>
    TransitionEnd,

    /// <summary>The start edge of a clip (6 px inside, or the first third of a narrow clip).</summary>
    ClipStart,

    /// <summary>The end edge of a clip (6 px inside, or the last third of a narrow clip).</summary>
    ClipEnd,

    /// <summary>The volume line of a selected audio clip (within 4 px of it).</summary>
    VolumeLine,

    /// <summary>The body of a clip.</summary>
    ClipBody,

    /// <summary>The playhead line over empty lane space.</summary>
    Playhead,

    /// <summary>Empty space between two clips of a track.</summary>
    Gap,

    /// <summary>Empty lane space after the last clip, over the divider, or below the tracks.</summary>
    Lane,
}
