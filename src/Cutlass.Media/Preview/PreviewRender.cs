using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Emit;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.Media.Preview;

/// <summary>
/// The render requests and process plumbing the preview engine shares (TECH_BRIEF 8.1 and 8.2): scrub frames,
/// prefetch windows, playback video windows and PerTrack audio windows all go through the one compiler path.
/// </summary>
internal static class PreviewRender
{
    /// <summary>A one-frame scrub request <c>[frame, frame+1)</c> (PreviewFrame, video only).</summary>
    /// <param name="frame">The timeline frame.</param>
    /// <param name="size">The output size.</param>
    /// <param name="useProxies">True to read proxies.</param>
    /// <returns>The request.</returns>
    public static RenderRequest Frame(long frame, PreviewSize size, bool useProxies) =>
        new(frame, frame + 1, size.Width, size.Height, RenderPurpose.PreviewFrame, IncludeVideo: true, IncludeAudio: false,
            useProxies, AudioLayout.Mixed, FrameStep: 1, OutputTail.Preview, LimitPeaks: false);

    /// <summary>A playback or prefetch video window (PreviewVideo, video only, every <paramref name="step"/>-th frame).</summary>
    /// <param name="start">The first frame.</param>
    /// <param name="end">The end frame (exclusive).</param>
    /// <param name="size">The output size.</param>
    /// <param name="useProxies">True to read proxies.</param>
    /// <param name="step">The frame step (the playback speed).</param>
    /// <returns>The request.</returns>
    public static RenderRequest Video(long start, long end, PreviewSize size, bool useProxies, int step) =>
        new(start, end, size.Width, size.Height, RenderPurpose.PreviewVideo, IncludeVideo: true, IncludeAudio: false,
            useProxies, AudioLayout.Mixed, step, OutputTail.Preview, LimitPeaks: false);

    /// <summary>A playback audio window (PreviewAudio, audio only).</summary>
    /// <param name="start">The first frame.</param>
    /// <param name="end">The end frame (exclusive).</param>
    /// <param name="size">The canvas size (audio ignores it; it only has to be valid).</param>
    /// <param name="useProxies">The proxy flag of the session (the compiler decides what it means for audio).</param>
    /// <param name="layout">PerTrack for live mixing, Mixed for the fallback.</param>
    /// <returns>The request.</returns>
    public static RenderRequest Audio(long start, long end, PreviewSize size, bool useProxies, AudioLayout layout) =>
        new(start, end, size.Width, size.Height, RenderPurpose.PreviewAudio, IncludeVideo: false, IncludeAudio: true,
            useProxies, layout, FrameStep: 1, OutputTail.Preview, LimitPeaks: false);

    /// <summary>
    /// Media path to fingerprint for the cache key: originals map to the asset fingerprint, proxies to the fingerprint
    /// plus <c>|proxy</c>.
    /// </summary>
    /// <param name="project">The snapshot.</param>
    /// <param name="catalog">The catalog that resolves the render sources.</param>
    /// <returns>The map.</returns>
    public static Dictionary<string, string> Fingerprints(Project project, IMediaCatalog catalog)
    {
        var map = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var asset in project.Media)
        {
            map.TryAdd(asset.Path, asset.Fingerprint);
            if (catalog.Get(asset.Id) is { } info)
            {
                map.TryAdd(info.Original.Path, asset.Fingerprint);
                if (info.Proxy is { } proxy)
                {
                    map.TryAdd(proxy.Path, asset.Fingerprint + "|proxy");
                }
            }
        }

        return map;
    }

    /// <summary>The <see cref="FrameCache"/> key of one timeline frame (compiled as text only; nothing runs).</summary>
    /// <param name="project">The snapshot.</param>
    /// <param name="catalog">The font-resolved catalog.</param>
    /// <param name="fingerprints">The result of <see cref="Fingerprints"/>.</param>
    /// <param name="frame">The timeline frame (negative frames render frame 0).</param>
    /// <param name="size">The output size.</param>
    /// <param name="useProxies">True to read proxies.</param>
    /// <returns>The key.</returns>
    public static string FrameKey(
        Project project,
        IMediaCatalog catalog,
        IReadOnlyDictionary<string, string> fingerprints,
        long frame,
        PreviewSize size,
        bool useProxies)
    {
        var render = TimelineCompiler.Compile(project, catalog, Frame(Math.Max(0, frame), size, useProxies));
        return FrameCache.Key(render, size, fingerprints);
    }

    /// <summary>
    /// Writes the aux files (and the script when the graph is too long to pass inline) and returns the ffmpeg spec of
    /// a rawvideo BGRA (video) or s16le (audio) preview process. Fails clearly when titles need drawtext and the build
    /// lacks it.
    /// </summary>
    /// <param name="render">The compiled render.</param>
    /// <param name="purpose">The render purpose.</param>
    /// <param name="video">True for rawvideo BGRA, false for s16le PCM.</param>
    /// <param name="frames">The <c>-frames:v</c> limit for video, or null.</param>
    /// <param name="toolset">The ffmpeg toolset.</param>
    /// <param name="capabilities">The capabilities.</param>
    /// <param name="work">The work directory.</param>
    /// <returns>The process spec.</returns>
    public static ProcessSpec Spec(
        CompiledRender render,
        RenderPurpose purpose,
        bool video,
        long? frames,
        FfmpegToolset toolset,
        FfmpegCapabilities capabilities,
        WorkDirectory work)
    {
        if (!capabilities.HasDrawtext && render.GraphText.Contains("drawtext=", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("This ffmpeg build has no drawtext filter, so titles cannot be previewed.");
        }

        work.EnsureCreated();
        foreach (var aux in render.AuxFiles)
        {
            work.WriteFile(aux.FileName, aux.Utf8Content);
        }

        var syntax = capabilities.ScriptSyntax;
        var script = FfmpegArgsBuilder.UseScriptFile(purpose, render.GraphText, syntax) ? work.WriteScript(render.GraphText) : null;
        var graph = FfmpegArgsBuilder.Graph(render.GraphText, script, syntax);
        var args = video ? FfmpegArgsBuilder.RawVideo(render, graph, frames) : FfmpegArgsBuilder.RawAudio(render, graph);
        return new ProcessSpec(toolset.FfmpegPath, args, work.Path, RedirectStdin: false, ProcessPurpose.Preview, BelowNormalPriority: false);
    }

    /// <summary>A short description of a failure for the UI: the message plus the last stderr line when it adds something.</summary>
    /// <param name="ex">The exception.</param>
    /// <returns>The text.</returns>
    public static string Describe(Exception ex)
    {
        if (ex is FfmpegException f && f.StderrTail.Count > 0)
        {
            var last = f.StderrTail[^1].Trim();
            if (last.Length > 0 && !f.Message.Contains(last, StringComparison.Ordinal))
            {
                return f.Message + " " + last;
            }
        }

        return ex.Message;
    }
}
