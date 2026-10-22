using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;

namespace Cutlass.IntegrationTests.Infrastructure;

/// <summary>
/// The product's process layer wired up for one test: a private work directory, an in-memory child registry, a
/// <see cref="ProcessRunner"/> (with a Job Object on Windows) and the validated test ffmpeg.
/// </summary>
public sealed class MediaHarness : IAsyncDisposable
{
    private readonly TempDirectory _temp;

    private MediaHarness(TempDirectory temp, ChildProcessRegistry registry, ProcessRunner runner, WorkDirectory work)
    {
        _temp = temp;
        Registry = registry;
        Runner = runner;
        Work = work;
    }

    /// <summary>The temp folder holding the work directory and anything else the test writes.</summary>
    public string Root => _temp.Path;

    /// <summary>The child registry (not persisted).</summary>
    public ChildProcessRegistry Registry { get; }

    /// <summary>The runner.</summary>
    public ProcessRunner Runner { get; }

    /// <summary>The work directory.</summary>
    public WorkDirectory Work { get; }

    /// <summary>The validated ffmpeg and ffprobe, set by <see cref="CreateAsync"/>.</summary>
    public FfmpegToolset Toolset { get; private set; } = null!;

    /// <summary>Creates the harness and validates the test ffmpeg with the product code.</summary>
    /// <returns>The harness.</returns>
    public static async Task<MediaHarness> CreateAsync()
    {
        var temp = new TempDirectory("cutlass-harness");
        var registry = new ChildProcessRegistry(recordDirectory: null);
        var runner = ProcessRunner.CreateDefault(registry);
        var work = new WorkDirectory(temp.Combine("work"));
        work.EnsureCreated();
        var harness = new MediaHarness(temp, registry, runner, work);
        harness.Toolset = await FfmpegToolset.ValidateAsync(TestFfmpeg.FfmpegPath, runner, work.Path, CancellationToken.None);
        return harness;
    }

    /// <summary>A spec for ffmpeg in the work directory.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="purpose">The purpose.</param>
    /// <param name="redirectStdin">True to open stdin.</param>
    /// <returns>The spec.</returns>
    public ProcessSpec Ffmpeg(IReadOnlyList<string> args, ProcessPurpose purpose = ProcessPurpose.Preview, bool redirectStdin = false) =>
        new(Toolset.FfmpegPath, args, Work.Path, redirectStdin, purpose, BelowNormalPriority: false);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Registry.KillAll();
        Registry.Dispose();
        _temp.Dispose();
        return ValueTask.CompletedTask;
    }
}
