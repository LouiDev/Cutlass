using System.Globalization;
using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto;
using Cutlass.Agent.Imaging;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Preview;

namespace Cutlass.Agent.Tools;

/// <summary>Frames and contact sheets through <see cref="StillRenderer"/>, and the playhead (AGENT_INTEGRATION 4.4).</summary>
internal sealed class PreviewTools(ToolDispatcher d)
{
    /// <summary>The widest render.</summary>
    public const int MaxWidth = 1920;

    /// <summary>The most contact sheet cells.</summary>
    public const int MaxCells = 64;

    /// <summary>The largest contact sheet in pixels.</summary>
    public const long MaxSheetPixels = 16_000_000;

    public void Register()
    {
        d.Register("frame.render", RenderAsync);
        d.Register("contact-sheet", ContactSheetAsync);
        d.Register("playhead.set", PlayheadAsync);
    }

    /// <summary>The render size for a requested width: even, 16 to 1920, the sequence aspect, never above the canvas.</summary>
    internal static PreviewSize SizeFor(SequenceSettings settings, int width, string field)
    {
        var w = (int)Args.InRange(width, 16, MaxWidth, field) / 2 * 2;
        return FrameServer.FitSize(settings, w, int.MaxValue / 4);
    }

    private static bool Base64(string? format, string field) => format?.ToLowerInvariant() switch
    {
        null or "png" => false,
        "base64" => true,
        _ => throw ApiException.InvalidArgument(field, "'format' is png or base64."),
    };

    private static async Task<VideoFrame> RenderOneAsync(ToolCallContext ctx, StillRenderer stills, Project project, long frame, PreviewSize size, bool proxies)
    {
        try
        {
            return await stills.RenderAsync(project, frame, size, proxies, ctx.Ct).ConfigureAwait(false);
        }
        catch (FfmpegException ex)
        {
            throw new ApiException(ErrorCode.Unavailable, "ffmpeg could not render the frame: " + PreviewRenderMessage(ex));
        }
        catch (InvalidOperationException ex)
        {
            throw new ApiException(ErrorCode.Unavailable, ex.Message);
        }
    }

    private static string PreviewRenderMessage(FfmpegException ex) =>
        ex.StderrTail.Count > 0 ? ex.Message + " " + ex.StderrTail[^1].Trim() : ex.Message;

    private async Task<ApiResult> RenderAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.RenderFrameRequest);
        var frame = Args.Required(req.Frame, "frame");
        if (frame < 0)
        {
            throw ApiException.InvalidArgument("frame", "'frame' must not be negative.");
        }

        var base64 = Base64(req.Format, "format");
        var stills = ctx.Host.Services.Stills ?? throw MediaTools.Unavailable(ctx);
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var size = SizeFor(snap.Project.Sequence.Settings, req.Width ?? 640, "width");
        var image = await RenderOneAsync(ctx, stills, snap.Project, frame, size, req.UseProxies ?? false).ConfigureAwait(false);
        byte[] png;
        try
        {
            png = PngEncoder.EncodeBgra(image.Buffer, image.Width, image.Height, image.Stride);
        }
        finally
        {
            image.Return();
        }

        if (snap.LockMine && !snap.User.IsPlaying)
        {
            // Follow (6.2): the viewer shows what the agent looks at.
            ctx.Host.Ui.Post(() => ctx.Host.OnAgentFocus(frame));
        }

        var dto = new FrameDto
        {
            Width = size.Width,
            Height = size.Height,
            Frame = frame,
            Timecode = Timecode.Format(frame, snap.Project.Sequence.Settings.Rate),
            PngBase64 = base64 ? Convert.ToBase64String(png) : null,
        };
        var json = JsonSerializer.SerializeToElement(dto, AgentJsonContext.Relaxed.FrameDto);
        return new ApiResult { Revision = snap.Revision, Result = json, Png = base64 ? null : png };
    }

    private async Task<ApiResult> ContactSheetAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.ContactSheetRequest);
        var base64 = Base64(req.Format, "format");
        var stills = ctx.Host.Services.Stills ?? throw MediaTools.Unavailable(ctx);
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        var frames = FramesOf(req);
        var settings = snap.Project.Sequence.Settings;
        var size = SizeFor(settings, req.CellWidth ?? 320, "cellWidth");
        var columns = (int)Args.InRange(req.Columns ?? ContactSheetComposer.DefaultColumns(frames.Count), 1, MaxCells, "columns");
        var composer = new ContactSheetComposer(size.Width, size.Height, columns, frames.Count);
        if ((long)composer.Width * composer.Height > MaxSheetPixels)
        {
            throw new ApiException(ErrorCode.PayloadTooLarge, "The contact sheet would exceed 16 megapixels; use fewer cells or a smaller cellWidth.",
                new Dictionary<string, string> { ["field"] = "cellWidth" });
        }

        // Two at a time, the renderer's own limit.
        for (var i = 0; i < frames.Count; i += 2)
        {
            var batch = frames.Skip(i).Take(2).Select(f => RenderOneAsync(ctx, stills, snap.Project, f, size, false)).ToList();
            var images = await Task.WhenAll(batch).ConfigureAwait(false);
            for (var k = 0; k < images.Length; k++)
            {
                try
                {
                    composer.Draw(i + k, images[k].Buffer, images[k].Stride);
                }
                finally
                {
                    images[k].Return();
                }
            }
        }

        var png = composer.ToPng();
        var dto = new ContactSheetDto
        {
            Width = composer.Width,
            Height = composer.Height,
            CellWidth = size.Width,
            CellHeight = size.Height,
            Columns = composer.Columns,
            Layout =
            [
                .. frames.Select((f, i) =>
                {
                    var (x, y) = composer.CellOrigin(i);
                    return new ContactCellDto { Frame = f, Timecode = Timecode.Format(f, settings.Rate), X = x, Y = y };
                }),
            ],
            PngBase64 = base64 ? Convert.ToBase64String(png) : null,
        };
        var json = JsonSerializer.SerializeToElement(dto, AgentJsonContext.Relaxed.ContactSheetDto);
        var layout = JsonSerializer.Serialize(dto.Layout, AgentJsonContext.Default.ListContactCellDto); // a header value: the default encoder keeps it ASCII
        return new ApiResult
        {
            Revision = snap.Revision,
            Result = json,
            Png = base64 ? null : png,
            Headers = base64 ? null : new Dictionary<string, string> { [ApiVersion.LayoutHeader] = layout },
        };
    }

    private static List<long> FramesOf(Api.Requests.ContactSheetRequest req)
    {
        if (req.Frames is { Count: > 0 } list)
        {
            if (list.Count > MaxCells)
            {
                throw new ApiException(ErrorCode.PayloadTooLarge, "At most 64 frames fit on a contact sheet.", new Dictionary<string, string> { ["field"] = "frames" });
            }

            if (list.Any(f => f < 0))
            {
                throw ApiException.InvalidArgument("frames", "Frames must not be negative.");
            }

            return list;
        }

        var start = Args.Required(req.Start, "start");
        var end = Args.Required(req.End, "end");
        var count = (int)Args.InRange(Args.Required(req.Count, "count"), 1, MaxCells, "count");
        if (start < 0 || end <= start)
        {
            throw ApiException.InvalidArgument("end", string.Create(CultureInfo.InvariantCulture, $"Give frames, or start < end ({start}, {end}) and count."));
        }

        // An even spread: cell k shows start + floor(k * (end - start) / count).
        return [.. Enumerable.Range(0, count).Select(k => start + ((end - start) * k / count)).Distinct()];
    }

    private async Task<ApiResult> PlayheadAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.PlayheadRequest);
        var frame = Args.Required(req.Frame, "frame");
        if (frame < 0)
        {
            throw ApiException.InvalidArgument("frame", "'frame' must not be negative.");
        }

        var host = ctx.Host;
        await d.Edits.WithLockAsync(ctx, waitWhilePaused: false, () =>
        {
            if (!host.UserState.IsPlaying)
            {
                host.Playhead = frame;
                host.OnAgentFocus(frame);
            }

            d.Events.Add(new EventDto { Kind = "playheadChanged", Frame = frame });
            return true;
        }).ConfigureAwait(false);
        return ctx.Ok(new PlayheadDto { Frame = frame }, AgentJsonContext.Relaxed.PlayheadDto);
    }
}
