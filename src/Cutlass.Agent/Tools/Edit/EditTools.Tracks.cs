using System.Globalization;
using Cutlass.Agent.Api;
using Cutlass.Agent.Tools.Dispatch;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.Agent.Tools.Edit;

/// <summary>Track edits: add, update, remove.</summary>
internal sealed partial class EditTools
{
    private void RegisterTracks()
    {
        d.Register("edit.track.add", TrackAddAsync);
        d.Register("edit.track.update", TrackUpdateAsync);
        d.Register("edit.track.remove", TrackRemoveAsync);
    }

    private Task<ApiResult> TrackAddAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.TrackAddRequest);
        var kind = EnumWire.Parse<TrackKind>(Args.Required(req.Kind, "kind"), "kind", TrackKind.Video);
        if (req.Index is < 1)
        {
            throw ApiException.InvalidArgument("index", "'index' starts at 1 (V1 or A1).");
        }

        return d.Edits.EditAsync(ctx, req, p => TimelineEdits.AddTrack(Ids, p, kind, req.Index is { } i ? i - 1 : null));
    }

    private Task<ApiResult> TrackUpdateAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.TrackUpdateRequest);
        var id = Args.Required(req.TrackId, "trackId");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            var track = RequireTrack(p, id);
            var warnings = new List<string>();
            if (track.Kind == TrackKind.Video && (req.Muted is not null || req.Solo is not null || req.GainDb is not null))
            {
                warnings.Add("muted, solo and gainDb apply to audio tracks only; they were ignored.");
            }

            if (track.Kind == TrackKind.Audio && req.Hidden is not null)
            {
                warnings.Add("hidden applies to video tracks only; it was ignored.");
            }

            var gain = req.GainDb;
            if (gain is { } g && (g < TimelineEdits.MinGainDb || g > TimelineEdits.MaxGainDb))
            {
                gain = Math.Clamp(g, TimelineEdits.MinGainDb, TimelineEdits.MaxGainDb);
                warnings.Add(string.Create(CultureInfo.InvariantCulture, $"Track gain is limited to {TimelineEdits.MinGainDb} to {TimelineEdits.MaxGainDb} dB; using {gain} dB."));
            }

            var audio = track.Kind == TrackKind.Audio;
            var result = TimelineEdits.SetTrack(
                p,
                id,
                t => t with
                {
                    Name = string.IsNullOrWhiteSpace(req.Name) ? t.Name : req.Name.Trim(),
                    Hidden = !audio && req.Hidden is { } h ? h : t.Hidden,
                    Locked = req.Locked ?? t.Locked,
                    Targeted = req.Targeted ?? t.Targeted,
                    Muted = audio && req.Muted is { } m ? m : t.Muted,
                    Solo = audio && req.Solo is { } s ? s : t.Solo,
                    GainDb = audio && gain is { } gg ? gg : t.GainDb,
                },
                "Track");
            return Warn(result, warnings);
        });
    }

    private Task<ApiResult> TrackRemoveAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.TrackIdRequest);
        var id = Args.Required(req.TrackId, "trackId");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            RequireTrack(p, id);
            return TimelineEdits.RemoveTrack(p, id);
        });
    }
}
