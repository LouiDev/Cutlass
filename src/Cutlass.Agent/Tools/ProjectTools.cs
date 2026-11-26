using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto;
using Cutlass.Agent.Api.Requests;
using Cutlass.Agent.Api.Requests.Project;
using Cutlass.Agent.Tools.Dispatch;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.Agent.Tools;

/// <summary>The project tools (AGENT_INTEGRATION 4.4): get, describe, new, open, save, save-as and shutdown.</summary>
internal sealed class ProjectTools(ToolDispatcher d)
{
    public void Register()
    {
        d.Register("project.get", GetAsync);
        d.Register("project.describe", DescribeAsync);
        d.Register("project.new", NewAsync);
        d.Register("project.open", OpenAsync);
        d.Register("project.save", SaveAsync);
        d.Register("project.save-as", SaveAsAsync);
        d.Register("shutdown", ShutdownAsync);
    }

    /// <summary>The summary of the current project after a host operation.</summary>
    internal static async Task<ApiResult> SummaryAsync(ToolDispatcher d, ToolCallContext ctx, IReadOnlyList<string>? warnings = null)
    {
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var dto = ProjectSummaryMapper.Map(snap.Project, snap.ProjectPath, snap.Dirty, snap.Revision, snap.Playhead, d.IsOnline, null);
        return ApiResult.Ok(snap.Revision, dto, AgentJsonContext.Relaxed.ProjectSummaryDto, warnings);
    }

    private async Task<ApiResult> GetAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.ProjectQueryRequest);
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        if (req.TrackIds is { Count: > 0 } ids)
        {
            foreach (var id in ids.Where(id => TimelineQuery.FindTrack(snap.Project, id) is null))
            {
                throw ApiException.NotFound("track", id);
            }
        }

        var dto = ProjectSummaryMapper.Map(snap.Project, snap.ProjectPath, snap.Dirty, snap.Revision, snap.Playhead, d.IsOnline, req);
        return ApiResult.Ok(snap.Revision, dto, AgentJsonContext.Relaxed.ProjectSummaryDto);
    }

    private async Task<ApiResult> DescribeAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.ProjectQueryRequest);
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var text = ProjectDescriber.Describe(snap.Project, snap.ProjectPath, snap.Dirty, snap.Revision, snap.Playhead, snap.Lock, d.IsOnline, req.From, req.To);
        return new ApiResult { Revision = snap.Revision, Text = text };
    }

    private void CheckUnsaved(bool dirty, bool? discard)
    {
        if (dirty && !(discard == true && d.Host.Policy.AcceptsDiscardChanges))
        {
            throw ApiException.Conflict(
                "unsaved_changes",
                "The project has unsaved changes. Save it first (project/save or save-as) or ask your user.");
        }
    }

    private async Task<ApiResult> NewAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.NewProjectRequest);
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var defaults = TimelineEdits.DefaultSettings;
        var width = (int)Args.InRange(req.Width ?? defaults.Width, 16, 7680, "width");
        var height = (int)Args.InRange(req.Height ?? defaults.Height, 16, 4320, "height");
        if (width % 2 != 0 || height % 2 != 0)
        {
            throw ApiException.InvalidArgument(width % 2 != 0 ? "width" : "height", "The canvas width and height must be even.");
        }

        var rate = req.FrameRate is { } r ? Args.Rate(r, "frameRate") : defaults.Rate;
        CheckUnsaved(snap.Dirty, req.DiscardChanges);
        var outcome = await d.Edits.HostOperationAsync(ctx, () => ctx.Host.NewProjectAsync(new SequenceSettings(width, height, rate), ctx.Call.LockId!, ctx.Ct)).ConfigureAwait(false);
        if (outcome.Error is { } error)
        {
            throw new ApiException(error);
        }

        return await SummaryAsync(d, ctx, outcome.Warnings).ConfigureAwait(false);
    }

    private async Task<ApiResult> OpenAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.OpenProjectRequest);
        var path = Args.Required(req.Path, "path");
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var full = d.Paths.CheckRead(path, snap.Project, snap.ProjectPath);
        if (!File.Exists(full))
        {
            throw new ApiException(ErrorCode.NotFound, $"There is no project file at '{path}'.", new Dictionary<string, string> { ["path"] = path });
        }

        CheckUnsaved(snap.Dirty, req.DiscardChanges);
        var outcome = await d.Edits.HostOperationAsync(ctx, () => ctx.Host.OpenProjectAsync(full, ctx.Call.LockId!, ctx.Ct)).ConfigureAwait(false);
        if (outcome.Error is { } error)
        {
            throw new ApiException(error);
        }

        return await SummaryAsync(d, ctx, outcome.Warnings).ConfigureAwait(false);
    }

    private async Task<ApiResult> SaveAsync(ToolCallContext ctx)
    {
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        if (snap.ProjectPath is null)
        {
            throw ApiException.Conflict("untitled", "The project has no file yet. Use save-as with a path inside the write folders.");
        }

        return await SaveCoreAsync(ctx, null).ConfigureAwait(false);
    }

    private async Task<ApiResult> SaveAsAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.SaveAsRequest);
        var path = Args.Required(req.Path, "path");
        if (!path.EndsWith(".cutlass", StringComparison.OrdinalIgnoreCase))
        {
            path += ".cutlass";
        }

        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var full = d.Paths.CheckWrite(path, snap.ProjectPath);
        var isCurrent = snap.ProjectPath is not null && string.Equals(Path.GetFullPath(snap.ProjectPath), full, ctx.Host.Services.Paths.PathComparison);
        if (File.Exists(full) && !isCurrent && req.Overwrite != true)
        {
            throw ApiException.Conflict("exists", $"'{path}' already exists. Pass overwrite: true to replace it, or choose another name.");
        }

        return await SaveCoreAsync(ctx, full).ConfigureAwait(false);
    }

    private async Task<ApiResult> SaveCoreAsync(ToolCallContext ctx, string? path)
    {
        var outcome = await d.Edits.HostOperationAsync(ctx, () => ctx.Host.SaveProjectAsync(path, ctx.Call.LockId!, ctx.Ct)).ConfigureAwait(false);
        if (outcome.Error is { } error)
        {
            throw new ApiException(error);
        }

        return ctx.Ok(new PathDto { Path = (outcome.Path ?? path ?? string.Empty).Replace('\\', '/') }, AgentJsonContext.Relaxed.PathDto, outcome.Warnings);
    }

    private async Task<ApiResult> ShutdownAsync(ToolCallContext ctx)
    {
        if (d.ShutdownHandler is not { } handler)
        {
            throw new ApiException(ErrorCode.NotFound, "Shutdown exists only for a headless 'serve' process.");
        }

        var req = ctx.Read(AgentJsonContext.Relaxed.ShutdownRequest);
        await d.Edits.WithLockAsync(ctx, waitWhilePaused: false, () => true).ConfigureAwait(false);
        var save = req.Save == true;

        // Answer first; the process stops right after.
        _ = Task.Run(async () =>
        {
            await Task.Delay(200).ConfigureAwait(false);
            await handler(save).ConfigureAwait(false);
        });
        return ctx.Ok(EmptyDto.Instance, AgentJsonContext.Relaxed.EmptyDto);
    }
}
