using System.Collections.Immutable;
using System.Globalization;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Requests.Clips;
using Cutlass.Agent.Tools.Dispatch;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Agent.Tools.Edit;

/// <summary>Clip edits: place, move, trim, roll, slip, split, delete, remove ranges, close gap, properties, speed, link, volume keys.</summary>
internal sealed partial class EditTools
{
    /// <summary>The most ranges per remove-ranges call.</summary>
    public const int MaxRanges = 500;

    private void RegisterClips()
    {
        d.Register("edit.place", PlaceAsync);
        d.Register("edit.move", MoveAsync);
        d.Register("edit.trim", TrimAsync);
        d.Register("edit.roll", RollAsync);
        d.Register("edit.slip", SlipAsync);
        d.Register("edit.split", SplitAsync);
        d.Register("edit.delete", DeleteAsync);
        d.Register("edit.remove-ranges", RemoveRangesAsync);
        d.Register("edit.close-gap", CloseGapAsync);
        d.Register("edit.clip-props", ClipPropsAsync);
        d.Register("edit.speed", SpeedAsync);
        d.Register("edit.freeze", FreezeAsync);
        d.Register("edit.link", LinkAsync);
        d.Register("edit.unlink", UnlinkAsync);
        d.Register("edit.volume-keys", VolumeKeysAsync);
    }

    private Task<ApiResult> PlaceAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.PlaceRequest);
        var mediaId = Args.Required(req.MediaId, "mediaId");
        var at = Args.Required(req.At, "at");
        if (at < 0)
        {
            throw ApiException.InvalidArgument("at", "'at' must not be negative.");
        }

        var mode = Mode(req.Mode);
        var streams = (req.Streams ?? "both").ToLowerInvariant() switch
        {
            "both" => (Video: true, Audio: true),
            "video" => (Video: true, Audio: false),
            "audio" => (Video: false, Audio: true),
            _ => throw ApiException.InvalidArgument("streams", "'streams' is both, video or audio."),
        };
        var sourceIn = Args.Flicks(req.SourceInFlicks, req.SourceInSeconds, "sourceInFlicks");
        var sourceOut = Args.Flicks(req.SourceOutFlicks, req.SourceOutSeconds, "sourceOutFlicks");
        if (req.DurationFrames is { } df && df < 1)
        {
            throw ApiException.InvalidArgument("durationFrames", "'durationFrames' must be at least 1.");
        }

        var stillSeconds = d.Host.Services.Settings.DefaultStillSeconds;
        return d.Edits.EditAsync(ctx, req, p =>
        {
            var media = TimelineQuery.FindMedia(p, mediaId) ?? throw ApiException.NotFound("media", mediaId);
            string? videoTrack = null;
            string? audioTrack = null;
            if (streams.Video && media.Info.Video is not null)
            {
                videoTrack = req.VideoTrackId is { } v
                    ? KindTrack(p, v, TrackKind.Video, "videoTrackId")
                    : TimelineQuery.TracksOfKind(p, TrackKind.Video).LastOrDefault(t => t.Targeted && !t.Locked)?.Id;
            }

            if (streams.Audio && media.Info.Audio is not null)
            {
                audioTrack = req.AudioTrackId is { } a
                    ? KindTrack(p, a, TrackKind.Audio, "audioTrackId")
                    : TimelineQuery.TracksOfKind(p, TrackKind.Audio).FirstOrDefault(t => t.Targeted && !t.Locked)?.Id;
            }

            if (videoTrack is null && audioTrack is null)
            {
                return EditResult.Unchanged(p, mode == EditMode.Insert ? "Insert" : "Overwrite",
                    "No track can take this media: target an unlocked track of the right kind or pass videoTrackId or audioTrackId.");
            }

            var rate = p.Sequence.Settings.Rate;
            var stillFrames = Math.Max(1, (long)Math.Round(stillSeconds * rate.Value.ToDouble()));
            var source = Math.Max(0, sourceIn ?? 0);
            var duration = req.DurationFrames ?? TimelineEdits.SourceRangeFrames(p, mediaId, source, sourceOut, stillFrames);
            if (duration < 1)
            {
                return EditResult.Unchanged(p, mode == EditMode.Insert ? "Insert" : "Overwrite", "The source range is empty.");
            }

            var placements = TimelineEdits.SourcePlacements(p, mediaId, source, duration, videoTrack, audioTrack);
            return TimelineEdits.Place(Ids, p, placements, at, mode);
        });
    }

    private static string KindTrack(Project p, string id, TrackKind kind, string field)
    {
        var track = RequireTrack(p, id);
        return track.Kind == kind
            ? track.Id
            : throw ApiException.InvalidArgument(field, $"'{id}' is {(track.Kind == TrackKind.Video ? "a video" : "an audio")} track.");
    }

    private Task<ApiResult> MoveAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.MoveRequest);
        var ids = Args.Ids(req.ClipIds, "clipIds");
        var delta = Args.Required(req.DeltaFrames, "deltaFrames");
        var mode = Mode(req.Mode);
        return d.Edits.EditAsync(ctx, req, p =>
        {
            RequireClips(p, ids);
            return TimelineEdits.Move(Ids, p, WithLinked(p, ids, req.IncludeLinked), delta, req.DeltaTracks ?? 0, mode);
        });
    }

    private Task<ApiResult> TrimAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.TrimRequest);
        var id = Args.Required(req.ClipId, "clipId");
        var edge = EnumWire.Parse<ClipEdge>(Args.Required(req.Edge, "edge"), "edge", ClipEdge.Start);
        if ((req.DeltaFrames is null) == (req.ToFrame is null))
        {
            throw ApiException.InvalidArgument("deltaFrames", "Give exactly one of 'deltaFrames' and 'toFrame'.");
        }

        return d.Edits.EditAsync(ctx, req, p =>
        {
            var clip = RequireClip(p, id);
            var delta = req.DeltaFrames ?? (req.ToFrame!.Value - (edge == ClipEdge.Start ? clip.Start : clip.End));
            return TimelineEdits.Trim(p, id, edge, delta, req.Ripple ?? false, req.IncludeLinked ?? true);
        });
    }

    private Task<ApiResult> RollAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.RollRequest);
        var left = Args.Required(req.LeftClipId, "leftClipId");
        var right = Args.Required(req.RightClipId, "rightClipId");
        var delta = Args.Required(req.DeltaFrames, "deltaFrames");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            RequireClips(p, [left, right]);
            return TimelineEdits.Roll(p, left, right, delta, req.IncludeLinked ?? true);
        });
    }

    private Task<ApiResult> SlipAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.SlipRequest);
        var id = Args.Required(req.ClipId, "clipId");
        var delta = Args.Required(req.DeltaFrames, "deltaFrames");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            RequireClip(p, id);
            return TimelineEdits.Slip(p, id, delta, req.IncludeLinked ?? true);
        });
    }

    private Task<ApiResult> SplitAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.SplitRequest);
        var frame = Args.Required(req.Frame, "frame");
        var trackIds = Args.OptionalIds(req.TrackIds, "trackIds");
        var clipIds = Args.OptionalIds(req.ClipIds, "clipIds");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            foreach (var t in trackIds ?? [])
            {
                RequireTrack(p, t);
            }

            IReadOnlyCollection<string>? only = null;
            if (clipIds is { Count: > 0 })
            {
                RequireClips(p, clipIds);
                only = WithLinked(p, clipIds, req.IncludeLinked);
            }

            IReadOnlyCollection<string> tracks = trackIds is { Count: > 0 }
                ? trackIds
                : [.. p.Sequence.Tracks.Where(t => !t.Locked).Select(t => t.Id)];
            return TimelineEdits.Split(Ids, p, tracks, frame, only);
        });
    }

    private Task<ApiResult> DeleteAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.DeleteRequest);
        var ids = Args.Ids(req.ClipIds, "clipIds");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            RequireClips(p, ids);
            var all = WithLinked(p, ids, req.IncludeLinked);
            return req.Ripple == true ? TimelineEdits.RippleDelete(p, all) : TimelineEdits.Lift(p, all);
        });
    }

    private Task<ApiResult> RemoveRangesAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.RemoveRangesRequest);
        if (req.Ranges is not { Count: > 0 } list)
        {
            throw ApiException.InvalidArgument("ranges", "'ranges' needs at least one range.");
        }

        if (list.Count > MaxRanges)
        {
            throw new ApiException(ErrorCode.PayloadTooLarge, $"At most {MaxRanges} ranges per call; split the list.", new Dictionary<string, string> { ["field"] = "ranges" });
        }

        var ranges = new List<(long Start, long End)>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            var field = string.Create(CultureInfo.InvariantCulture, $"ranges[{i}]");
            var start = Args.Required(list[i].Start, field + ".start");
            var end = Args.Required(list[i].End, field + ".end");
            if (start < 0 || end <= start)
            {
                throw ApiException.InvalidArgument(field, string.Create(CultureInfo.InvariantCulture, $"Range {i} [{start}, {end}) is empty or negative."));
            }

            ranges.Add((start, end));
        }

        var trackIds = Args.OptionalIds(req.TrackIds, "trackIds");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            foreach (var t in trackIds ?? [])
            {
                RequireTrack(p, t);
            }

            return TimelineEdits.RemoveRanges(Ids, p, ranges, trackIds, req.Ripple ?? true);
        });
    }

    private Task<ApiResult> CloseGapAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.CloseGapRequest);
        var trackId = Args.Required(req.TrackId, "trackId");
        var gap = Args.Required(req.GapStart, "gapStart");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            RequireTrack(p, trackId);
            return TimelineEdits.CloseGap(p, trackId, gap);
        });
    }

    private Task<ApiResult> ClipPropsAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.ClipPropsRequest);
        var ids = Args.Ids(req.ClipIds, "clipIds");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            RequireClips(p, ids);
            var warnings = new SortedSet<string>(StringComparer.Ordinal);
            var result = TimelineEdits.UpdateClips(p, ids, c => Apply(p, c, req, warnings), "Clip Properties");
            return Warn(result, warnings);
        });
    }

    private static Clip Apply(Project project, Clip clip, ClipPropsRequest req, ISet<string> warnings)
    {
        var enabled = req.Enabled ?? clip.Enabled;
        switch (clip)
        {
            case MediaClip { Stream: StreamKind.Video } m:
                if (req.GainDb is not null || req.AudioEffects is not null)
                {
                    warnings.Add("gainDb and audioEffects apply to audio clips only; they were ignored for video clips.");
                }

                var freeze = req.Freeze ?? m.Freeze;
                if (req.Freeze is not null && TimelineQuery.FindMedia(project, m.MediaId)?.Info.IsStill == true)
                {
                    // A still never gets the flag: it would only show a misleading hold badge.
                    warnings.Add("A still image is already a single frame.");
                    freeze = m.Freeze;
                }

                return m with
                {
                    Enabled = enabled,
                    Freeze = freeze,
                    Video = MergeVideo(m.Video, req),
                };
            case MediaClip m:
                if (req.Opacity is not null || req.Transform is not null || req.Color is not null || req.Effects is not null || req.Freeze is not null)
                {
                    warnings.Add("opacity, transform, color, effects and freeze apply to video clips only; they were ignored for audio clips.");
                }

                return m with
                {
                    Enabled = enabled,
                    Audio = MergeAudio(m.Audio, req),
                };
            case TitleClip title:
                if (req.GainDb is not null || req.AudioEffects is not null || req.Transform is not null || req.Color is not null || req.Effects is not null || req.Freeze is not null)
                {
                    warnings.Add("Titles take opacity, fades and enabled only; use update_title for their style and position.");
                }

                return title with
                {
                    Enabled = enabled,
                    Opacity = req.Opacity ?? title.Opacity,
                    FadeInFrames = req.FadeInFrames ?? title.FadeInFrames,
                    FadeOutFrames = req.FadeOutFrames ?? title.FadeOutFrames,
                };
            case GeneratorClip g:
                return ApplyGenerator(g, enabled, req, warnings);
            default:
                return clip;
        }
    }

    // The partial merge of the video side of edit.clip-props, shared by video media clips and video generators.
    private static VideoProps MergeVideo(VideoProps v, ClipPropsRequest req)
    {
        var t = v.Transform;
        var tr = req.Transform;
        var co = v.Color;
        var cr = req.Color;
        var fo = v.Effects;
        var fr = req.Effects;
        return v with
        {
            Opacity = req.Opacity ?? v.Opacity,
            Transform = tr is null ? t : new Transform(tr.X ?? t.X, tr.Y ?? t.Y, tr.Scale ?? t.Scale, tr.CropLeft ?? t.CropLeft, tr.CropTop ?? t.CropTop, tr.CropRight ?? t.CropRight, tr.CropBottom ?? t.CropBottom),
            Color = cr is null ? co : new ColorAdjust(cr.Brightness ?? co.Brightness, cr.Contrast ?? co.Contrast, cr.Saturation ?? co.Saturation, cr.Gamma ?? co.Gamma),
            Effects = fr is null ? fo : new VideoEffects(
                fr.FlipH ?? fo.FlipH,
                fr.FlipV ?? fo.FlipV,
                fr.RotationDegrees ?? fo.RotationDegrees,
                fr.HueDegrees ?? fo.HueDegrees,
                fr.TemperatureKelvin ?? fo.TemperatureKelvin,
                fr.Tint ?? fo.Tint,
                fr.Blur ?? fo.Blur,
                fr.Sharpen ?? fo.Sharpen,
                fr.Vignette ?? fo.Vignette,
                fr.Grayscale ?? fo.Grayscale,
                fr.KeyEnabled ?? fo.KeyEnabled,
                fr.KeyColor ?? fo.KeyColor,
                fr.KeySimilarity ?? fo.KeySimilarity,
                fr.KeyBlend ?? fo.KeyBlend),
            FadeInFrames = req.FadeInFrames ?? v.FadeInFrames,
            FadeOutFrames = req.FadeOutFrames ?? v.FadeOutFrames,
        };
    }

    // The partial merge of the audio side of edit.clip-props, shared by audio media clips and tones.
    private static AudioProps MergeAudio(AudioProps a, ClipPropsRequest req)
    {
        var ao = a.Effects;
        var ar = req.AudioEffects;
        return a with
        {
            GainDb = req.GainDb ?? a.GainDb,
            FadeInFrames = req.FadeInFrames ?? a.FadeInFrames,
            FadeOutFrames = req.FadeOutFrames ?? a.FadeOutFrames,
            Effects = ar is null ? ao : new AudioEffects(
                ar.HighPassHz ?? ao.HighPassHz,
                ar.LowPassHz ?? ao.LowPassHz,
                ar.BassDb ?? ao.BassDb,
                ar.TrebleDb ?? ao.TrebleDb,
                ar.Compressor ?? ao.Compressor,
                ar.CompressorThresholdDb ?? ao.CompressorThresholdDb,
                ar.CompressorRatio ?? ao.CompressorRatio,
                ar.Gate ?? ao.Gate,
                ar.GateThresholdDb ?? ao.GateThresholdDb,
                ar.Pan ?? ao.Pan,
                ar.Mono ?? ao.Mono),
        };
    }

    private Task<ApiResult> SpeedAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.SpeedRequest);
        var ids = Args.Ids(req.ClipIds, "clipIds");
        var percent = (int)Args.InRange(Args.Required(req.Percent, "percent"), TimelineEdits.MinSpeedPercent, TimelineEdits.MaxSpeedPercent, "percent");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            RequireClips(p, ids);
            return TimelineEdits.SetSpeed(p, ids, percent, req.RippleFollowing ?? true);
        });
    }

    private Task<ApiResult> FreezeAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.FreezeRequest);
        var id = Args.Required(req.ClipId, "clipId");
        var frame = Args.Required(req.Frame, "frame");
        var duration = Args.Required(req.DurationFrames, "durationFrames");
        if (frame < 0 || duration < 1)
        {
            throw ApiException.InvalidArgument(frame < 0 ? "frame" : "durationFrames", "'frame' must not be negative and 'durationFrames' must be at least 1.");
        }

        return d.Edits.EditAsync(ctx, req, p =>
        {
            if (RequireClip(p, id) is not MediaClip { Stream: StreamKind.Video })
            {
                throw ApiException.InvalidArgument("clipId", $"'{id}' is not a video clip.");
            }

            return TimelineEdits.FreezeFrame(Ids, p, id, frame, duration, req.RippleFollowing ?? true);
        });
    }

    private Task<ApiResult> LinkAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.ClipIdsRequest);
        var ids = Args.Ids(req.ClipIds, "clipIds");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            RequireClips(p, ids);
            return TimelineEdits.Link(Ids, p, ids);
        });
    }

    private Task<ApiResult> UnlinkAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.ClipIdsRequest);
        var ids = Args.Ids(req.ClipIds, "clipIds");
        return d.Edits.EditAsync(ctx, req, p =>
        {
            RequireClips(p, ids);
            return TimelineEdits.Unlink(p, ids);
        });
    }

    private Task<ApiResult> VolumeKeysAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.VolumeKeysRequest);
        var id = Args.Required(req.ClipId, "clipId");
        var keys = req.Keys ?? [];
        if (keys.Count > Args.MaxIds)
        {
            throw new ApiException(ErrorCode.PayloadTooLarge, "At most 1000 keys per clip.", new Dictionary<string, string> { ["field"] = "keys" });
        }

        var envelope = keys
            .Select(k => new VolumeKey(k.Frame, Math.Clamp(k.Db, TimelineEdits.MinGainDb, TimelineEdits.MaxGainDb)))
            .GroupBy(k => k.Frame)
            .Select(g => g.Last())
            .OrderBy(k => k.Frame)
            .ToImmutableArray();
        return d.Edits.EditAsync(ctx, req, p =>
        {
            // Audio media clips and tones (effects batch 5) carry audio props and so volume keys.
            var audio = RequireClip(p, id);
            if (ClipAudio.Of(audio) is null)
            {
                throw ApiException.InvalidArgument("clipId", $"'{id}' is not an audio clip or tone; volume keys belong to audio clips and tones.");
            }

            var warnings = new List<string>();
            if (envelope.Any(k => k.Frame < 0 || k.Frame >= audio.Duration))
            {
                warnings.Add(string.Create(CultureInfo.InvariantCulture, $"Keys outside the clip (0 to {audio.Duration - 1}) were left out."));
            }

            var result = TimelineEdits.UpdateClips(p, [id], c => ClipAudio.Of(c) is { } a ? ClipAudio.With(c, a with { Keys = envelope }) : c, "Volume Keys");
            return Warn(result, warnings);
        });
    }
}
