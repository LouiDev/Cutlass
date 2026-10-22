using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Core.Editing;

namespace Cutlass.Agent.Tools;

/// <summary>Marker edits and In/Out.</summary>
internal sealed partial class EditTools
{
    private void RegisterMarkers()
    {
        d.Register("edit.marker.add", MarkerAddAsync);
        d.Register("edit.marker.update", MarkerUpdateAsync);
        d.Register("edit.marker.remove", MarkerRemoveAsync);
        d.Register("edit.in-out", InOutAsync);
    }

    private Task<ApiResult> MarkerAddAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.MarkerAddRequest);
        var frame = Args.Required(req.Frame, "frame");
        var color = req.Color is { } c ? Args.Color(c, "color", withAlpha: false) : TimelineEdits.DefaultMarkerColor;
        return d.Edits.EditAsync(ctx, req, p => TimelineEdits.AddMarker(Ids, p, frame, req.Name ?? string.Empty, color));
    }

    private Task<ApiResult> MarkerUpdateAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.MarkerUpdateRequest);
        var id = Args.Required(req.MarkerId, "markerId");
        var color = req.Color is { } c ? Args.Color(c, "color", withAlpha: false) : null;
        return d.Edits.EditAsync(ctx, req, p =>
        {
            _ = p.Sequence.Markers.FirstOrDefault(m => m.Id == id) ?? throw ApiException.NotFound("marker", id);
            var result = TimelineEdits.UpdateMarker(p, id, m => m with
            {
                Frame = req.Frame ?? m.Frame,
                Name = req.Name ?? m.Name,
                Color = color ?? m.Color,
            });
            return req.Name is null && color is null && req.Frame is not null ? result with { Label = "Move Marker" } : result;
        });
    }

    private Task<ApiResult> MarkerRemoveAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.MarkerIdsRequest);
        var ids = Args.Ids(req.MarkerIds, "markerIds");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            foreach (var id in ids.Where(id => p.Sequence.Markers.All(m => m.Id != id)))
            {
                throw ApiException.NotFound("marker", id);
            }

            return TimelineEdits.RemoveMarkers(p, ids);
        });
    }

    private static long? Point(JsonElement value, long? current, string field) => value switch
    {
        { ValueKind: JsonValueKind.Undefined } => current,
        { ValueKind: JsonValueKind.Null } => null,
        { ValueKind: JsonValueKind.Number } v when v.TryGetInt64(out var n) => n,
        { ValueKind: JsonValueKind.String } v when long.TryParse(v.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) => n,
        _ => throw ApiException.InvalidArgument(field, $"'{field}' is a frame number or null."),
    };

    private Task<ApiResult> InOutAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.InOutRequest);
        return d.Edits.EditAsync(ctx, req, p =>
        {
            var inFrame = Point(req.InFrame, p.Sequence.InFrame, "inFrame");
            var outFrame = Point(req.OutFrame, p.Sequence.OutFrame, "outFrame");
            return TimelineEdits.SetInOut(p, inFrame, outFrame);
        });
    }
}
