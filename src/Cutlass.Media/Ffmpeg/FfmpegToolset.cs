using Cutlass.Compiler.Capabilities;
using Cutlass.Media.Parsing;

namespace Cutlass.Media.Ffmpeg;

/// <summary>A validated ffmpeg and the ffprobe next to it (TECH_BRIEF 5.2).</summary>
/// <param name="FfmpegPath">Full path of ffmpeg.</param>
/// <param name="FfprobePath">Full path of ffprobe, in the same folder.</param>
/// <param name="Version">What <c>-version</c> reported.</param>
public sealed record FfmpegToolset(string FfmpegPath, string FfprobePath, FfmpegVersion Version)
{
    /// <summary>How long <c>-version</c> may take.</summary>
    public static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Validates an ffmpeg: ffprobe must sit next to it, <c>-hide_banner -nostdin -version</c> must parse, and libavfilter
    /// must be at least <see cref="FfmpegCapabilities.MinimumAvfilterMajor"/>.
    /// </summary>
    /// <param name="ffmpegPath">Full path of ffmpeg.</param>
    /// <param name="runner">Runs the version query.</param>
    /// <param name="workingDirectory">The work directory to run in.</param>
    /// <param name="cancellationToken">Cancels validation.</param>
    /// <returns>The toolset.</returns>
    /// <exception cref="FfmpegException">When ffmpeg cannot run, ffprobe is missing, the output does not parse or
    /// the build is too old.</exception>
    public static async Task<FfmpegToolset> ValidateAsync(
        string ffmpegPath,
        ProcessRunner runner,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentNullException.ThrowIfNull(runner);

        var ffmpeg = Path.GetFullPath(ffmpegPath);
        var ffprobe = FfmpegLocator.ProbePathFor(ffmpeg);
        if (!File.Exists(ffprobe))
        {
            throw new FfmpegException($"ffprobe was not found next to {ffmpeg}.", FfmpegException.NoExitCode, [], ffmpeg);
        }

        // Linux: a picked or bundled build may have lost its execute bits (unzipped); restore them where allowed.
        FfmpegLocator.TryMakeExecutable(ffmpeg);
        FfmpegLocator.TryMakeExecutable(ffprobe);

        var spec = new ProcessSpec(ffmpeg, ["-hide_banner", "-nostdin", "-version"], workingDirectory, false, ProcessPurpose.Probe, false);
        var result = await runner.RunAsync(spec, ValidationTimeout, cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess();

        var version = VersionParser.Parse(result.StdoutText)
            ?? throw new FfmpegException(
                "The output of ffmpeg -version could not be read.",
                result.ExitCode,
                result.Tail(ProcessResult.ExceptionTailLines),
                result.DisplayCommand);
        if (version.AvfilterMajor < FfmpegCapabilities.MinimumAvfilterMajor)
        {
            throw new FfmpegException(
                $"ffmpeg {version.BuildId} is too old: libavfilter {version.AvfilterMajor}, Cutlass needs " +
                $"{FfmpegCapabilities.MinimumAvfilterMajor} (ffmpeg 6.0) or newer.",
                result.ExitCode,
                [],
                result.DisplayCommand);
        }

        return new FfmpegToolset(ffmpeg, ffprobe, version);
    }
}
