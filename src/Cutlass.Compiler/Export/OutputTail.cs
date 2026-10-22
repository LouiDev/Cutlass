namespace Cutlass.Compiler.Export;

/// <summary>
/// The end of the video chain (TECH_BRIEF 7.7): what the composite is converted to before it becomes <c>[vout]</c>.
/// </summary>
/// <param name="Kind">The tail kind.</param>
/// <param name="PixelFormat">The final pixel format: the preset's <c>-pix_fmt</c> for exports, <c>bgra</c> for
/// previews, <c>pal8</c> (what <c>paletteuse</c> produces) for GIF.</param>
public sealed record OutputTail(OutputTailKind Kind, string PixelFormat)
{
    /// <summary>The preview tail: <c>format=bgra</c> for rawvideo frames.</summary>
    public static OutputTail Preview { get; } = new(OutputTailKind.Preview, "bgra");

    /// <summary>The GIF palette tail.</summary>
    public static OutputTail Gif { get; } = new(OutputTailKind.Gif, "pal8");

    /// <summary>An export tail whose <c>format=</c> matches the preset's <c>-pix_fmt</c>.</summary>
    /// <param name="pixelFormat">The pixel format, for example <c>yuv420p</c>.</param>
    /// <returns>The tail.</returns>
    public static OutputTail ForExport(string pixelFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pixelFormat);
        return new OutputTail(OutputTailKind.Export, pixelFormat);
    }
}
