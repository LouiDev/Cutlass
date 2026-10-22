using System.Text.Json;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;

namespace Cutlass.IntegrationTests.Media;

/// <summary>
/// The pid registry: orphan selection (unit level), per-instance record files and a real sweep of a persisted child
/// (TECH_BRIEF 5.4).
/// </summary>
public sealed class ChildProcessRegistryTests
{
    private const long T0 = 638_900_000_000_000_000;

    [Fact]
    public void SelectsOnlyLiveFfmpegProcessesWithMatchingStartTime()
    {
        var tolerance = ChildProcessRegistry.StartTimeTolerance.Ticks;
        ChildProcessRecord[] recorded =
        [
            new(10, T0, "/usr/bin/ffmpeg"),                       // match
            new(11, T0, "/usr/bin/ffprobe"),                      // match (time at the tolerance edge)
            new(12, T0, "/usr/bin/ffmpeg"),                       // pid reused by another program
            new(13, T0, "/usr/bin/ffmpeg"),                       // pid reused by a later ffmpeg
            new(14, T0, "/usr/bin/ffmpeg"),                       // gone
            new(15, T0, "C:/Tools/ffmpeg.exe"),                   // match, case-insensitive name
            new(0, T0, "/usr/bin/ffmpeg"),                        // invalid pid
        ];
        var live = new Dictionary<int, (long, string)>
        {
            [10] = (T0 + 1000, "ffmpeg"),
            [11] = (T0 - tolerance, "ffprobe"),
            [12] = (T0, "bash"),
            [13] = (T0 + tolerance + 1, "ffmpeg"),
            [15] = (T0, "FFMPEG"),
        };

        var orphans = ChildProcessRegistry.SelectOrphans(recorded, pid => live.TryGetValue(pid, out var v) ? v : null);

        Assert.Equal(new[] { 10, 11, 15 }, orphans.Select(o => o.Pid));
    }

    [Fact]
    public void SweepWithoutFilesDoesNothing()
    {
        using var temp = new TempDirectory("cutlass-pids");
        using var registry = new ChildProcessRegistry(temp.Path);

        Assert.Equal(0, registry.SweepOrphans());
        Assert.Equal(0, new ChildProcessRegistry(null).SweepOrphans());
    }

    [Fact]
    public void CorruptAndLegacyFilesAreDeletedWithoutKilling()
    {
        using var temp = new TempDirectory("cutlass-pids");
        var corrupt = temp.Combine("pids-1-2.json");
        var legacy = temp.Combine("pids.json");
        File.WriteAllText(corrupt, "{ not json");
        File.WriteAllText(legacy, "[]");
        using var registry = new ChildProcessRegistry(temp.Path);

        Assert.Equal(0, registry.SweepOrphans());
        Assert.False(File.Exists(corrupt));
        Assert.False(File.Exists(legacy));
    }

    [FfmpegFact]
    public async Task PersistedChildIsKilledByTheNextStartsSweepOnlyOnceItsOwnerIsGone()
    {
        using var temp = new TempDirectory("cutlass-pids");
        var work = new WorkDirectory(temp.Combine("work"));
        work.EnsureCreated();

        // "Previous run": a registry that persists, and no Job Object, like Linux.
        var crashed = new ChildProcessRegistry(temp.Path);
        var runner = new ProcessRunner(crashed, job: null);
        var running = runner.Start(SpinningFfmpeg(work));
        var start = ProcessAssert.StartTicks(running.Pid);
        try
        {
            crashed.Flush();
            var file = crashed.RecordFile!;
            var written = Read(file);
            var child = Assert.Single(written.Children);
            Assert.Equal(running.Pid, child.Pid);

            // While the owner (this process) runs, a second instance's sweep leaves its file and child alone.
            using var next = new ChildProcessRegistry(temp.Path);
            Assert.Equal(0, next.SweepOrphans());
            Assert.True(File.Exists(file));

            // The owner is gone: the same record under a pid that no longer runs.
            File.Delete(file);
            var dead = Write(temp, written with { OwnerPid = int.MaxValue - 7 });
            Assert.Equal(1, next.SweepOrphans());
            await ProcessAssert.GoneWithinAsync(running.Pid, start, TimeSpan.FromSeconds(5));
            Assert.False(File.Exists(dead));
        }
        finally
        {
            await running.DisposeAsync();
            crashed.Dispose();
        }
    }

    [FfmpegFact]
    public async Task AFileWhoseOwnerStillRunsIsNotSwept()
    {
        // The second instance's startup sweep must not kill the first instance's live children (the Linux finding).
        // The live ffmpeg plays the owning instance here, so the real liveness lookup decides.
        using var temp = new TempDirectory("cutlass-pids");
        var work = new WorkDirectory(temp.Combine("work"));
        work.EnsureCreated();
        using var first = new ChildProcessRegistry(temp.Path);
        var runner = new ProcessRunner(first, job: null);
        var running = runner.Start(SpinningFfmpeg(work));
        try
        {
            first.Flush();
            var child = Assert.Single(Read(first.RecordFile!).Children);
            File.Delete(first.RecordFile!);
            var owned = Write(temp, new ChildProcessFile(child.Pid, child.StartTimeUtcTicks, ChildProcessRegistry.CurrentBootId(), [child]));

            using var second = new ChildProcessRegistry(temp.Path);
            Assert.Equal(0, second.SweepOrphans());
            Assert.True(File.Exists(owned));
            Assert.False(running.Exited.IsCompleted);

            // From another boot the same file is stale: deleted, nothing killed.
            File.Delete(owned);
            var stale = Write(temp, new ChildProcessFile(child.Pid, child.StartTimeUtcTicks, "another-boot", [child]));
            Assert.Equal(0, second.SweepOrphans());
            Assert.False(File.Exists(stale));
            Assert.False(running.Exited.IsCompleted);
        }
        finally
        {
            await running.KillAsync();
            await running.DisposeAsync();
        }
    }

    [FfmpegFact]
    public async Task WritesAreThrottledAndTheFileDisappearsWithoutChildren()
    {
        using var temp = new TempDirectory("cutlass-pids");
        var work = new WorkDirectory(temp.Combine("work"));
        work.EnsureCreated();
        using var registry = new ChildProcessRegistry(temp.Path);
        var runner = new ProcessRunner(registry, job: null);

        var result = await runner.RunAsync(
            new ProcessSpec(TestFfmpeg.FfmpegPath, ["-hide_banner", "-nostdin", "-version"], work.Path, false, ProcessPurpose.Probe, false),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.Equal(0, result.ExitCode);

        // Register and unregister both scheduled a write; within the throttle window the file settles on "no children",
        // which removes it.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline && File.Exists(registry.RecordFile))
        {
            await Task.Delay(50);
        }

        Assert.False(File.Exists(registry.RecordFile));
        Assert.Equal(0, registry.Count);
    }

    private static ProcessSpec SpinningFfmpeg(WorkDirectory work) => new(
        TestFfmpeg.FfmpegPath,
        ["-hide_banner", "-nostdin", "-v", "error", "-re", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25", "-f", "null", "-"],
        work.Path,
        RedirectStdin: false,
        ProcessPurpose.Background,
        BelowNormalPriority: true);

    private static ChildProcessFile Read(string path) =>
        JsonSerializer.Deserialize(File.ReadAllText(path), Cutlass.Media.MediaJsonContext.Default.ChildProcessFile)!;

    private static string Write(TempDirectory folder, ChildProcessFile file)
    {
        var path = folder.Combine(ChildProcessRegistry.FileNameFor(file.OwnerPid, file.OwnerStartTicks));
        File.WriteAllText(path, JsonSerializer.Serialize(file, Cutlass.Media.MediaJsonContext.Default.ChildProcessFile));
        return path;
    }
}
