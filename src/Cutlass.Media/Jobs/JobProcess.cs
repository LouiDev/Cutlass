using System.Text;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Parsing;

namespace Cutlass.Media.Jobs;

/// <summary>
/// Runs one background ffmpeg process that writes files and reports <c>-progress pipe:1</c> on stdout (thumbnails,
/// proxies): progress becomes <c>out_time_us / total</c>, cancellation and the timeout kill the process, and a
/// non-zero exit throws <see cref="FfmpegException"/>.
/// </summary>
internal static class JobProcess
{
    /// <summary>Runs the process to completion.</summary>
    /// <param name="runner">The process runner.</param>
    /// <param name="spec">The process (with <c>-progress pipe:1 -nostats</c> in its arguments).</param>
    /// <param name="totalMicros">The output duration that counts as 100%.</param>
    /// <param name="progress">Receives fractions from 0 to 1, or null.</param>
    /// <param name="timeout">Kill the process after this long.</param>
    /// <param name="cancellationToken">Cancels (kills) the process.</param>
    /// <returns>A task that completes when the process exited with code 0.</returns>
    /// <exception cref="FfmpegException">When ffmpeg fails or times out.</exception>
    /// <exception cref="OperationCanceledException">When cancelled.</exception>
    public static async Task RunAsync(
        ProcessRunner runner,
        ProcessSpec spec,
        long totalMicros,
        IProgress<double>? progress,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var process = runner.Start(spec);
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var reader = ReadProgressAsync(process.Stdout, totalMicros, progress);
        try
        {
            await process.Exited.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await process.KillAsync().ConfigureAwait(false);
            await IgnoreFailure(reader).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new FfmpegException(
                $"ffmpeg did not finish within {timeout.TotalMinutes:0.#} min and was stopped.",
                FfmpegException.NoExitCode,
                process.StderrTail(ProcessResult.ExceptionTailLines),
                process.DisplayCommand);
        }

        await IgnoreFailure(reader).ConfigureAwait(false);
        var exitCode = await process.Exited.ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new FfmpegException(exitCode, process.StderrTail(ProcessResult.ExceptionTailLines), process.DisplayCommand);
        }

        progress?.Report(1);
    }

    private static async Task ReadProgressAsync(Stream stdout, long totalMicros, IProgress<double>? progress)
    {
        var parser = new ProgressParser();
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[4096];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        int read;
        while ((read = await stdout.ReadAsync(bytes.AsMemory(), CancellationToken.None).ConfigureAwait(false)) > 0)
        {
            var count = decoder.GetChars(bytes, 0, read, chars, 0);
            foreach (var block in parser.Feed(chars.AsSpan(0, count)))
            {
                if (block.OutTimeMicros is { } outTime)
                {
                    progress?.Report(ProgressParser.Fraction(outTime, totalMicros));
                }
            }
        }
    }

    private static async Task IgnoreFailure(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The pipe broke because the process was stopped.
        }
    }
}
