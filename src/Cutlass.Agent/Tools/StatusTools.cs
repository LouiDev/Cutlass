using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;

namespace Cutlass.Agent.Tools;

/// <summary><c>status</c> and <c>capabilities</c> (AGENT_INTEGRATION 4.4).</summary>
internal sealed class StatusTools(ToolDispatcher d)
{
    public void Register()
    {
        d.Register("status", StatusAsync);
        d.Register("capabilities", CapabilitiesAsync);
    }

    private async Task<ApiResult> StatusAsync(ToolCallContext ctx)
    {
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var services = ctx.Host.Services;
        var dto = new StatusDto
        {
            Version = d.AppVersion,
            HostKind = ctx.Host.Kind,
            Ffmpeg = new FfmpegStatusDto { Ready = services.IsMediaReady, Version = services.FfmpegVersion, Reason = services.NotReadyReason },
            Project = new ProjectStatusDto
            {
                Path = snap.ProjectPath?.Replace('\\', '/'),
                Name = ProjectSummaryMapper.NameOf(snap.ProjectPath),
                Dirty = snap.Dirty,
                HasProject = true,
                OfflineMediaIds = [.. snap.Project.Media.Where(m => !d.IsOnline(m.Id)).Select(m => m.Id)],
            },
            Lock = LockTools.ToDto(snap.Lock, snap.LockMine),
            Playhead = snap.Playhead,
            Batch = snap.BatchLabel is null ? null : EditRunner.Strip(snap.BatchLabel),
            Revision = snap.Revision,
        };
        return ApiResult.Ok(snap.Revision, dto, AgentJsonContext.Relaxed.StatusDto);
    }

    private Task<ApiResult> CapabilitiesAsync(ToolCallContext ctx)
    {
        var caps = ctx.Host.Services.Capabilities;
        var dto = new CapabilitiesDto
        {
            Presets =
            [
                .. ExportPresets.All.Select(p =>
                {
                    var reason = caps is null ? ctx.Host.Services.NotReadyReason ?? "ffmpeg is not ready." : p.UnavailableReason(caps);
                    return new PresetDto { Id = p.Id, Name = p.DisplayName, Kind = EnumWire.Name(p.Kind), Available = reason is null, Reason = reason };
                }),
            ],
            Qualities = EnumWire.Names<ExportQuality>(),
            TransitionKinds = EnumWire.Names<TransitionKind>(),
            TitleAnchors = EnumWire.Names<TitleAnchor>(),
            EditModes = EnumWire.Names<Core.Editing.EditMode>(),
            Titles = caps?.HasDrawtext ?? false,
        };
        return Task.FromResult(ctx.Ok(dto, AgentJsonContext.Relaxed.CapabilitiesDto));
    }
}
