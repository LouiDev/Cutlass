using Cutlass.Compiler.Export;

namespace Cutlass.Compiler;

/// <summary>
/// What to compile: a window of timeline frames at a canvas size, for one purpose (TECH_BRIEF 7.1).
/// </summary>
/// <param name="StartFrame">The first timeline frame of the window (inclusive).</param>
/// <param name="EndFrame">The end of the window (exclusive). Must be greater than <paramref name="StartFrame"/>.</param>
/// <param name="CanvasWidth">The output width in pixels (even). Pixel quantities scale by
/// <c>CanvasWidth / sequence width</c>.</param>
/// <param name="CanvasHeight">The output height in pixels (even).</param>
/// <param name="Purpose">Why the range is compiled.</param>
/// <param name="IncludeVideo">True to compile the video graph (<c>[vout]</c>).</param>
/// <param name="IncludeAudio">True to compile the audio graph (<c>[aout]</c>).</param>
/// <param name="UseProxies">True to read video from proxies where one exists.</param>
/// <param name="AudioLayout">Mixed stereo or one stereo pair per track.</param>
/// <param name="FrameStep">Output every n-th frame (playback at 2x and 4x); 1 outputs every frame.</param>
/// <param name="Tail">The video tail: export pixel format, preview BGRA or the GIF palette chain.</param>
/// <param name="LimitPeaks">True to run the mixed audio through the peak limiter.</param>
public sealed record RenderRequest(
    long StartFrame,
    long EndFrame,
    int CanvasWidth,
    int CanvasHeight,
    RenderPurpose Purpose,
    bool IncludeVideo,
    bool IncludeAudio,
    bool UseProxies,
    AudioLayout AudioLayout,
    int FrameStep,
    OutputTail Tail,
    bool LimitPeaks)
{
    /// <summary>The window length in frames, <c>N = EndFrame - StartFrame</c>.</summary>
    public long FrameCount => EndFrame - StartFrame;
}
