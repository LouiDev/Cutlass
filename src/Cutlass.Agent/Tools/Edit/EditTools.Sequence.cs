using Cutlass.Agent.Api;
using Cutlass.Agent.Tools.Dispatch;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.Agent.Tools.Edit;

/// <summary>The sequence edit: size and rate.</summary>
internal sealed partial class EditTools
{
    private void RegisterSequence() => d.Register("edit.sequence", SequenceAsync);

    private Task<ApiResult> SequenceAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.SequenceRequest);
        if (req.Width is null && req.Height is null && req.FrameRate is null)
        {
            throw ApiException.InvalidArgument("width", "Give 'width', 'height', 'frameRate' or several.");
        }

        var width = req.Width is { } w ? (int)Args.InRange(w, 16, 7680, "width") : (int?)null;
        var height = req.Height is { } h ? (int)Args.InRange(h, 16, 4320, "height") : (int?)null;
        var rate = req.FrameRate is { } r ? Args.Rate(r, "frameRate") : (Core.Time.FrameRate?)null;
        return d.Edits.EditAsync(ctx, req, p =>
        {
            var s = p.Sequence.Settings;
            return TimelineEdits.SetSequenceSettings(p, new SequenceSettings(width ?? s.Width, height ?? s.Height, rate ?? s.Rate));
        });
    }
}
