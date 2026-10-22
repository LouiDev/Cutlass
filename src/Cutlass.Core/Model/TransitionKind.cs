namespace Cutlass.Core.Model;

/// <summary>The effect of a <see cref="Transition"/>. All but <see cref="AudioCrossfade"/> are video transitions.</summary>
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

    /// <summary>Audio crossfade (audio tracks only).</summary>
    AudioCrossfade,
}
