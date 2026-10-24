using Cutlass.Compiler.Graph;
using Cutlass.Core.Model;

namespace Cutlass.Compiler.Emit;

/// <summary>
/// The per-clip video effect filters (TECH_BRIEF 7.3, effects batch 2). Every method returns null when its value is
/// neutral, so a clip without effects emits exactly the chain it emitted before. Numbers go through <see cref="FfNum"/>.
/// </summary>
internal static class VideoEffectFilters
{
    /// <summary>The <c>hflip</c> filter.</summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the clip is not mirrored horizontally.</returns>
    public static string? FlipH(VideoEffects effects) => effects.FlipH ? "hflip" : null;

    /// <summary>The <c>vflip</c> filter.</summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the clip is not mirrored vertically.</returns>
    public static string? FlipV(VideoEffects effects) => effects.FlipV ? "vflip" : null;

    /// <summary>The <c>colortemperature</c> filter (RGB domain; kept adjacent to <see cref="Tint"/> so ffmpeg converts once).</summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null at 6500 K.</returns>
    public static string? Temperature(VideoEffects effects) =>
        effects.TemperatureKelvin != VideoEffects.None.TemperatureKelvin
            ? "colortemperature=temperature=" + FfNum.Decimal(effects.TemperatureKelvin)
            : null;

    /// <summary>The <c>colorbalance</c> filter for the green to magenta tint: a positive tint takes green out of the midtones.</summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the tint is 0.</returns>
    public static string? Tint(VideoEffects effects) =>
        effects.Tint != 0 ? "colorbalance=gm=" + FfNum.Decimal(-effects.Tint) : null;

    /// <summary>
    /// The conversion back from the RGB domain after <see cref="Temperature"/> or <see cref="Tint"/>. The RGB-only
    /// filters make libavfilter negotiate the preceding <c>scale</c> to RGB output, which bypasses its
    /// <c>out_color_matrix</c>; this explicit scale pinned to yuv420p converts back with BT.709 instead of leaving the
    /// matrix to an auto-inserted scaler.
    /// </summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when neither RGB filter is emitted.</returns>
    public static string? RgbToYuv(VideoEffects effects) =>
        Temperature(effects) is not null || Tint(effects) is not null
            ? "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p"
            : null;

    /// <summary>The <c>hue</c> filter: the hue shift in degrees, with <c>s=0</c> appended for grayscale.</summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the hue is 0 and the clip keeps its colour.</returns>
    public static string? Hue(VideoEffects effects)
    {
        if (effects.HueDegrees == 0 && !effects.Grayscale)
        {
            return null;
        }

        var parts = new List<string>(2);
        if (effects.HueDegrees != 0)
        {
            parts.Add("h=" + FfNum.Decimal(effects.HueDegrees));
        }

        if (effects.Grayscale)
        {
            parts.Add("s=0");
        }

        return "hue=" + string.Join(':', parts);
    }

    /// <summary>The <c>gblur</c> filter. The sigma is in sequence pixels and scales with the render size so proxies and the export look alike.</summary>
    /// <param name="effects">The effects.</param>
    /// <param name="pixelScale">The render width over the sequence width.</param>
    /// <returns>The filter text, or null when the blur is 0.</returns>
    public static string? Blur(VideoEffects effects, double pixelScale) =>
        effects.Blur > 0 ? "gblur=sigma=" + FfNum.Decimal(effects.Blur * pixelScale) : null;

    /// <summary>The <c>unsharp</c> filter: a 5x5 luma kernel with amount 0 to 1.5.</summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the sharpen amount is 0.</returns>
    public static string? Sharpen(VideoEffects effects) =>
        effects.Sharpen > 0 ? "unsharp=lx=5:ly=5:la=" + FfNum.Decimal(1.5 * effects.Sharpen) : null;

    /// <summary>
    /// The <c>vignette</c> filter: strength 0 to 1 maps to the angle 0 to PI/2. Its dithering is off because the dither
    /// noise comes from a generator whose state advances frame by frame, which would make a window that starts inside
    /// the clip differ from the full render (TECH_BRIEF 7.5 range independence).
    /// </summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the vignette is 0.</returns>
    public static string? Vignette(VideoEffects effects) =>
        effects.Vignette > 0 ? "vignette=a=" + FfNum.Decimal(effects.Vignette * Math.PI / 2) + ":dither=0" : null;

    /// <summary>The <c>chromakey</c> filter; it needs the alpha format, so it follows <c>format=yuva420p</c>.</summary>
    /// <param name="ctx">The compile context (for the bad-colour warning).</param>
    /// <param name="clipId">The clip, for the warning.</param>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the key is off or the colour is not #RRGGBB.</returns>
    public static string? ChromaKey(EmitContext ctx, string clipId, VideoEffects effects)
    {
        if (!effects.KeyEnabled)
        {
            return null;
        }

        var color = effects.KeyColor is { Length: 7 } ? TitleEmitter.FormatColor(effects.KeyColor) : null;
        if (color is null)
        {
            ctx.Warn($"The key colour '{effects.KeyColor}' of clip {clipId} is not #RRGGBB; the chroma key is skipped.");
            return null;
        }

        return "chromakey=color=" + color + ":similarity=" + FfNum.Decimal(effects.KeySimilarity)
            + ":blend=" + FfNum.Decimal(effects.KeyBlend);
    }

    /// <summary>
    /// The <c>rotate</c> filter for user rotation (never <c>transpose</c>, which belongs to source orientation).
    /// The frame keeps its size, the corners clip and the uncovered area is transparent, so it follows the alpha format.
    /// </summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the rotation is 0.</returns>
    public static string? Rotate(VideoEffects effects) =>
        effects.RotationDegrees != 0
            ? "rotate=a=" + FfNum.Decimal(effects.RotationDegrees) + "*PI/180:fillcolor=black@0"
            : null;
}
