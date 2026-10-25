using System.Collections.Immutable;

namespace Cutlass.Core.Model;

/// <summary>
/// The built-in title templates (DESIGN 4.2): named <see cref="TitleSpec"/> presets authored for a 1080-line canvas,
/// shared by the Effects tab and the agent API so both place identical titles. <see cref="Scale"/> adapts a template
/// to another sequence height.
/// </summary>
public static class TitleTemplates
{
    private static readonly ImmutableArray<(string Name, string DisplayName, string Description, TitleSpec Spec)> Items =
    [
        ("Centered", "Centered", "Large centred title.", TitleSpec.Default with { Text = "Title", Anchor = TitleAnchor.Center }),
        ("LowerThird", "Lower Third", "Name strap in the lower third.", TitleSpec.Default with
        {
            Text = "Name",
            Font = "builtin:Inter-SemiBold",
            Size = 64,
            Box = true,
            Anchor = TitleAnchor.LowerThird,
        }),
        ("TopCaption", "Top Caption", "Small caption near the top.", TitleSpec.Default with
        {
            Text = "Caption",
            Font = "builtin:Inter-Regular",
            Size = 48,
            OutlineWidth = 0,
            Box = true,
            Anchor = TitleAnchor.Top,
        }),
        ("Subtitle", "Subtitle", "Subtitle line on a translucent dark box.", TitleSpec.Default with
        {
            Text = "Subtitle",
            Font = "builtin:Inter-Regular",
            Size = 44,
            Box = true,
            BoxColor = "#000000A0",
            BoxPadding = 14,
            Anchor = TitleAnchor.LowerThird,
        }),
        ("Headline", "Headline", "Very large bold headline near the top.", TitleSpec.Default with
        {
            Text = "Headline",
            Font = "builtin:Inter-Bold",
            Size = 120,
            OutlineWidth = 4,
            Anchor = TitleAnchor.Top,
        }),
        ("Quote", "Quote", "Centred quote on a translucent box.", TitleSpec.Default with
        {
            Text = "Quote",
            Font = "builtin:Inter-SemiBold",
            Size = 72,
            Box = true,
            BoxColor = "#00000080",
            BoxPadding = 28,
            Anchor = TitleAnchor.Center,
        }),
    ];

    /// <summary>The template names in declaration order (the Effects tab order and the wire spelling).</summary>
    public static ImmutableArray<string> Names { get; } = [.. Items.Select(i => i.Name)];

    /// <summary>Looks a template up by name, ignoring case.</summary>
    /// <param name="name">The template name, for example <c>LowerThird</c>.</param>
    /// <param name="spec">The template's spec for a 1080-line canvas.</param>
    /// <returns>False for null or an unknown name.</returns>
    public static bool TryGet(string? name, out TitleSpec spec)
    {
        if (name is not null)
        {
            foreach (var item in Items)
            {
                if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    spec = item.Spec;
                    return true;
                }
            }
        }

        spec = TitleSpec.Default;
        return false;
    }

    /// <summary>The display name of a template ("Lower Third"), or the name itself when unknown.</summary>
    /// <param name="name">The template name.</param>
    /// <returns>The display name.</returns>
    public static string DisplayName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Find(name)?.DisplayName ?? name;
    }

    /// <summary>The one-line description of a template, or an empty string when unknown.</summary>
    /// <param name="name">The template name.</param>
    /// <returns>The description.</returns>
    public static string Description(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Find(name)?.Description ?? string.Empty;
    }

    /// <summary>
    /// Scales a template authored for a 1080-line canvas to a sequence height: the font size (at least 1), the outline
    /// width (one decimal) and the box padding scale by <c>height / 1080</c>, so a template looks the same at every size.
    /// </summary>
    /// <param name="spec">The spec at 1080 lines.</param>
    /// <param name="height">The sequence height in pixels.</param>
    /// <returns>The scaled spec (the same instance at 1080 lines).</returns>
    public static TitleSpec Scale(TitleSpec spec, int height)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (height == 1080 || height <= 0)
        {
            return spec;
        }

        var scale = height / 1080.0;
        return spec with
        {
            Size = Math.Max(1, Math.Round(spec.Size * scale)),
            OutlineWidth = Math.Round(spec.OutlineWidth * scale, 1),
            BoxPadding = Math.Round(spec.BoxPadding * scale),
        };
    }

    private static (string Name, string DisplayName, string Description, TitleSpec Spec)? Find(string name)
    {
        foreach (var item in Items)
        {
            if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }
}
