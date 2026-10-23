using Cutlass.Agent.Api;
using Cutlass.Agent.Tools.Dispatch;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.Agent.Tools.Edit;

/// <summary>Transition edits: add, update, remove.</summary>
internal sealed partial class EditTools
{
    private void RegisterTransitions()
    {
        d.Register("edit.transition.add", TransitionAddAsync);
        d.Register("edit.transition.update", TransitionUpdateAsync);
        d.Register("edit.transition.remove", TransitionRemoveAsync);
    }

    private static TransitionKind KindFor(Track track, string? text)
    {
        var fallback = track.Kind == TrackKind.Audio ? TransitionKind.AudioCrossfade : TransitionKind.Crossfade;
        var kind = EnumWire.Parse(text, "kind", fallback);
        if (track.Kind == TrackKind.Audio && kind != TransitionKind.AudioCrossfade)
        {
            // Unspecified in the spec: an audio track only takes the audio crossfade, so any kind becomes that.
            return TransitionKind.AudioCrossfade;
        }

        if (track.Kind == TrackKind.Video && kind == TransitionKind.AudioCrossfade)
        {
            throw ApiException.InvalidArgument("kind", "audioCrossfade is for audio tracks; pick a video transition kind.");
        }

        return kind;
    }

    private Task<ApiResult> TransitionAddAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.TransitionAddRequest);
        var trackId = Args.Required(req.TrackId, "trackId");
        var cut = Args.Required(req.CutFrame, "cutFrame");
        var duration = Args.Required(req.DurationFrames, "durationFrames");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            var track = RequireTrack(p, trackId);
            return TimelineEdits.AddTransition(Ids, p, trackId, cut, KindFor(track, req.Kind), duration);
        });
    }

    private Task<ApiResult> TransitionUpdateAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.TransitionUpdateRequest);
        var id = Args.Required(req.TransitionId, "transitionId");
        if (req.Kind is null && req.DurationFrames is null)
        {
            throw ApiException.InvalidArgument("kind", "Give 'kind', 'durationFrames' or both.");
        }

        return d.Edits.EditAsync(ctx, req, p =>
        {
            var (track, _) = TimelineQuery.FindTransition(p, id) ?? throw ApiException.NotFound("transition", id);
            var current = p;
            var warnings = new List<string>();
            var label = "Transition";
            if (req.Kind is not null)
            {
                var r = TimelineEdits.SetTransitionKind(current, [id], KindFor(track, req.Kind));
                warnings.AddRange(r.Warnings);
                current = r.Project;
                label = r.Label;
            }

            if (req.DurationFrames is { } duration)
            {
                var r = TimelineEdits.SetTransitionDuration(current, id, duration);
                warnings.AddRange(r.Warnings);
                current = r.Project;
                label = req.Kind is null ? r.Label : "Transition";
            }

            return ReferenceEquals(current, p)
                ? EditResult.Unchanged(p, label, [.. warnings])
                : new EditResult(current, label, [.. warnings], [id]);
        });
    }

    private Task<ApiResult> TransitionRemoveAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.TransitionIdsRequest);
        var ids = Args.Ids(req.TransitionIds, "transitionIds");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            foreach (var id in ids)
            {
                _ = TimelineQuery.FindTransition(p, id) ?? throw ApiException.NotFound("transition", id);
            }

            return TimelineEdits.RemoveTransitions(p, ids);
        });
    }
}
