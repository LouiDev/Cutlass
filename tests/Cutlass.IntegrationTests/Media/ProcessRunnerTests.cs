using System.Diagnostics;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Parsing;

namespace Cutlass.IntegrationTests.Media;

/// <summary>ProcessRunner with real ffmpeg: binary stdout, stderr tails, cancellation and kill (TECH_BRIEF 5.3, 5.4).</summary>
[Collection(TestMediaCollection.Name)]
public sealed class ProcessRunnerTests
{
    /// <summary>An endless real-time source: runs until stopped.</summary>
    private static readonly string[] EndlessArgs =
        ["-hide_banner", "-v", "error", "-re", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25", "-f", "null", "-"];

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public ProcessRunnerTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task RawvideoFrameHasExactByteCount()
    {
        await using var h = await MediaHarness.CreateAsync();

        var result = await h.Runner.RunAsync(
            h.Ffmpeg(["-hide_banner", "-nostdin", "-v", "error", "-ss", "1", "-i", _media.Cfr25Av, "-frames:v", "1",
                      "-f", "rawvideo", "-pix_fmt", "bgra", "-s", "320x180", "pipe:1"]),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(320 * 180 * 4, result.Stdout.Length);
        Assert.Equal(0, h.Registry.Count);
    }

    [FfmpegFact]
    public async Task StdoutStreamDeliversExactPcm()
    {
        await using var h = await MediaHarness.CreateAsync();
        await using var running = h.Runner.Start(
            h.Ffmpeg(["-hide_banner", "-nostdin", "-v", "error", "-i", _media.Tone, "-t", "1",
                      "-f", "s16le", "-ac", "2", "-ar", "48000", "pipe:1"]));

        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await running.Stdout.ReadAsync(buffer)) > 0)
        {
            total += read;
        }

        Assert.Equal(0, await running.Exited);
        Assert.Equal(48_000 * 2 * 2, total);
    }

    [FfmpegFact]
    public async Task BadCommandPutsStderrTailIntoFfmpegException()
    {
        await using var h = await MediaHarness.CreateAsync();
        var missing = Path.Combine(h.Root, "does-not-exist.mp4");

        var result = await h.Runner.RunAsync(
            h.Ffmpeg(["-hide_banner", "-nostdin", "-v", "error", "-i", missing, "-f", "null", "-"]),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        var ex = Assert.Throws<FfmpegException>(() => result.EnsureSuccess());

        Assert.NotEqual(0, ex.ExitCode);
        Assert.Contains(ex.StderrTail, line => line.Contains("does-not-exist.mp4", StringComparison.Ordinal));
        Assert.Contains("Error opening", ex.Message, StringComparison.Ordinal);
        Assert.StartsWith("ffmpeg exited with code ", ex.Message, StringComparison.Ordinal);
        Assert.Contains("does-not-exist.mp4", ex.DisplayCommand, StringComparison.Ordinal);
        Assert.True(ex.StderrTail.Count <= ProcessResult.ExceptionTailLines);
    }

    [FfmpegFact]
    public async Task MissingExecutableRaisesFfmpegException()
    {
        await using var h = await MediaHarness.CreateAsync();
        var spec = new ProcessSpec(Path.Combine(h.Root, "no-such-ffmpeg.exe"), ["-version"], h.Work.Path, false, ProcessPurpose.Probe, false);

        var ex = Assert.Throws<FfmpegException>(() => h.Runner.Start(spec));
        Assert.Equal(FfmpegException.NoExitCode, ex.ExitCode);
    }

    [FfmpegFact]
    public async Task CancellationKillsTheProcessAndLeavesNoOrphan()
    {
        await using var h = await MediaHarness.CreateAsync();
        using var cts = new CancellationTokenSource();
        var pidTask = WaitForSingleChildAsync(h);

        var run = h.Runner.RunAsync(h.Ffmpeg(["-nostdin", .. EndlessArgs]), timeout: null, cts.Token);
        var (pid, start) = await pidTask;
        Assert.True(ProcessAssert.IsAlive(pid, start));

        var sw = Stopwatch.StartNew();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        await ProcessAssert.GoneWithinAsync(pid, start, TimeSpan.FromSeconds(5));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "No stdin: cancel kills at once instead of waiting for q.");
        Assert.Equal(0, h.Registry.Count);
    }

    [FfmpegFact]
    public async Task CancellationOfAnExportWritesQFirstAndFfmpegExitsCleanly()
    {
        await using var h = await MediaHarness.CreateAsync();
        var output = Path.Combine(h.Root, "out.partial.mkv");

        // An export-shaped process: stdin open, no -nostdin, writing a file.
        await using var running = h.Runner.Start(h.Ffmpeg(
            ["-hide_banner", "-v", "error", "-re", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25", "-c:v", "ffv1", "-y", output],
            ProcessPurpose.Export,
            redirectStdin: true));
        var start = ProcessAssert.StartTicks(running.Pid);
        await Task.Delay(700);

        var graceful = await running.StopAsync(TimeSpan.FromSeconds(10));

        Assert.True(graceful, "ffmpeg should stop on q before the grace period ends.");
        Assert.Equal(0, await running.Exited);
        Assert.False(ProcessAssert.IsAlive(running.Pid, start));
        Assert.True(new FileInfo(output).Length > 0);
    }

    [FfmpegFact]
    public async Task StopFallsBackToKillWhenQIsIgnored()
    {
        await using var h = await MediaHarness.CreateAsync();

        // With -nostdin ffmpeg ignores the q written to the open stdin, so StopAsync must kill after the grace period.
        await using var running = h.Runner.Start(h.Ffmpeg(["-nostdin", .. EndlessArgs], ProcessPurpose.Export, redirectStdin: true));
        var start = ProcessAssert.StartTicks(running.Pid);

        var graceful = await running.StopAsync(TimeSpan.FromMilliseconds(500));

        Assert.False(graceful);
        await ProcessAssert.GoneWithinAsync(running.Pid, start, TimeSpan.FromSeconds(5));
    }

    [FfmpegFact]
    public async Task DisposingARunningProcessKillsIt()
    {
        await using var h = await MediaHarness.CreateAsync();
        int pid;
        long start;
        await using (var running = h.Runner.Start(h.Ffmpeg(["-nostdin", .. EndlessArgs])))
        {
            pid = running.Pid;
            start = ProcessAssert.StartTicks(pid);
            Assert.Equal(1, h.Registry.Count);
        }

        await ProcessAssert.GoneWithinAsync(pid, start, TimeSpan.FromSeconds(1));
        Assert.Equal(0, h.Registry.Count);
    }

    [FfmpegFact]
    public async Task RegistryKillAllStopsEveryChild()
    {
        await using var h = await MediaHarness.CreateAsync();
        await using var a = h.Runner.Start(h.Ffmpeg(["-nostdin", .. EndlessArgs]));
        await using var b = h.Runner.Start(h.Ffmpeg(["-nostdin", .. EndlessArgs], ProcessPurpose.Background));
        var startA = ProcessAssert.StartTicks(a.Pid);
        var startB = ProcessAssert.StartTicks(b.Pid);
        Assert.Equal(2, h.Registry.Count);

        h.Registry.KillAll();

        await ProcessAssert.GoneWithinAsync(a.Pid, startA, TimeSpan.FromSeconds(5));
        await ProcessAssert.GoneWithinAsync(b.Pid, startB, TimeSpan.FromSeconds(5));
    }

    [FfmpegFact]
    public async Task TimeoutStopsTheProcessAndThrows()
    {
        await using var h = await MediaHarness.CreateAsync();

        var ex = await Assert.ThrowsAsync<FfmpegException>(
            () => h.Runner.RunAsync(h.Ffmpeg(["-nostdin", .. EndlessArgs]), TimeSpan.FromMilliseconds(800), CancellationToken.None));

        Assert.Equal(FfmpegException.NoExitCode, ex.ExitCode);
        Assert.Equal(0, h.Registry.Count);
    }

    [FfmpegFact]
    public async Task OddPathWorksForInputAndOutput()
    {
        await using var h = await MediaHarness.CreateAsync();
        var outputDir = Path.Combine(h.Root, Path.GetFileName(_media.OddDirectory));
        Directory.CreateDirectory(outputDir);
        var output = Path.Combine(outputDir, "out \u00E4 [2] it's.wav");

        var result = await h.Runner.RunAsync(
            h.Ffmpeg(["-hide_banner", "-nostdin", "-v", "error", "-i", _media.OddPathClip, "-vn", "-t", "0.5",
                      "-c:a", "pcm_s16le", "-y", output], ProcessPurpose.Background),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.Equal(0, result.EnsureSuccess().ExitCode);
        Assert.True(File.Exists(output));
        Assert.Contains(_media.OddPathClip, result.DisplayCommand, StringComparison.Ordinal);
    }

    [FfmpegFact]
    public async Task ProgressPipeParsesToTheEnd()
    {
        await using var h = await MediaHarness.CreateAsync();
        await using var running = h.Runner.Start(h.Ffmpeg(
            ["-hide_banner", "-nostdin", "-v", "error", "-i", _media.Cfr25Av, "-f", "null", "-progress", "pipe:1", "-nostats", "-"]));

        var parser = new ProgressParser();
        var blocks = new List<ProgressBlock>();
        using (var reader = new StreamReader(running.Stdout))
        {
            var chunk = new char[7]; // Tiny reads split lines on purpose.
            int n;
            while ((n = await reader.ReadAsync(chunk)) > 0)
            {
                blocks.AddRange(parser.Feed(chunk.AsSpan(0, n)));
            }
        }

        Assert.Equal(0, await running.Exited);
        Assert.NotEmpty(blocks);
        var last = blocks[^1];
        Assert.True(last.IsEnd);
        Assert.Equal(100, last.Frame);
        Assert.InRange(last.OutTimeMicros!.Value, 3_900_000, 4_100_000);
    }

    [Fact]
    public void DisplayCommandQuotesPerPlatform()
    {
        string[] args = ["-i", "C:/a b/it's.mp4", "-vf", "scale=2:2", "say \"hi\""];

        Assert.Equal(
            "ffmpeg -i 'C:/a b/it'\\''s.mp4' -vf scale=2:2 'say \"hi\"'",
            ProcessRunner.FormatDisplayCommand("ffmpeg", args, posix: true));
        Assert.Equal(
            "ffmpeg -i \"C:/a b/it's.mp4\" -vf scale=2:2 \"say \\\"hi\\\"\"",
            ProcessRunner.FormatDisplayCommand("ffmpeg", args, posix: false));
    }

    private static async Task<(int Pid, long Start)> WaitForSingleChildAsync(MediaHarness h)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            var children = h.Registry.Snapshot();
            if (children.Count == 1)
            {
                return (children[0].Pid, children[0].StartTimeUtcTicks);
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The child process never registered.");
    }
}
