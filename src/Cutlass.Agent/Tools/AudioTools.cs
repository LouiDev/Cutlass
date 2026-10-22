using System.Collections.Immutable;
using System.Globalization;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Agent.Tools;

/// <summary>Mix analysis (AGENT_INTEGRATION 4.4): <c>audio.levels</c> and <c>audio.silences</c>.</summary>
internal sealed class AudioTools(ToolDispatcher d)
{
    /// <summary>The longest analysed range in seconds (30 minutes).</summary>
    public const double MaxSeconds = 30 * 60;

    /// <summary>The most intervals per call.</summary>
    public const int MaxIntervals = 100_000;

    public void Register()
    {
        d.Register("audio.levels", LevelsAsync);
        d.Register("audio.silences", SilencesAsync);
    }

    /// <summary>Enforces the 4.6 limits: at most 30 minutes and 100,000 intervals.</summary>
    internal static void CheckLevelLimits(double seconds, int intervalMs)
    {
        if (seconds > MaxSeconds)
        {
            throw new ApiException(ErrorCode.PayloadTooLarge, "At most 30 minutes can be analysed per call; split the range.",
                new Dictionary<string, string> { ["field"] = "end" });
        }

        if (seconds * 1000 / intervalMs > MaxIntervals)
        {
            throw new ApiException(ErrorCode.PayloadTooLarge, "The range holds more than 100000 intervals; raise intervalMs or split the range.",
                new Dictionary<string, string> { ["field"] = "intervalMs" });
        }
    }

    /// <summary>A snapshot in which only the given audio tracks are heard (solo), or the project itself.</summary>
    internal static Project Solo(Project project, IReadOnlyList<string>? trackIds)
    {
        if (trackIds is not { Count: > 0 })
        {
            return project;
        }

        foreach (var id in trackIds)
        {
            var track = TimelineQuery.FindTrack(project, id) ?? throw ApiException.NotFound("track", id);
            if (track.Kind != TrackKind.Audio)
            {
                throw ApiException.InvalidArgument("trackIds", $"'{id}' is a video track; levels need audio tracks.");
            }
        }

        var tracks = project.Sequence.Tracks
            .Select(t => t.Kind == TrackKind.Audio ? t with { Solo = trackIds.Contains(t.Id, StringComparer.Ordinal), Muted = false } : t)
            .ToImmutableArray();
        return project with { Sequence = project.Sequence with { Tracks = tracks } };
    }

    private async Task<(List<double> Rms, List<double> Peak)> AnalyzeAsync(ToolCallContext ctx, Project project, long start, long end, int intervalMs)
    {
        var reader = ctx.Host.Services.AudioReader ?? throw MediaTools.Unavailable(ctx);
        var rate = project.Sequence.Settings.Rate;
        var samples = TimeMath.SamplesAt(end, rate) - TimeMath.SamplesAt(start, rate);
        var analyzer = new LevelAnalyzer(intervalMs, samples);
        await reader.ReadAsync(project, start, end, pcm =>
        {
            analyzer.Add(pcm.Span);
            return ValueTask.CompletedTask;
        }, ctx.Ct).ConfigureAwait(false);
        return analyzer.Complete();
    }

    private static (long Start, long End) Range(long? start, long? end, Project project, bool defaultWhole)
    {
        var s = start ?? (defaultWhole ? 0 : throw ApiException.InvalidArgument("start", "'start' is required."));
        var e = end ?? (defaultWhole ? TimelineQuery.Duration(project) : throw ApiException.InvalidArgument("end", "'end' is required."));
        if (s < 0)
        {
            throw ApiException.InvalidArgument("start", "'start' must not be negative.");
        }

        if (e <= s)
        {
            throw ApiException.InvalidArgument("end", string.Create(CultureInfo.InvariantCulture, $"'end' ({e}) must be after 'start' ({s})."));
        }

        return (s, e);
    }

    private async Task<ApiResult> LevelsAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.AudioLevelsRequest);
        var interval = (int)Args.InRange(req.IntervalMs ?? 50, 10, 1000, "intervalMs");
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var (start, end) = Range(req.Start, req.End, snap.Project, defaultWhole: false);
        var rate = snap.Project.Sequence.Settings.Rate;
        CheckLevelLimits(TimeMath.SecondsAt(end - start, rate).ToDouble(), interval);
        var project = Solo(snap.Project, Args.OptionalIds(req.TrackIds, "trackIds"));
        var (rms, peak) = await AnalyzeAsync(ctx, project, start, end, interval).ConfigureAwait(false);
        var dto = new LevelsDto { StartFrame = start, IntervalMs = interval, RmsDb = rms, PeakDb = peak };
        return ApiResult.Ok(snap.Revision, dto, AgentJsonContext.Relaxed.LevelsDto);
    }

    private async Task<ApiResult> SilencesAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.SilencesRequest);
        var threshold = Args.InRange(req.ThresholdDb ?? -40, -120, 0, "thresholdDb");
        var minMs = (int)Args.InRange(req.MinDurationMs ?? 500, 0, 3_600_000, "minDurationMs");
        var padMs = (int)Args.InRange(req.PadMs ?? 100, 0, 60_000, "padMs");
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var (start, end) = Range(req.Start, req.End, snap.Project, defaultWhole: true);
        var rate = snap.Project.Sequence.Settings.Rate;
        var seconds = TimeMath.SecondsAt(end - start, rate).ToDouble();
        // 10 ms resolution, coarser only when the range would exceed the interval limit.
        var interval = Math.Max(10, (int)Math.Ceiling(seconds * 1000 / MaxIntervals));
        CheckLevelLimits(seconds, interval);
        var project = Solo(snap.Project, Args.OptionalIds(req.TrackIds, "trackIds"));
        var (rms, _) = await AnalyzeAsync(ctx, project, start, end, interval).ConfigureAwait(false);
        var ranges = SilenceFinder.Find(rms, interval, threshold, minMs, padMs, start, end, rate);
        var dto = new SilencesDto
        {
            Ranges = [.. ranges.Select(r => new SilenceRangeDto { Start = r.Start, End = r.End, Seconds = Math.Round(TimeMath.SecondsAt(r.End - r.Start, rate).ToDouble(), 3) })],
        };
        return ApiResult.Ok(snap.Revision, dto, AgentJsonContext.Relaxed.SilencesDto);
    }
}
