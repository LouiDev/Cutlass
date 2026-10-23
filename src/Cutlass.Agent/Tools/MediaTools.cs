using System.Collections.Immutable;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto.Media;
using Cutlass.Agent.Tools.Analysis;
using Cutlass.Agent.Tools.Dispatch;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Agent.Tools;

/// <summary>The media tools (AGENT_INTEGRATION 4.4): list, get, import, remove, peaks and levels.</summary>
internal sealed class MediaTools(ToolDispatcher d)
{
    /// <summary>The most files per import.</summary>
    public const int MaxImportPaths = 32;

    public void Register()
    {
        d.Register("media.list", ListAsync);
        d.Register("media.get", GetAsync);
        d.Register("media.import", ImportAsync);
        d.Register("media.remove", RemoveAsync);
        d.Register("media.peaks", PeaksAsync);
        d.Register("media.levels", LevelsAsync);
    }

    internal static ApiException Unavailable(ToolCallContext ctx) =>
        new(ErrorCode.Unavailable, "ffmpeg is not ready: " + (ctx.Host.Services.NotReadyReason ?? "media services are unavailable.") + " Tell your user.");

    private static MediaAsset Find(Project project, string id) =>
        TimelineQuery.FindMedia(project, id) ?? throw ApiException.NotFound("media", id);

    private async Task<ApiResult> ListAsync(ToolCallContext ctx)
    {
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        List<MediaDto> list = [.. snap.Project.Media.Select(m => ProjectSummaryMapper.MapMedia(m, d.IsOnline(m.Id)))];
        return ApiResult.Ok(snap.Revision, list, AgentJsonContext.Relaxed.ListMediaDto);
    }

    private async Task<ApiResult> GetAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.MediaIdRequest);
        var id = Args.Required(req.MediaId, "mediaId");
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var asset = Find(snap.Project, id);
        return ApiResult.Ok(snap.Revision, ProjectSummaryMapper.MapMedia(asset, d.IsOnline(id)), AgentJsonContext.Relaxed.MediaDto);
    }

    private async Task<ApiResult> ImportAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.ImportRequest);
        if (req.Paths is not { Count: > 0 } paths)
        {
            throw ApiException.InvalidArgument("paths", "'paths' needs at least one file.");
        }

        if (paths.Count > MaxImportPaths)
        {
            throw new ApiException(ErrorCode.PayloadTooLarge, $"At most {MaxImportPaths} files can be imported per call; split the list.",
                new Dictionary<string, string> { ["field"] = "paths" });
        }

        if (!ctx.Host.Services.IsMediaReady)
        {
            throw Unavailable(ctx);
        }

        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var resolved = paths.Select(p => d.Paths.CheckRead(p, snap.Project, snap.ProjectPath)).ToList();
        var outcome = await d.Edits.HostOperationAsync(ctx, () => ctx.Host.ImportAsync(resolved, ctx.Call.LockId!, ctx.Ct)).ConfigureAwait(false);
        var dto = new ImportResultDto
        {
            Imported = [.. outcome.Imported.Select(m => ProjectSummaryMapper.MapMedia(m, d.IsOnline(m.Id)))],
            Failed = [.. outcome.Failed.Select(f => new ImportFailureDto { Path = f.Path.Replace('\\', '/'), Message = f.Message })],
        };
        return ctx.Ok(dto, AgentJsonContext.Relaxed.ImportResultDto, outcome.Warnings);
    }

    private Task<ApiResult> RemoveAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.MediaIdRequest);
        var id = Args.Required(req.MediaId, "mediaId");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            Find(p, id);
            return TimelineEdits.RemoveMedia(p, [id]);
        });
    }

    private async Task<ApiResult> PeaksAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.PeaksRequest);
        var id = Args.Required(req.MediaId, "mediaId");
        var bins = (int)Args.InRange(req.Bins ?? 1000, 1, 4000, "bins");
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var asset = Find(snap.Project, id);
        if (asset.Info.Audio is null)
        {
            throw ApiException.InvalidArgument("mediaId", "This media has no audio.");
        }

        var waveforms = ctx.Host.Services.Waveforms ?? throw Unavailable(ctx);
        var pyramid = waveforms.TryGet(asset);
        if (pyramid is null)
        {
            // Build in the background (cached by the waveform service) and ask the agent to retry.
            _ = Task.Run(async () =>
            {
                try
                {
                    await waveforms.GetAsync(asset, null, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    ctx.Host.Services.Log?.WriteException("agent: peaks", ex);
                }
            });
            throw new ApiException(ErrorCode.Busy, "The waveform is being built. Retry in a few seconds.",
                new Dictionary<string, string> { ["reason"] = "building" }, 3000);
        }

        var lengthFlicks = TimeMath.FlicksFromMicros(asset.Info.DurationMicros);
        var start = Math.Max(0, req.StartFlicks ?? 0);
        var end = Math.Min(req.EndFlicks ?? lengthFlicks, lengthFlicks);
        if (end <= start)
        {
            throw ApiException.InvalidArgument("endFlicks", "The range is empty.");
        }

        long Sample(long flicks) => (long)((Int128)flicks * TimeMath.SampleRate / TimeMath.FlicksPerSecond);
        var peaks = pyramid.PeaksFor(Sample(start), Sample(end), bins);
        var seconds = (double)(end - start) / TimeMath.FlicksPerSecond;
        var dto = new PeaksDto
        {
            PeaksPerSecond = Math.Round(bins / seconds, 6),
            Min = [.. peaks.Select(p => Math.Round(p.Min / 32768.0, 5))],
            Max = [.. peaks.Select(p => Math.Round(p.Max / 32768.0, 5))],
        };
        return ApiResult.Ok(snap.Revision, dto, AgentJsonContext.Relaxed.PeaksDto);
    }

    private async Task<ApiResult> LevelsAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.MediaLevelsRequest);
        var id = Args.Required(req.MediaId, "mediaId");
        var interval = (int)Args.InRange(req.IntervalMs ?? 50, 10, 1000, "intervalMs");
        var startFlicks = Args.Flicks(req.StartFlicks, req.StartSeconds, "startFlicks") ?? 0;
        var endArg = Args.Flicks(req.EndFlicks, req.EndSeconds, "endFlicks");
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var asset = Find(snap.Project, id);
        if (asset.Info.Audio is null)
        {
            throw ApiException.InvalidArgument("mediaId", "This media has no audio.");
        }

        var reader = ctx.Host.Services.AudioReader ?? throw Unavailable(ctx);
        var lengthFlicks = TimeMath.FlicksFromMicros(asset.Info.DurationMicros);
        startFlicks = Math.Clamp(startFlicks, 0, lengthFlicks);
        var endFlicks = Math.Clamp(endArg ?? lengthFlicks, startFlicks, lengthFlicks);
        if (endFlicks <= startFlicks)
        {
            throw ApiException.InvalidArgument("endFlicks", "The range is empty.");
        }

        AudioTools.CheckLevelLimits((double)(endFlicks - startFlicks) / TimeMath.FlicksPerSecond, interval);

        // A private one-clip snapshot, as the Source monitor builds it: the media's audio on A1 from frame 0.
        var rate = snap.Project.Sequence.Settings.Rate;
        var frames = (long)(((Int128)(endFlicks - startFlicks) + rate.FlicksPerFrame - 1) / rate.FlicksPerFrame);
        var empty = TimelineEdits.NewProject(new SequentialIds(), snap.Project.Sequence.Settings);
        var clip = new MediaClip("c_levels", 0, frames, true, null, asset.Id, StreamKind.Audio, startFlicks, 100, VideoProps.Default, AudioProps.Default);
        var tracks = empty.Sequence.Tracks.Select(t => t.Kind == TrackKind.Audio && TimelineQuery.KindIndex(empty, t.Id) == 0 ? t with { Clips = [clip] } : t).ToImmutableArray();
        var project = new Project(Project.CurrentSchemaVersion, [asset], empty.Sequence with { Tracks = tracks, HasHadClips = true });
        var analyzer = new LevelAnalyzer(interval, (long)Math.Ceiling((double)(endFlicks - startFlicks) * TimeMath.SampleRate / TimeMath.FlicksPerSecond));
        await reader.ReadAsync(project, 0, frames, pcm =>
        {
            analyzer.Add(pcm.Span);
            return ValueTask.CompletedTask;
        }, ctx.Ct).ConfigureAwait(false);
        var (rms, peak) = analyzer.Complete();
        var dto = new LevelsDto { StartFrame = 0, StartFlicks = startFlicks, IntervalMs = interval, RmsDb = rms, PeakDb = peak };
        return ApiResult.Ok(snap.Revision, dto, AgentJsonContext.Relaxed.LevelsDto);
    }

    private sealed class SequentialIds : IIdGenerator
    {
        private int _next;

        public string NewId(string prefix) => prefix + "lv" + (++_next).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
