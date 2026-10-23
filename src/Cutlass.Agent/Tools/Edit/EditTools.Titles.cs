using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto.Project;
using Cutlass.Agent.Tools.Dispatch;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Media.Platform;

namespace Cutlass.Agent.Tools.Edit;

/// <summary>Title edits: add and update.</summary>
internal sealed partial class EditTools
{
    private void RegisterTitles()
    {
        d.Register("edit.title.add", TitleAddAsync);
        d.Register("edit.title.update", TitleUpdateAsync);
    }

    private TitleSpec ApplyTitle(TitleSpec spec, TitleDto? patch, Project project, string? projectPath)
    {
        if (patch is null)
        {
            return spec;
        }

        var font = patch.Font;
        if (font is not null && !font.StartsWith(FontLocator.BuiltinPrefix, StringComparison.Ordinal) && Path.IsPathFullyQualified(font))
        {
            // A font file must lie inside the read folders (4.4); a family name is resolved by FontLocator.
            font = d.Paths.CheckRead(font, project, projectPath);
        }

        return spec with
        {
            Text = patch.Text ?? spec.Text,
            Font = font ?? spec.Font,
            Size = patch.Size ?? spec.Size,
            Color = patch.Color is { } c ? Args.Color(c, "title.color", withAlpha: true) : spec.Color,
            OutlineWidth = patch.OutlineWidth ?? spec.OutlineWidth,
            OutlineColor = patch.OutlineColor is { } oc ? Args.Color(oc, "title.outlineColor", withAlpha: true) : spec.OutlineColor,
            Box = patch.Box ?? spec.Box,
            BoxColor = patch.BoxColor is { } bc ? Args.Color(bc, "title.boxColor", withAlpha: true) : spec.BoxColor,
            BoxPadding = patch.BoxPadding ?? spec.BoxPadding,
            Anchor = EnumWire.Parse(patch.Anchor, "title.anchor", spec.Anchor),
            OffsetX = patch.OffsetX ?? spec.OffsetX,
            OffsetY = patch.OffsetY ?? spec.OffsetY,
        };
    }

    private IEnumerable<string> DrawtextWarning() =>
        d.Host.Services.Capabilities is { HasDrawtext: false }
            ? ["This ffmpeg has no drawtext filter, so titles will not render in preview or export."]
            : [];

    private Task<ApiResult> TitleAddAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.TitleAddRequest);
        var trackId = Args.Required(req.TrackId, "trackId");
        var at = Args.Required(req.At, "at");
        var duration = Args.Required(req.DurationFrames, "durationFrames");
        if (at < 0 || duration < 1)
        {
            throw ApiException.InvalidArgument(at < 0 ? "at" : "durationFrames", "'at' must not be negative and 'durationFrames' must be at least 1.");
        }

        if (string.IsNullOrEmpty(req.Title?.Text))
        {
            throw ApiException.InvalidArgument("title.text", "'title.text' is required.");
        }

        var mode = Mode(req.Mode);
        return d.Edits.EditAsync(ctx, req, p =>
        {
            var track = RequireTrack(p, trackId);
            if (track.Kind != TrackKind.Video)
            {
                throw ApiException.InvalidArgument("trackId", "Titles go on video tracks.");
            }

            var spec = ApplyTitle(TitleSpec.Default, req.Title, p, ctx.Host.ProjectPath);
            var clip = new TitleClip("c_title", 0, duration, true, spec, req.Opacity ?? 1, req.FadeInFrames ?? 0, req.FadeOutFrames ?? 0);
            var result = TimelineEdits.Place(Ids, p, [new ClipPlacement(trackId, clip)], at, mode);
            return Warn(result with { Label = "Add Title" }, DrawtextWarning());
        });
    }

    private Task<ApiResult> TitleUpdateAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.TitleUpdateRequest);
        var id = Args.Required(req.ClipId, "clipId");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            if (RequireClip(p, id) is not TitleClip title)
            {
                throw ApiException.InvalidArgument("clipId", $"'{id}' is not a title clip.");
            }

            var spec = ApplyTitle(title.Title, req.Title, p, ctx.Host.ProjectPath);
            var result = TimelineEdits.UpdateClips(
                p,
                [id],
                c => ((TitleClip)c) with
                {
                    Title = spec,
                    Opacity = req.Opacity ?? title.Opacity,
                    FadeInFrames = req.FadeInFrames ?? title.FadeInFrames,
                    FadeOutFrames = req.FadeOutFrames ?? title.FadeOutFrames,
                },
                "Edit Title");
            return Warn(result, DrawtextWarning());
        });
    }
}
