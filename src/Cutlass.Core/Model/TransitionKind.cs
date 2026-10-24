namespace Cutlass.Core.Model;

/// <summary>
/// The effect of a <see cref="Transition"/>. All but <see cref="AudioCrossfade"/> are video transitions, each backed
/// by one ffmpeg 6.0 <c>xfade</c> transition (the Compiler maps the names). Members are persisted by name, so they are
/// only ever appended, never renamed; <see cref="AudioCrossfade"/> stays last so enum order lists the video kinds first.
/// </summary>
public enum TransitionKind
{
    /// <summary>Video cross dissolve.</summary>
    Crossfade,

    /// <summary>Fade through black.</summary>
    DipToBlack,

    /// <summary>Fade through white.</summary>
    DipToWhite,

    /// <summary>Wipe towards the left.</summary>
    WipeLeft,

    /// <summary>Wipe towards the right.</summary>
    WipeRight,

    /// <summary>Slide towards the left.</summary>
    SlideLeft,

    /// <summary>Slide towards the right.</summary>
    SlideRight,

    /// <summary>Circle opening from the centre.</summary>
    CircleOpen,

    /// <summary>Wipe upwards.</summary>
    WipeUp,

    /// <summary>Wipe downwards.</summary>
    WipeDown,

    /// <summary>Wipe from the bottom right corner towards the top left.</summary>
    WipeTopLeft,

    /// <summary>Wipe from the bottom left corner towards the top right.</summary>
    WipeTopRight,

    /// <summary>Wipe from the top right corner towards the bottom left.</summary>
    WipeBottomLeft,

    /// <summary>Wipe from the top left corner towards the bottom right.</summary>
    WipeBottomRight,

    /// <summary>Slide upwards.</summary>
    SlideUp,

    /// <summary>Slide downwards.</summary>
    SlideDown,

    /// <summary>Soft-edged wipe towards the left.</summary>
    SmoothLeft,

    /// <summary>Soft-edged wipe towards the right.</summary>
    SmoothRight,

    /// <summary>Soft-edged wipe upwards.</summary>
    SmoothUp,

    /// <summary>Soft-edged wipe downwards.</summary>
    SmoothDown,

    /// <summary>Circle closing towards the centre.</summary>
    CircleClose,

    /// <summary>Circle that crops the first clip away and then grows the second one in.</summary>
    CircleCrop,

    /// <summary>Rectangle that crops the first clip away and then grows the second one in.</summary>
    RectCrop,

    /// <summary>Radial sweep around the centre, like a clock hand.</summary>
    Radial,

    /// <summary>Dissolve weighted by the colour distance between the two pictures.</summary>
    Distance,

    /// <summary>Horizontal bars opening from the centre line.</summary>
    HorizontalOpen,

    /// <summary>Horizontal bars closing towards the centre line.</summary>
    HorizontalClose,

    /// <summary>Vertical bars opening from the centre line.</summary>
    VerticalOpen,

    /// <summary>Vertical bars closing towards the centre line.</summary>
    VerticalClose,

    /// <summary>Diagonal wipe from the bottom right corner towards the top left.</summary>
    DiagonalTopLeft,

    /// <summary>Diagonal wipe from the bottom left corner towards the top right.</summary>
    DiagonalTopRight,

    /// <summary>Diagonal wipe from the top right corner towards the bottom left.</summary>
    DiagonalBottomLeft,

    /// <summary>Diagonal wipe from the top left corner towards the bottom right.</summary>
    DiagonalBottomRight,

    /// <summary>Noisy dissolve (random pixels switch over).</summary>
    Dissolve,

    /// <summary>Pixelate out and back in.</summary>
    Pixelize,

    /// <summary>Dissolve through greyscale.</summary>
    FadeGrays,

    /// <summary>Cross dissolve that reaches the second clip early.</summary>
    FadeFast,

    /// <summary>Cross dissolve that reaches the second clip late.</summary>
    FadeSlow,

    /// <summary>Squeeze the first clip horizontally while the second one expands.</summary>
    SqueezeHorizontal,

    /// <summary>Squeeze the first clip vertically while the second one expands.</summary>
    SqueezeVertical,

    /// <summary>Zoom into the first clip and dissolve to the second.</summary>
    ZoomIn,

    /// <summary>Audio crossfade (audio tracks only).</summary>
    AudioCrossfade,
}
