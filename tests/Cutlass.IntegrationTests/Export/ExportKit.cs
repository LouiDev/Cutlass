using System.Collections.Immutable;
using System.Globalization;
using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Emit;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Export;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;
using Cutlass.Media.Services;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// The product's export stack wired up against the test ffmpeg: probe, frame index, capabilities, fonts and the
/// export service, plus helpers to render raw frames and to inspect output files.
/// </summary>
internal sealed class ExportKit : IAsyncDisposable
{
    /// <summary>Flicks per second.</summary>
    public const long F = TimeMath.FlicksPerSecond;

    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(3);
    private readonly TempDirectory _output = new("cutlass-export");

    private ExportKit(MediaHarness harness)
    {
        Harness = harness;
        Probe = new ProbeService(harness.Runner, harness.Toolset, harness.Work);
        Index = new FrameIndexService(harness.Runner, harness.Toolset, harness.Work);
        Fonts = new FontLocator(AppContext.BaseDirectory, harness.Work, harness.Runner);
    }

    /// <summary>The process layer.</summary>
    public MediaHarness Harness { get; }

    /// <summary>The probe service.</summary>
    public ProbeService Probe { get; }

    /// <summary>The frame index service.</summary>
    public FrameIndexService Index { get; }

    /// <summary>The font locator (bundled Inter next to the test assembly).</summary>
    public FontLocator Fonts { get; }

    /// <summary>The detected capabilities.</summary>
    public FfmpegCapabilities Capabilities { get; private set; } = null!;

    /// <summary>The export service.</summary>
    public ExportService Exports { get; private set; } = null!;

    /// <summary>A fresh folder for output files.</summary>
    public string OutputDirectory => _output.Path;

    /// <summary>Creates the kit.</summary>
    public static async Task<ExportKit> CreateAsync()
    {
        var harness = await MediaHarness.CreateAsync();
        var kit = new ExportKit(harness);
        kit.Capabilities = await new CapabilityProbe(harness.Runner, harness.Work, cacheFile: null)
            .ProbeAsync(harness.Toolset, CancellationToken.None);
        kit.Exports = new ExportService(harness.Runner, harness.Toolset, kit.Capabilities, harness.Work, kit.Fonts);
        return kit;
    }

    /// <summary>Probes a file into a media asset.</summary>
    public async Task<MediaAsset> AssetAsync(string id, string path)
    {
        var probe = await Probe.ProbeAsync(path, CancellationToken.None);
        var info = new FileInfo(path);
        return new MediaAsset(id, Path.GetFullPath(path), null, "sha256:" + id, info.Length, info.LastWriteTimeUtc, probe.Info);
    }

    /// <summary>Builds a catalog of originals with their frame indexes.</summary>
    public async Task<TestRenderCatalog> CatalogAsync(IEnumerable<MediaAsset> assets, bool withIndex = true)
    {
        var catalog = new TestRenderCatalog();
        foreach (var asset in assets)
        {
            var index = withIndex ? await Index.GetAsync(asset, CancellationToken.None) : null;
            catalog.Add(new MediaRenderInfo(asset, new RenderSource(asset.Path, asset.Info.FormatStartMicros, index), null));
        }

        return catalog;
    }

    /// <summary>Compiles and renders BGRA frames to memory, the way the frame server does.</summary>
    public async Task<byte[]> RenderBgraAsync(Project project, IMediaCatalog catalog, RenderRequest request)
    {
        var render = TimelineCompiler.Compile(project, catalog, request);
        var args = FfmpegArgsBuilder.RawVideo(render, GraphArguments(render, request.Purpose), frames: null);
        var result = await Harness.Runner.RunAsync(Harness.Ffmpeg(args), RunTimeout, CancellationToken.None);
        Assert.True(result.ExitCode == 0, "ffmpeg failed:\n" + string.Join('\n', result.StderrTail) + "\n" + render.GraphText);
        Assert.Equal(render.FrameCount * request.CanvasWidth * request.CanvasHeight * 4, result.Stdout.LongLength);
        return result.Stdout;
    }

    /// <summary>Compiles and renders s16le PCM to memory, the way playback does.</summary>
    public async Task<byte[]> RenderPcmAsync(Project project, IMediaCatalog catalog, RenderRequest request)
    {
        var render = TimelineCompiler.Compile(project, catalog, request);
        var args = FfmpegArgsBuilder.RawAudio(render, GraphArguments(render, request.Purpose));
        var result = await Harness.Runner.RunAsync(Harness.Ffmpeg(args), RunTimeout, CancellationToken.None);
        Assert.True(result.ExitCode == 0, "ffmpeg failed:\n" + string.Join('\n', result.StderrTail) + "\n" + render.GraphText);
        Assert.Equal(render.SampleCount * render.AudioChannels * 2, result.Stdout.LongLength);
        return result.Stdout;
    }

    /// <summary>An export request for the whole window into the output folder.</summary>
    public ExportRequest Request(
        Project project,
        IMediaCatalog catalog,
        ExportPreset preset,
        string name,
        long start,
        long end,
        int width = 320,
        int height = 180,
        ExportQuality quality = ExportQuality.High,
        bool limitPeaks = false) =>
        new(project, catalog, preset, quality, width, height, start, end, limitPeaks, Path.Combine(OutputDirectory, name + preset.Extension));

    /// <summary>Runs an export to the end.</summary>
    public async Task<ExportJob> ExportAsync(ExportRequest request)
    {
        var job = Exports.Enqueue(request);
        await job.Completion.WaitAsync(RunTimeout);
        return job;
    }

    /// <summary>Asserts a job completed, showing its error and log otherwise.</summary>
    public static void AssertCompleted(ExportJob job)
    {
        Assert.True(
            job.State == ExportJobState.Completed,
            $"Export {job.State}: {job.Error}\n{string.Join('\n', job.Log)}\n{job.DisplayCommand}\n{job.Render?.GraphText}");
        Assert.True(File.Exists(job.OutputPath));
        Assert.False(File.Exists(job.PartialPath));
    }

    /// <summary>Counts the decoded video frames (<c>ffprobe -count_frames</c>).</summary>
    public static async Task<long> CountFramesAsync(string file)
    {
        var output = await ProbeTextAsync(["-v", "error", "-count_frames", "-select_streams", "v:0", "-show_entries", "stream=nb_read_frames", "-of", "csv=p=0", file]);
        return long.Parse(output.Trim(), CultureInfo.InvariantCulture);
    }

    /// <summary>ffprobe stream entries as <c>key=value</c> lines of the first stream of a kind (<c>v</c> or <c>a</c>).</summary>
    public static async Task<Dictionary<string, string>> StreamInfoAsync(string file, string kind)
    {
        var output = await ProbeTextAsync(["-v", "error", "-select_streams", kind + ":0", "-show_entries", "stream", "-of", "default=noprint_wrappers=1", file]);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n'))
        {
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0)
            {
                map.TryAdd(line[..eq].Trim(), line[(eq + 1)..].Trim());
            }
        }

        return map;
    }

    /// <summary>Decodes the first audio stream to interleaved s16le stereo at 48 kHz.</summary>
    public static async Task<short[]> DecodeAudioAsync(string file)
    {
        var bytes = await DecodeAsync(["-i", file, "-map", "0:a:0", "-f", "s16le", "-c:a", "pcm_s16le", "-ac", "2", "-ar", "48000", "pipe:1"]);
        var samples = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);
        return samples;
    }

    /// <summary>Decodes every video frame to 8-bit gray at its own size.</summary>
    public static Task<byte[]> DecodeGrayAsync(string file) =>
        DecodeAsync(["-i", file, "-map", "0:v:0", "-f", "rawvideo", "-pix_fmt", "gray", "-fps_mode", "passthrough", "pipe:1"]);

    /// <summary>The index of the first sample (per channel) whose left value exceeds a threshold, or -1.</summary>
    public static long FirstPeak(short[] interleavedStereo, short threshold, long fromSample = 0)
    {
        for (var i = fromSample; i < interleavedStereo.Length / 2; i++)
        {
            if (Math.Abs((int)interleavedStereo[i * 2]) > threshold)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Every sample index (per channel) whose left value exceeds a threshold.</summary>
    public static ImmutableArray<long> Peaks(short[] interleavedStereo, short threshold)
    {
        var builder = ImmutableArray.CreateBuilder<long>();
        for (long i = 0; i < interleavedStereo.Length / 2; i++)
        {
            if (Math.Abs((int)interleavedStereo[i * 2]) > threshold)
            {
                builder.Add(i);
            }
        }

        return builder.ToImmutable();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Exports.DisposeAsync();
        await Harness.DisposeAsync();
        _output.Dispose();
    }

    private IReadOnlyList<string> GraphArguments(CompiledRender render, RenderPurpose purpose)
    {
        foreach (var aux in render.AuxFiles)
        {
            Harness.Work.WriteFile(aux.FileName, aux.Utf8Content);
        }

        if (render.GraphText.Contains("drawtext=", StringComparison.Ordinal))
        {
            Fonts.EnsureBundledFonts();
        }

        var syntax = Capabilities.ScriptSyntax;
        var script = FfmpegArgsBuilder.UseScriptFile(purpose, render.GraphText, syntax) ? Harness.Work.WriteScript(render.GraphText) : null;
        return FfmpegArgsBuilder.Graph(render.GraphText, script, syntax);
    }

    private static async Task<string> ProbeTextAsync(IReadOnlyList<string> args)
    {
        var result = await TestProcess.RunAsync(TestFfmpeg.FfprobePath, args, Path.GetTempPath(), RunTimeout);
        Assert.True(result.ExitCode == 0, "ffprobe failed: " + result.Stderr);
        return result.Stdout.Replace("\r", string.Empty, StringComparison.Ordinal);
    }

    private static async Task<byte[]> DecodeAsync(IReadOnlyList<string> args)
    {
        List<string> full = ["-hide_banner", "-nostdin", "-v", "error", .. args];
        var result = await TestProcess.RunAsync(TestFfmpeg.FfmpegPath, full, Path.GetTempPath(), RunTimeout);
        Assert.True(result.ExitCode == 0, "ffmpeg decode failed: " + result.Stderr);
        return result.StdoutBytes;
    }
}
