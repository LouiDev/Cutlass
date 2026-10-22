using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace Cutlass.App.Controls;

/// <summary>
/// The DESIGN 8 colours the timeline draws with, read from <c>Themes/CutlassTheme.axaml</c> (the <c>&lt;Name&gt;Color</c>
/// resources) with the documented values as fallbacks.
/// </summary>
public sealed class TimelinePalette
{
    private TimelinePalette(Func<string, string, Color> color)
    {
        Bg0 = color("Bg0Color", "#141518");
        Bg1 = color("Bg1Color", "#1C1E22");
        Bg2 = color("Bg2Color", "#25282D");
        Bg3 = color("Bg3Color", "#30343A");
        Border = color("BorderColor", "#2F3238");
        TextPrimary = color("TextPrimaryColor", "#E6E8EB");
        TextSecondary = color("TextSecondaryColor", "#9BA1A9");
        TextDisabled = color("TextDisabledColor", "#5C626B");
        Accent = color("AccentColor", "#4CC38A");
        OnAccent = color("OnAccentColor", "#141518");
        Playhead = color("PlayheadColor", "#FF4D4D");
        Selection = color("SelectionColor", "#FFFFFF");
        VideoClip = color("VideoClipColor", "#3A6EA5");
        AudioClip = color("AudioClipColor", "#2E8B6E");
        TitleClip = color("TitleClipColor", "#8E5BD6");
        StillClip = color("StillClipColor", "#4F7C9A");
        Snap = color("SnapColor", "#F5D547");
        Marker = color("MarkerColor", "#F5D547");
        InOutRange = color("InOutRangeColor", "#14FFFFFF");
        Danger = color("DangerColor", "#E5484D");
    }

    /// <summary>The DESIGN 8 defaults, used before the control is in a tree.</summary>
    public static TimelinePalette Default { get; } = new((_, fallback) => Color.Parse(fallback));

    /// <summary>The Bg0 token.</summary>
    public Color Bg0 { get; }

    /// <summary>The Bg1 token.</summary>
    public Color Bg1 { get; }

    /// <summary>The Bg2 token.</summary>
    public Color Bg2 { get; }

    /// <summary>The Bg3 token.</summary>
    public Color Bg3 { get; }

    /// <summary>The Border token.</summary>
    public Color Border { get; }

    /// <summary>The TextPrimary token.</summary>
    public Color TextPrimary { get; }

    /// <summary>The TextSecondary token.</summary>
    public Color TextSecondary { get; }

    /// <summary>The TextDisabled token.</summary>
    public Color TextDisabled { get; }

    /// <summary>The Accent token.</summary>
    public Color Accent { get; }

    /// <summary>The OnAccent token.</summary>
    public Color OnAccent { get; }

    /// <summary>The Playhead token.</summary>
    public Color Playhead { get; }

    /// <summary>The Selection token.</summary>
    public Color Selection { get; }

    /// <summary>The VideoClip token.</summary>
    public Color VideoClip { get; }

    /// <summary>The AudioClip token.</summary>
    public Color AudioClip { get; }

    /// <summary>The TitleClip token.</summary>
    public Color TitleClip { get; }

    /// <summary>The StillClip token.</summary>
    public Color StillClip { get; }

    /// <summary>The Snap token.</summary>
    public Color Snap { get; }

    /// <summary>The Marker token.</summary>
    public Color Marker { get; }

    /// <summary>The InOutRange token.</summary>
    public Color InOutRange { get; }

    /// <summary>The Danger token.</summary>
    public Color Danger { get; }

    /// <summary>Reads the palette from the resources visible to a control.</summary>
    /// <param name="control">The control.</param>
    /// <returns>The palette.</returns>
    public static TimelinePalette From(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var theme = control.ActualThemeVariant ?? ThemeVariant.Dark;
        return new TimelinePalette((key, fallback) =>
            control.TryFindResource(key, theme, out var value) && value is Color c ? c : Color.Parse(fallback));
    }

    /// <summary>An immutable brush of a colour, optionally with an extra opacity.</summary>
    /// <param name="color">The colour.</param>
    /// <param name="opacity">The opacity 0 to 1.</param>
    /// <returns>The brush.</returns>
    public static IImmutableSolidColorBrush Brush(Color color, double opacity = 1) => new ImmutableSolidColorBrush(color, opacity);

    /// <summary>A colour moved toward white (selected clips are 12% lighter).</summary>
    /// <param name="color">The colour.</param>
    /// <param name="amount">0 to 1.</param>
    /// <returns>The lighter colour.</returns>
    public static Color Lighten(Color color, double amount) => Color.FromArgb(
        color.A,
        (byte)Math.Round(color.R + ((255 - color.R) * amount)),
        (byte)Math.Round(color.G + ((255 - color.G) * amount)),
        (byte)Math.Round(color.B + ((255 - color.B) * amount)));
}
