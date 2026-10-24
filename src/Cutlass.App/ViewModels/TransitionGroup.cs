namespace Cutlass.App.ViewModels;

/// <summary>
/// The sub-header a transition is listed under in the Effects tab (DESIGN 4.2), so the 42 kinds stay scannable.
/// <see cref="EffectItem.TransitionGroup(Core.Model.TransitionKind)"/> assigns one per kind; the order here is the
/// order of the headers.
/// </summary>
public enum TransitionGroup
{
    /// <summary>Dissolves and fades: Cross Dissolve, the dips, Fade Grays, Fade Fast, Fade Slow, Noise Dissolve, Pixelate.</summary>
    Fades,

    /// <summary>Hard and soft wipes, the diagonals, Radial and Distance.</summary>
    Wipes,

    /// <summary>Slides, squeezes and Zoom In.</summary>
    Slides,

    /// <summary>Circles, Rect Crop and the horizontal and vertical bar openings.</summary>
    Shapes,

    /// <summary>Audio Crossfade.</summary>
    Audio,
}
