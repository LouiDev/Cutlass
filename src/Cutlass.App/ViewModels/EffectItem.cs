using CommunityToolkit.Mvvm.ComponentModel;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels;

/// <summary>
/// One entry of the Effects tab (DESIGN 4.2): a transition kind, a title template or a fade preset. Dragging an item
/// out publishes its <see cref="Id"/> under <see cref="DragFormat"/>, so the timeline can accept drops later.
/// </summary>
public sealed partial class EffectItem : ObservableObject
{
    /// <summary>The drag and drop data format of an effect id (a string such as <c>transition:Crossfade</c>).</summary>
    public const string DragFormat = "cutlass/effect-id";

    /// <summary>The id prefix of transitions; the rest is the <see cref="TransitionKind"/> name.</summary>
    public const string TransitionPrefix = "transition:";

    /// <summary>The id prefix of title templates; the rest is the template name (Centered, LowerThird, TopCaption).</summary>
    public const string TitlePrefix = "title:";

    /// <summary>The id prefix of adjustments; the rest is FadeIn, FadeOut, FadeInOut or RemoveFades.</summary>
    public const string AdjustmentPrefix = "adjust:";

    /// <summary>Creates the item.</summary>
    /// <param name="id">The effect id, for example <c>transition:DipToBlack</c>.</param>
    /// <param name="name">The display name.</param>
    /// <param name="category">The group.</param>
    /// <param name="badge">The short badge text drawn as the item's icon.</param>
    /// <param name="detail">The secondary text, for example "1 s".</param>
    /// <param name="description">The tooltip.</param>
    public EffectItem(string id, string name, EffectCategory category, string badge, string detail, string description)
    {
        Id = id;
        Name = name;
        Category = category;
        Badge = badge;
        Detail = detail;
        Description = description;
    }

    /// <summary>The effect id.</summary>
    public string Id { get; }

    /// <summary>The display name.</summary>
    public string Name { get; }

    /// <summary>The group.</summary>
    public EffectCategory Category { get; }

    /// <summary>The badge text.</summary>
    public string Badge { get; }

    /// <summary>The secondary text (default duration).</summary>
    public string Detail { get; }

    /// <summary>The tooltip.</summary>
    public string Description { get; }

    /// <summary>True for a transition (the badge is tinted like video clips).</summary>
    public bool IsTransition => Category == EffectCategory.Transition;

    /// <summary>True for a title template (the badge is tinted like title clips).</summary>
    public bool IsTitle => Category == EffectCategory.Title;

    /// <summary>The transition kind, or null for other effects.</summary>
    public TransitionKind? TransitionKind { get; init; }

    /// <summary>The title style, or null for other effects.</summary>
    public TitleSpec? Title { get; init; }

    /// <summary>True for the default video transition (Ctrl+D), drawn with a star.</summary>
    [ObservableProperty]
    public partial bool IsDefault { get; set; }

    /// <summary>True while the item is the Effects tab's selection.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The display name of a transition kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>For example "Dip to Black".</returns>
    public static string TransitionName(TransitionKind kind) => kind switch
    {
        Core.Model.TransitionKind.Crossfade => "Cross Dissolve",
        Core.Model.TransitionKind.DipToBlack => "Dip to Black",
        Core.Model.TransitionKind.DipToWhite => "Dip to White",
        Core.Model.TransitionKind.WipeLeft => "Wipe Left",
        Core.Model.TransitionKind.WipeRight => "Wipe Right",
        Core.Model.TransitionKind.SlideLeft => "Slide Left",
        Core.Model.TransitionKind.SlideRight => "Slide Right",
        Core.Model.TransitionKind.CircleOpen => "Circle Open",
        Core.Model.TransitionKind.WipeUp => "Wipe Up",
        Core.Model.TransitionKind.WipeDown => "Wipe Down",
        Core.Model.TransitionKind.WipeTopLeft => "Wipe Top Left",
        Core.Model.TransitionKind.WipeTopRight => "Wipe Top Right",
        Core.Model.TransitionKind.WipeBottomLeft => "Wipe Bottom Left",
        Core.Model.TransitionKind.WipeBottomRight => "Wipe Bottom Right",
        Core.Model.TransitionKind.SlideUp => "Slide Up",
        Core.Model.TransitionKind.SlideDown => "Slide Down",
        Core.Model.TransitionKind.SmoothLeft => "Smooth Left",
        Core.Model.TransitionKind.SmoothRight => "Smooth Right",
        Core.Model.TransitionKind.SmoothUp => "Smooth Up",
        Core.Model.TransitionKind.SmoothDown => "Smooth Down",
        Core.Model.TransitionKind.CircleClose => "Circle Close",
        Core.Model.TransitionKind.CircleCrop => "Circle Crop",
        Core.Model.TransitionKind.RectCrop => "Rect Crop",
        Core.Model.TransitionKind.Radial => "Radial Wipe",
        Core.Model.TransitionKind.Distance => "Distance Dissolve",
        Core.Model.TransitionKind.HorizontalOpen => "Horizontal Open",
        Core.Model.TransitionKind.HorizontalClose => "Horizontal Close",
        Core.Model.TransitionKind.VerticalOpen => "Vertical Open",
        Core.Model.TransitionKind.VerticalClose => "Vertical Close",
        Core.Model.TransitionKind.DiagonalTopLeft => "Diagonal Top Left",
        Core.Model.TransitionKind.DiagonalTopRight => "Diagonal Top Right",
        Core.Model.TransitionKind.DiagonalBottomLeft => "Diagonal Bottom Left",
        Core.Model.TransitionKind.DiagonalBottomRight => "Diagonal Bottom Right",
        Core.Model.TransitionKind.Dissolve => "Noise Dissolve",
        Core.Model.TransitionKind.Pixelize => "Pixelate",
        Core.Model.TransitionKind.FadeGrays => "Fade Grays",
        Core.Model.TransitionKind.FadeFast => "Fade Fast",
        Core.Model.TransitionKind.FadeSlow => "Fade Slow",
        Core.Model.TransitionKind.SqueezeHorizontal => "Squeeze Horizontal",
        Core.Model.TransitionKind.SqueezeVertical => "Squeeze Vertical",
        Core.Model.TransitionKind.ZoomIn => "Zoom In",
        Core.Model.TransitionKind.AudioCrossfade => "Audio Crossfade",
        _ => kind.ToString(),
    };

    /// <summary>The Effects tab sub-header a transition kind is listed under.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The group.</returns>
    public static TransitionGroup TransitionGroup(TransitionKind kind) => kind switch
    {
        Core.Model.TransitionKind.Crossfade or Core.Model.TransitionKind.DipToBlack or Core.Model.TransitionKind.DipToWhite
            or Core.Model.TransitionKind.FadeGrays or Core.Model.TransitionKind.FadeFast or Core.Model.TransitionKind.FadeSlow
            or Core.Model.TransitionKind.Dissolve or Core.Model.TransitionKind.Pixelize => ViewModels.TransitionGroup.Fades,
        Core.Model.TransitionKind.WipeLeft or Core.Model.TransitionKind.WipeRight or Core.Model.TransitionKind.WipeUp
            or Core.Model.TransitionKind.WipeDown or Core.Model.TransitionKind.WipeTopLeft or Core.Model.TransitionKind.WipeTopRight
            or Core.Model.TransitionKind.WipeBottomLeft or Core.Model.TransitionKind.WipeBottomRight
            or Core.Model.TransitionKind.SmoothLeft or Core.Model.TransitionKind.SmoothRight or Core.Model.TransitionKind.SmoothUp
            or Core.Model.TransitionKind.SmoothDown or Core.Model.TransitionKind.DiagonalTopLeft or Core.Model.TransitionKind.DiagonalTopRight
            or Core.Model.TransitionKind.DiagonalBottomLeft or Core.Model.TransitionKind.DiagonalBottomRight
            or Core.Model.TransitionKind.Radial or Core.Model.TransitionKind.Distance => ViewModels.TransitionGroup.Wipes,
        Core.Model.TransitionKind.SlideLeft or Core.Model.TransitionKind.SlideRight or Core.Model.TransitionKind.SlideUp
            or Core.Model.TransitionKind.SlideDown or Core.Model.TransitionKind.SqueezeHorizontal or Core.Model.TransitionKind.SqueezeVertical
            or Core.Model.TransitionKind.ZoomIn => ViewModels.TransitionGroup.Slides,
        Core.Model.TransitionKind.AudioCrossfade => ViewModels.TransitionGroup.Audio,

        // CircleOpen, CircleClose, CircleCrop, RectCrop and the horizontal and vertical bar openings.
        _ => ViewModels.TransitionGroup.Shapes,
    };

    /// <summary>The badge of a transition kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>A two or three letter badge.</returns>
    public static string TransitionBadge(TransitionKind kind) => kind switch
    {
        Core.Model.TransitionKind.Crossfade => "XD",
        Core.Model.TransitionKind.DipToBlack => "DB",
        Core.Model.TransitionKind.DipToWhite => "DW",
        Core.Model.TransitionKind.WipeLeft => "W←",
        Core.Model.TransitionKind.WipeRight => "W→",
        Core.Model.TransitionKind.SlideLeft => "S←",
        Core.Model.TransitionKind.SlideRight => "S→",
        Core.Model.TransitionKind.CircleOpen => "O",
        Core.Model.TransitionKind.WipeUp => "W↑",
        Core.Model.TransitionKind.WipeDown => "W↓",
        Core.Model.TransitionKind.WipeTopLeft => "W↖",
        Core.Model.TransitionKind.WipeTopRight => "W↗",
        Core.Model.TransitionKind.WipeBottomLeft => "W↙",
        Core.Model.TransitionKind.WipeBottomRight => "W↘",
        Core.Model.TransitionKind.SlideUp => "S↑",
        Core.Model.TransitionKind.SlideDown => "S↓",
        Core.Model.TransitionKind.SmoothLeft => "M←",
        Core.Model.TransitionKind.SmoothRight => "M→",
        Core.Model.TransitionKind.SmoothUp => "M↑",
        Core.Model.TransitionKind.SmoothDown => "M↓",
        Core.Model.TransitionKind.CircleClose => "●",
        Core.Model.TransitionKind.CircleCrop => "OC",
        Core.Model.TransitionKind.RectCrop => "RC",
        Core.Model.TransitionKind.Radial => "R°",
        Core.Model.TransitionKind.Distance => "DI",
        Core.Model.TransitionKind.HorizontalOpen => "H↕",
        Core.Model.TransitionKind.HorizontalClose => "H↓↑",
        Core.Model.TransitionKind.VerticalOpen => "V↔",
        Core.Model.TransitionKind.VerticalClose => "V→←",
        Core.Model.TransitionKind.DiagonalTopLeft => "D↖",
        Core.Model.TransitionKind.DiagonalTopRight => "D↗",
        Core.Model.TransitionKind.DiagonalBottomLeft => "D↙",
        Core.Model.TransitionKind.DiagonalBottomRight => "D↘",
        Core.Model.TransitionKind.Dissolve => "DS",
        Core.Model.TransitionKind.Pixelize => "PX",
        Core.Model.TransitionKind.FadeGrays => "FG",
        Core.Model.TransitionKind.FadeFast => "FF",
        Core.Model.TransitionKind.FadeSlow => "FS",
        Core.Model.TransitionKind.SqueezeHorizontal => "SQH",
        Core.Model.TransitionKind.SqueezeVertical => "SQV",
        Core.Model.TransitionKind.ZoomIn => "Z+",
        Core.Model.TransitionKind.AudioCrossfade => "AX",
        _ => "?",
    };
}
