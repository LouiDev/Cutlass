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
        Core.Model.TransitionKind.AudioCrossfade => "Audio Crossfade",
        _ => kind.ToString(),
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
        Core.Model.TransitionKind.AudioCrossfade => "AX",
        _ => "?",
    };
}
