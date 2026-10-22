using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;

namespace Cutlass.IntegrationTests.Media;

/// <summary>The kill-on-close Job Object (TECH_BRIEF 5.4) with a real ffmpeg child. Does nothing off Windows.</summary>
public sealed class WindowsJobObjectTests
{
    [FfmpegFact]
    public async Task ClosingTheJobKillsItsChildren()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory("cutlass-job");
        var work = new WorkDirectory(temp.Combine("work"));
        work.EnsureCreated();
        using var registry = new ChildProcessRegistry(recordDirectory: null);
        var job = WindowsJobObject.Create();
        var runner = new ProcessRunner(registry, job);
        Assert.True(runner.UsesJobObject);

        await using var running = runner.Start(new ProcessSpec(
            TestFfmpeg.FfmpegPath,
            ["-hide_banner", "-nostdin", "-v", "error", "-re", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25", "-f", "null", "-"],
            work.Path,
            RedirectStdin: false,
            ProcessPurpose.Background,
            BelowNormalPriority: true));
        var start = ProcessAssert.StartTicks(running.Pid);
        Assert.True(ProcessAssert.IsAlive(running.Pid, start));

        // Closing the last handle is what happens when the app exits or crashes: Windows kills every child.
        job.Dispose();

        await ProcessAssert.GoneWithinAsync(running.Pid, start, TimeSpan.FromSeconds(5));
    }
}
