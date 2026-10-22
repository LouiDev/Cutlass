namespace Cutlass.Media.Preview;

/// <summary>
/// The pixel size of a preview frame (TECH_BRIEF 8.1). Both dimensions are even and at least 2, because the compiler
/// rejects odd canvases; <see cref="FrameServer.FitSize"/> produces such sizes.
/// </summary>
/// <param name="Width">The width in pixels (even).</param>
/// <param name="Height">The height in pixels (even).</param>
public sealed record PreviewSize(int Width, int Height)
{
    /// <summary>True when both dimensions are even and at least 2.</summary>
    public bool IsValid => Width >= 2 && Height >= 2 && Width % 2 == 0 && Height % 2 == 0;

    /// <summary>The byte length of one BGRA frame of this size, <c>Width * Height * 4</c>.</summary>
    public long FrameBytes => (long)Width * Height * 4;
}
