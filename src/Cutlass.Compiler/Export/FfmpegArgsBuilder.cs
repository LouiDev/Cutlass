using System.Text;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Graph;
using Cutlass.Core.Time;

namespace Cutlass.Compiler.Export;

/// <summary>
/// Builds ffmpeg argument lists from a <see cref="CompiledRender"/> (TECH_BRIEF 7.7, 7.8, 8.1 and 9). Pure: no I/O.
/// Arguments are separate list elements for <c>ProcessStartInfo.ArgumentList</c>; nothing is quoted.
/// </summary>
public static class FfmpegArgsBuilder
{
    /// <summary>The longest graph an InlineOnly build accepts before the compile fails clearly.</summary>
    public const int InlineOnlyLimit = 30_000;

    /// <summary>Previews pass the graph inline up to this many UTF-8 bytes, else as a script file.</summary>
    public const int PreviewInlineLimitBytes = 8 * 1024;

    /// <summary>The audio arguments every preset with audio shares.</summary>
    public static IReadOnlyList<string> CommonAudioArguments { get; } = ["-ar", "48000", "-ac", "2"];

    /// <summary>The arguments of one input: <c>[-loop 1 -framerate R] [-ss S] -t T -i path</c>.</summary>
    /// <param name="input">The input.</param>
    /// <returns>The arguments.</returns>
    public static IReadOnlyList<string> Input(InputSpec input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var args = new List<string>(8);
        if (input.LoopStill)
        {
            args.Add("-loop");
            args.Add("1");
            if (input.StillRate is { } rate)
            {
                args.Add("-framerate");
                args.Add(FfNum.Rate(rate));
            }
        }

        if (input.SeekMicros is { } seek)
        {
            args.Add("-ss");
            args.Add(FfNum.Seconds(seek));
        }

        args.Add("-t");
        args.Add(FfNum.Seconds(input.DurationMicros));
        args.Add("-i");
        args.Add(input.Path);
        return args;
    }

    /// <summary>The arguments of every input, in index order.</summary>
    /// <param name="inputs">The inputs.</param>
    /// <returns>The arguments.</returns>
    public static IReadOnlyList<string> Inputs(IReadOnlyList<InputSpec> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return [.. inputs.SelectMany(Input)];
    }

    /// <summary>
    /// Decides whether a graph goes into a script file (TECH_BRIEF 7.7): exports always use one, previews only above
    /// <see cref="PreviewInlineLimitBytes"/>; InlineOnly builds never do.
    /// </summary>
    /// <param name="purpose">The render purpose.</param>
    /// <param name="graphText">The graph.</param>
    /// <param name="syntax">The detected script syntax.</param>
    /// <returns>True to write <c>g_&lt;sha12&gt;.ffg</c>.</returns>
    public static bool UseScriptFile(RenderPurpose purpose, string graphText, FilterScriptSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(graphText);
        if (syntax == FilterScriptSyntax.InlineOnly)
        {
            return false;
        }

        return purpose == RenderPurpose.Export || Encoding.UTF8.GetByteCount(graphText) > PreviewInlineLimitBytes;
    }

    /// <summary>
    /// The graph arguments: <c>-/filter_complex file</c> (Slash), <c>-filter_complex_script file</c> (LegacyScript) or
    /// <c>-filter_complex graph</c> inline.
    /// </summary>
    /// <param name="graphText">The graph text (used inline when <paramref name="scriptFileName"/> is null).</param>
    /// <param name="scriptFileName">The bare script name in the work directory, or null for inline.</param>
    /// <param name="syntax">The detected script syntax.</param>
    /// <returns>The two arguments.</returns>
    /// <exception cref="InvalidOperationException">When an InlineOnly build would need a graph above
    /// <see cref="InlineOnlyLimit"/> characters.</exception>
    public static IReadOnlyList<string> Graph(string graphText, string? scriptFileName, FilterScriptSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(graphText);
        if (scriptFileName is not null && syntax != FilterScriptSyntax.InlineOnly)
        {
            var option = syntax == FilterScriptSyntax.Slash ? "-/filter_complex" : "-filter_complex_script";
            return [option, FilterEscaper.RequireBareName(scriptFileName)];
        }

        if (syntax == FilterScriptSyntax.InlineOnly && graphText.Length > InlineOnlyLimit)
        {
            throw new InvalidOperationException(
                $"The filtergraph has {graphText.Length} characters, but this ffmpeg build accepts graphs only inline " +
                $"and Cutlass limits inline graphs to {InlineOnlyLimit}. Use ffmpeg 6.0 or newer with script support.");
        }

        return ["-filter_complex", graphText];
    }

    /// <summary>
    /// The full export command (TECH_BRIEF 7.8 and 9), without the executable: global options, inputs, the graph,
    /// the maps, the video codec, the common video options, the audio codec, the common audio options, the container
    /// options and the output path.
    /// </summary>
    /// <param name="render">The compiled render (Purpose Export).</param>
    /// <param name="encoding">The resolved encoders.</param>
    /// <param name="quality">The quality choice.</param>
    /// <param name="rate">The sequence frame rate.</param>
    /// <param name="width">The output width (for bitrate scaling).</param>
    /// <param name="height">The output height (for bitrate scaling).</param>
    /// <param name="graphArguments">The result of <see cref="Graph"/>.</param>
    /// <param name="outputPath">The absolute output path (normally <c>name.partial.ext</c>).</param>
    /// <returns>The arguments.</returns>
    public static IReadOnlyList<string> Export(
        CompiledRender render,
        ExportEncoding encoding,
        ExportQuality quality,
        FrameRate rate,
        int width,
        int height,
        IReadOnlyList<string> graphArguments,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(graphArguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var preset = encoding.Preset;
        var q = preset.QualityValue(quality);
        List<string> args = ["-hide_banner", "-nostats", "-progress", "pipe:1", "-v", "warning", "-y"];
        args.AddRange(Inputs(render.Inputs));
        args.AddRange(graphArguments);

        var writesVideo = preset.HasVideo && render.VideoLabel is not null;
        var writesAudio = preset.HasAudio && render.AudioLabel is not null;
        if (writesVideo)
        {
            args.Add("-map");
            args.Add(render.VideoLabel!);
        }

        if (writesAudio)
        {
            args.Add("-map");
            args.Add(render.AudioLabel!);
        }

        if (writesVideo && encoding.Video is { } video)
        {
            var bitrate = video.UsesBitrate ? OpenH264Bitrate(quality, width, height) : (long?)null;
            args.AddRange(video.Expand(q, bitrate));
            if (preset.Kind == ExportPresetKind.Video)
            {
                args.AddRange(CommonVideoArguments(rate));
            }
        }

        if (writesAudio && encoding.Audio is { } audio)
        {
            args.AddRange(audio.Expand(q, null));
            args.AddRange(CommonAudioArguments);
        }

        args.AddRange(preset.ContainerArguments);
        args.Add(outputPath);
        return args;
    }

    /// <summary>
    /// The video options every video preset shares (not GIF): constant frame rate at the sequence rate and BT.709 tags.
    /// </summary>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The arguments.</returns>
    public static IReadOnlyList<string> CommonVideoArguments(FrameRate rate) =>
    [
        "-fps_mode", "cfr", "-r", FfNum.Rate(rate),
        "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv",
    ];

    /// <summary>
    /// The OpenH264 fallback bitrate: 4, 8, 12 or 20 Mb/s (Draft to Best) at 1080p, scaled by the pixel count.
    /// </summary>
    /// <param name="quality">The quality.</param>
    /// <param name="width">The output width.</param>
    /// <param name="height">The output height.</param>
    /// <returns>The bitrate in bits per second (at least 100 kb/s).</returns>
    public static long OpenH264Bitrate(ExportQuality quality, int width, int height)
    {
        long baseRate = quality switch
        {
            ExportQuality.Draft => 4_000_000,
            ExportQuality.Good => 8_000_000,
            ExportQuality.High => 12_000_000,
            _ => 20_000_000,
        };
        var pixels = (long)Math.Max(1, width) * Math.Max(1, height);
        return Math.Max(100_000, baseRate * pixels / (1920L * 1080L));
    }

    /// <summary>
    /// A rawvideo preview command (TECH_BRIEF 8.1): BGRA frames on stdout. Pass <paramref name="frames"/> = 1 for a scrub.
    /// </summary>
    /// <param name="render">The compiled render with a Preview tail.</param>
    /// <param name="graphArguments">The result of <see cref="Graph"/>.</param>
    /// <param name="frames">The frame limit (<c>-frames:v</c>), or null for all.</param>
    /// <returns>The arguments.</returns>
    /// <exception cref="ArgumentException">When the render has no video.</exception>
    public static IReadOnlyList<string> RawVideo(CompiledRender render, IReadOnlyList<string> graphArguments, long? frames)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(graphArguments);
        var label = render.VideoLabel ?? throw new ArgumentException("The render has no video output.", nameof(render));
        List<string> args = ["-hide_banner", "-nostdin", "-v", "error"];
        args.AddRange(Inputs(render.Inputs));
        args.AddRange(graphArguments);
        args.Add("-map");
        args.Add(label);
        if (frames is { } n)
        {
            args.Add("-frames:v");
            args.Add(FfNum.Integer(n));
        }

        args.AddRange(["-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1"]);
        return args;
    }

    /// <summary>A PCM preview command (TECH_BRIEF 7.6): s16le at 48 kHz with the render's channel count on stdout.</summary>
    /// <param name="render">The compiled render.</param>
    /// <param name="graphArguments">The result of <see cref="Graph"/>.</param>
    /// <returns>The arguments.</returns>
    /// <exception cref="ArgumentException">When the render has no audio.</exception>
    public static IReadOnlyList<string> RawAudio(CompiledRender render, IReadOnlyList<string> graphArguments)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(graphArguments);
        var label = render.AudioLabel ?? throw new ArgumentException("The render has no audio output.", nameof(render));
        List<string> args = ["-hide_banner", "-nostdin", "-v", "error"];
        args.AddRange(Inputs(render.Inputs));
        args.AddRange(graphArguments);
        args.AddRange(["-map", label, "-f", "s16le", "-c:a", "pcm_s16le", "pipe:1"]);
        return args;
    }
}
