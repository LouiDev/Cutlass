using Cutlass.Core.Model;

namespace Cutlass.Compiler.Emit;

/// <summary>
/// Compiles a timeline window into ffmpeg inputs and a filtergraph (TECH_BRIEF 7). One path serves export, scrub,
/// playback and In/Out ranges. The compiler is pure: no I/O, no clock, no culture, and the same inputs always give
/// byte-identical output.
/// </summary>
public static class TimelineCompiler
{
    /// <summary>Above this many inputs the render carries a memory warning (TECH_BRIEF 9).</summary>
    public const int ManyInputsThreshold = 64;

    /// <summary>Compiles a window of the project.</summary>
    /// <param name="p">The project snapshot.</param>
    /// <param name="m">The media catalog.</param>
    /// <param name="r">The request.</param>
    /// <returns>The compiled render.</returns>
    /// <exception cref="ArgumentException">When the request is invalid: an empty or negative window, an odd or
    /// non-positive canvas, a frame step below 1, or neither video nor audio.</exception>
    public static CompiledRender Compile(Project p, IMediaCatalog m, RenderRequest r)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(r);
        Validate(r);

        var ctx = new EmitContext(p, m, r);
        var outputs = new List<string>(2);
        long frames = 0;
        long samples = 0;
        var channels = 0;
        if (r.IncludeVideo)
        {
            frames = VideoEmitter.Emit(ctx);
            outputs.Add("vout");
        }

        if (r.IncludeAudio)
        {
            channels = AudioEmitter.Emit(ctx);
            samples = ctx.WindowSamples;
            outputs.Add("aout");
        }

        var graph = ctx.Graph.Build(outputs);
        if (ctx.Inputs.Count > ManyInputsThreshold)
        {
            ctx.Warn(
                $"The render reads {ctx.Inputs.Count} inputs at once (more than {ManyInputsThreshold}), " +
                "which needs a lot of memory.");
        }

        return new CompiledRender(
            ctx.Inputs.ToList(),
            graph,
            r.IncludeVideo ? "[vout]" : null,
            r.IncludeAudio ? "[aout]" : null,
            frames,
            samples,
            channels,
            [.. ctx.AuxFiles],
            [.. ctx.Warnings]);
    }

    private static void Validate(RenderRequest r)
    {
        if (r.StartFrame < 0 || r.EndFrame <= r.StartFrame)
        {
            throw new ArgumentException(
                $"The window [{r.StartFrame}, {r.EndFrame}) is empty or negative.", nameof(r));
        }

        if (r.CanvasWidth < 2 || r.CanvasHeight < 2 || r.CanvasWidth % 2 != 0 || r.CanvasHeight % 2 != 0)
        {
            throw new ArgumentException(
                $"The canvas {r.CanvasWidth}x{r.CanvasHeight} must be positive and even.", nameof(r));
        }

        if (r.FrameStep < 1)
        {
            throw new ArgumentException("The frame step must be at least 1.", nameof(r));
        }

        if (!r.IncludeVideo && !r.IncludeAudio)
        {
            throw new ArgumentException("A render needs video, audio or both.", nameof(r));
        }

        ArgumentNullException.ThrowIfNull(r.Tail);
    }
}
