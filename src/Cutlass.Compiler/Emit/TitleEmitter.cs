using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Cutlass.Compiler.Graph;
using Cutlass.Compiler.Planning;
using Cutlass.Core.Model;

namespace Cutlass.Compiler.Emit;

/// <summary>
/// Emits the source part of a title segment (TECH_BRIEF 7.3): a transparent canvas with <c>drawtext</c> reading the
/// text from <c>t_&lt;sha8&gt;.txt</c> with <c>expansion=none</c> and the font from a bare <c>fontfile</c>.
/// </summary>
internal static class TitleEmitter
{
    /// <summary>The bundled font used when a reference cannot be resolved.</summary>
    public const string FallbackFont = "Inter-Regular.ttf";

    private const string BuiltinPrefix = "builtin:";

    private static readonly string[] BundledFonts = ["Inter-Regular", "Inter-SemiBold", "Inter-Bold"];

    /// <summary>Starts the title chain; opacity and fades are added by the caller.</summary>
    /// <param name="ctx">The compile context.</param>
    /// <param name="clip">The title clip.</param>
    /// <param name="segment">The planned segment.</param>
    /// <returns>The open chain, canvas sized and <c>yuva420p</c>.</returns>
    public static GraphChain EmitSource(EmitContext ctx, TitleClip clip, PlannedSegment segment)
    {
        var spec = clip.Title;
        var text = FilterEscaper.NormalizeTitleText(spec.Text);
        var textFile = TextFileName(text);
        ctx.AddAuxFile(new AuxFile(textFile, text));

        var options = new List<string>
        {
            "fontfile=" + FilterGraphBuilder.FileName(ResolveFont(ctx, spec.Font)),
            "textfile=" + FilterGraphBuilder.FileName(textFile),
            "expansion=none",
            "fontsize=" + FfNum.Integer(Math.Max(1, ctx.ScalePixels(spec.Size))),
            "fontcolor=" + Color(ctx, spec.Color, "#FFFFFFFF"),
            "borderw=" + FfNum.Integer(Math.Max(0, ctx.ScalePixels(spec.OutlineWidth))),
            "bordercolor=" + Color(ctx, spec.OutlineColor, "#000000FF"),
        };
        if (spec.Box)
        {
            options.Add("box=1");
            options.Add("boxcolor=" + Color(ctx, spec.BoxColor, "#00000080"));
            options.Add("boxborderw=" + FfNum.Integer(Math.Max(0, ctx.ScalePixels(spec.BoxPadding))));
        }

        var (x, y) = spec.Anchor switch
        {
            TitleAnchor.Center => ("(w-text_w)/2", "(h-text_h)/2"),
            TitleAnchor.Top => ("(w-text_w)/2", "h*0.1"),
            _ => ("(w-text_w)/2", "h*0.8-text_h/2"),
        };
        options.Add("x=" + x + Offset(ctx.ScalePixels(spec.OffsetX)));
        options.Add("y=" + y + Offset(ctx.ScalePixels(spec.OffsetY)));

        return ctx.Graph.StartSource("color=c=black@0:s=" + ctx.CanvasSize + ":r=" + FfNum.Rate(ctx.Rate))
            .Then("trim=end_frame=" + FfNum.Integer(segment.Length))
            .Then("format=yuva420p")
            .Then("drawtext=" + string.Join(':', options));
    }

    /// <summary>The content-addressed text file name: <c>t_</c> plus the first 8 hex digits of the SHA-256.</summary>
    /// <param name="normalizedText">The normalised text (<see cref="FilterEscaper.NormalizeTitleText"/>).</param>
    /// <returns>The bare name.</returns>
    public static string TextFileName(string normalizedText) =>
        "t_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText)))[..8] + ".txt";

    /// <summary>
    /// Formats a <c>#RRGGBB</c> or <c>#RRGGBBAA</c> colour for drawtext: <c>0xRRGGBB</c>, plus <c>@alpha</c> when the
    /// alpha is below 1.
    /// </summary>
    /// <param name="value">The colour text.</param>
    /// <returns>The drawtext colour, or null when the text is not a valid colour.</returns>
    public static string? FormatColor(string? value)
    {
        if (value is null || value.Length is not (7 or 9) || value[0] != '#')
        {
            return null;
        }

        for (var i = 1; i < value.Length; i++)
        {
            if (!char.IsAsciiHexDigit(value[i]))
            {
                return null;
            }
        }

        var rgb = "0x" + value.Substring(1, 6).ToUpperInvariant();
        if (value.Length == 7)
        {
            return rgb;
        }

        var alpha = int.Parse(value.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return alpha == 255 ? rgb : rgb + "@" + FfNum.Alpha(alpha / 255.0);
    }

    private static string Color(EmitContext ctx, string value, string fallback)
    {
        if (FormatColor(value) is { } color)
        {
            return color;
        }

        ctx.Warn($"The title colour '{value}' is not #RRGGBB or #RRGGBBAA; {fallback} is used.");
        return FormatColor(fallback)!;
    }

    private static string ResolveFont(EmitContext ctx, string reference)
    {
        if (ctx.Catalog.ResolveFont(reference) is { } resolved && FilterEscaper.IsBareName(resolved))
        {
            return resolved;
        }

        if (reference.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
        {
            var name = reference[BuiltinPrefix.Length..];
            if (BundledFonts.Contains(name, StringComparer.Ordinal))
            {
                return name + ".ttf";
            }
        }

        ctx.Warn($"The title font '{reference}' is not available; Inter Regular is used.");
        return FallbackFont;
    }

    // The brief says offsets append "+<px>"; a negative offset is written "-<px>" rather than "+-<px>".
    private static string Offset(long pixels) => pixels switch
    {
        0 => string.Empty,
        > 0 => "+" + FfNum.Integer(pixels),
        _ => FfNum.Integer(pixels),
    };
}
