using Cutlass.Media.Parsing;

namespace Cutlass.Media.Export;

/// <summary>
/// A progress report of an export (TECH_BRIEF 9): <c>fraction = out_time_us / (N/R * 1e6)</c> and
/// <c>ETA = remaining / speed</c>.
/// </summary>
/// <param name="State">The job state.</param>
/// <param name="Fraction">The fraction done, 0 to 1.</param>
/// <param name="Eta">The estimated time left, or null while ffmpeg reports no speed.</param>
/// <param name="Speed">The encoding speed as a multiple of real time, or null.</param>
/// <param name="OutTimeMicros">The output position in microseconds.</param>
public sealed record ExportProgress(ExportJobState State, double Fraction, TimeSpan? Eta, double? Speed, long OutTimeMicros)
{
    /// <summary>Computes a report from a parsed <c>-progress</c> block.</summary>
    /// <param name="block">The block.</param>
    /// <param name="totalMicros">The planned output duration in microseconds.</param>
    /// <param name="previous">The previous report, whose position is kept when the block has none (<c>N/A</c>).</param>
    /// <returns>The report (state <see cref="ExportJobState.Running"/>).</returns>
    public static ExportProgress FromBlock(ProgressBlock block, long totalMicros, ExportProgress? previous)
    {
        ArgumentNullException.ThrowIfNull(block);
        var outTime = Math.Clamp(block.OutTimeMicros ?? previous?.OutTimeMicros ?? 0, 0, Math.Max(0, totalMicros));
        var fraction = block.IsEnd ? 1.0 : ProgressParser.Fraction(outTime, totalMicros);
        var speed = block.Speed is > 0 ? block.Speed : null;
        TimeSpan? eta = null;
        if (speed is { } s)
        {
            var remainingSeconds = Math.Max(0, totalMicros - outTime) / 1_000_000.0 / s;
            eta = TimeSpan.FromSeconds(Math.Round(remainingSeconds, 1));
        }

        return new ExportProgress(ExportJobState.Running, fraction, eta, speed, outTime);
    }
}
