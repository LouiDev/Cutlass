using System.Globalization;
using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Media.Export;
using Cutlass.Media.Platform;

namespace Cutlass.Agent.Tools;

/// <summary>Exports (AGENT_INTEGRATION 4.4): start, list, status with a wait, cancel.</summary>
internal sealed class ExportTools(ToolDispatcher d)
{
    /// <summary>The longest status wait.</summary>
    public const int MaxWaitMs = 25_000;

    public void Register()
    {
        d.Register("exports.start", StartAsync);
        d.Register("exports.list", ListAsync);
        d.Register("exports.get", GetAsync);
        d.Register("exports.cancel", CancelAsync);
    }

    /// <summary>Maps a job.</summary>
    internal static ExportDto ToDto(ExportJob job)
    {
        var state = job.State;
        return new ExportDto
        {
            Id = job.Id.ToString("N", CultureInfo.InvariantCulture),
            State = EnumWire.Name(state),
            Fraction = Math.Round(job.Fraction, 4),
            EtaSeconds = job.Eta is { } eta ? Math.Round(eta.TotalSeconds, 1) : null,
            Speed = job.Speed is { } s ? Math.Round(s, 3) : null,
            OutputPath = job.OutputPath.Replace('\\', '/'),
            Preset = job.Request.Preset.Id,
            Warnings = [.. job.Warnings],
            Error = job.Error?.Message,
            LogTail = state == ExportJobState.Failed ? [.. job.Log.TakeLast(20)] : [],
        };
    }

    private static ExportService Service(ToolCallContext ctx) => ctx.Host.Services.Export ?? throw MediaTools.Unavailable(ctx);

    private static ExportJob Find(ToolCallContext ctx, string id) =>
        Service(ctx).Jobs.FirstOrDefault(j => string.Equals(j.Id.ToString("N", CultureInfo.InvariantCulture), id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(j.Id.ToString(), id, StringComparison.OrdinalIgnoreCase))
        ?? throw ApiException.NotFound("export", id);

    private static (long Start, long End) RangeOf(Api.Requests.ExportStartRequest req, Project project)
    {
        if (req.Range is { } range && range.ValueKind == JsonValueKind.Object)
        {
            var s = range.TryGetProperty("start", out var a) && a.TryGetInt64(out var av) ? av : throw ApiException.InvalidArgument("range.start", "'range.start' is required.");
            var e = range.TryGetProperty("end", out var b) && b.TryGetInt64(out var bv) ? bv : throw ApiException.InvalidArgument("range.end", "'range.end' is required.");
            return (s, e);
        }

        if (req.Start is not null || req.End is not null)
        {
            return (req.Start ?? 0, req.End ?? ExportRequest.WholeRange(project).End);
        }

        var name = req.Range is { ValueKind: JsonValueKind.String } text ? text.GetString() : "whole";
        return name?.ToLowerInvariant() switch
        {
            "whole" or null => ExportRequest.WholeRange(project),
            "inout" => ExportRequest.InOutRange(project),
            _ => throw ApiException.InvalidArgument("range", "'range' is \"whole\", \"inOut\" or {start, end}."),
        };
    }

    private async Task<ApiResult> StartAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.ExportStartRequest);
        Service(ctx);
        var services = ctx.Host.Services;
        var caps = services.Capabilities ?? throw MediaTools.Unavailable(ctx);
        var catalog = services.Catalog ?? throw MediaTools.Unavailable(ctx);
        var preset = ExportPresets.Find(Args.Required(req.Preset, "preset"))
            ?? throw ApiException.InvalidArgument("preset", $"There is no preset '{req.Preset}'. Use one of: {string.Join(", ", ExportPresets.All.Select(p => p.Id))}.");
        if (preset.UnavailableReason(caps) is { } why)
        {
            throw new ApiException(ErrorCode.Unavailable, $"The {preset.DisplayName} preset is unavailable: {why}");
        }

        var quality = EnumWire.Parse(req.Quality, "quality", ExportPresets.DefaultQuality);
        var output = Args.Required(req.OutputPath, "outputPath");
        if (!Path.HasExtension(output) || !string.Equals(Path.GetExtension(output), preset.Extension, StringComparison.OrdinalIgnoreCase))
        {
            output += preset.Extension;
        }

        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var project = snap.Project;
        var full = d.Paths.CheckWrite(output, null);
        var comparison = services.Paths.PathComparison;
        if (project.Media.Any(m => string.Equals(SafeFull(m.Path), full, comparison)) || (File.Exists(full) && project.Media.Any(m => FileIdentity.SameFile(m.Path, full))))
        {
            throw new ApiException(ErrorCode.Forbidden, "The output would replace a media file of this project. Choose another name.",
                new Dictionary<string, string> { ["path"] = output });
        }

        if (File.Exists(full) && req.Overwrite != true)
        {
            throw ApiException.Conflict("exists", $"'{output}' already exists. Pass overwrite: true to replace it, or choose another name.");
        }

        var (start, end) = RangeOf(req, project);
        if (start < 0 || end <= start)
        {
            throw ApiException.InvalidArgument("range", string.Create(CultureInfo.InvariantCulture, $"The export range [{start}, {end}) is empty. Place clips first or fix the range."));
        }

        var settings = project.Sequence.Settings;
        var (width, height) = preset.Kind switch
        {
            ExportPresetKind.Gif => ExportPresets.CanvasForWidth(settings, (int)Args.InRange(req.Width ?? 480, 16, 3840, "width")),
            ExportPresetKind.Video when req.Height is { } h => ExportPresets.CanvasForHeight(settings, (int)Args.InRange(h, 16, 4320, "height")),
            _ => (settings.Width / 2 * 2, settings.Height / 2 * 2),
        };
        var request = new ExportRequest(project, catalog, preset, quality, width, height, start, end, req.LimitPeaks ?? true, full);
        var job = await d.Edits.HostOperationAsync(ctx, () => ctx.Host.StartExportAsync(request, ctx.Call.LockId!, ctx.Ct)).ConfigureAwait(false);
        var id = job.Id.ToString("N", CultureInfo.InvariantCulture);
        job.ProgressChanged += (_, p) => d.Events.Add(new EventDto { Kind = "exportProgress", ExportId = id, State = EnumWire.Name(p.State), Fraction = Math.Round(p.Fraction, 4) });
        return ctx.Ok(ToDto(job), AgentJsonContext.Relaxed.ExportDto);
    }

    private static string SafeFull(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return path;
        }
    }

    private Task<ApiResult> ListAsync(ToolCallContext ctx)
    {
        List<ExportDto> list = [.. Service(ctx).Jobs.Select(ToDto)];
        return Task.FromResult(ctx.Ok(list, AgentJsonContext.Relaxed.ListExportDto));
    }

    private async Task<ApiResult> GetAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.ExportIdRequest);
        var job = Find(ctx, Args.Required(req.ExportId, "exportId"));
        var wait = (int)Args.InRange(req.WaitMs ?? 0, 0, MaxWaitMs, "waitMs");
        if (wait > 0 && job.State is ExportJobState.Queued or ExportJobState.Running)
        {
            var initial = job.State;
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnProgress(object? sender, ExportProgress p)
            {
                if (p.State != initial)
                {
                    changed.TrySetResult();
                }
            }

            job.ProgressChanged += OnProgress;
            try
            {
                if (job.State == initial)
                {
                    await Task.WhenAny(changed.Task, job.Completion, Task.Delay(wait, ctx.Ct)).ConfigureAwait(false);
                }
            }
            finally
            {
                job.ProgressChanged -= OnProgress;
            }
        }

        return ctx.Ok(ToDto(job), AgentJsonContext.Relaxed.ExportDto);
    }

    private async Task<ApiResult> CancelAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.ExportIdRequest);
        var job = Find(ctx, Args.Required(req.ExportId, "exportId"));
        await d.Edits.WithLockAsync(ctx, waitWhilePaused: false, () => true).ConfigureAwait(false);
        job.Cancel();
        return ctx.Ok(ToDto(job), AgentJsonContext.Relaxed.ExportDto);
    }
}
