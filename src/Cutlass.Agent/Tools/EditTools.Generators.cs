using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Requests;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.Agent.Tools;

/// <summary>Generator edits (effects batch 5): add and update colour mattes, bars and tones.</summary>
internal sealed partial class EditTools
{
    private void RegisterGenerators()
    {
        d.Register("edit.generator.add", GeneratorAddAsync);
        d.Register("edit.generator.update", GeneratorUpdateAsync);
    }

    private Task<ApiResult> GeneratorAddAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.GeneratorAddRequest);
        var trackId = Args.Required(req.TrackId, "trackId");
        var at = Args.Required(req.At, "at");
        var duration = Args.Required(req.DurationFrames, "durationFrames");
        if (at < 0 || duration < 1)
        {
            throw ApiException.InvalidArgument(at < 0 ? "at" : "durationFrames", "'at' must not be negative and 'durationFrames' must be at least 1.");
        }

        var kind = EnumWire.Parse(Args.Required(req.Kind, "kind"), "kind", GeneratorKind.ColorMatte);
        var color = MatteColor(req.Color);
        var toneHz = ToneHz(req.ToneHz);
        var mode = Mode(req.Mode);
        return d.Edits.EditAsync(ctx, req, p =>
        {
            var track = RequireTrack(p, trackId);
            if (track.Kind != kind.TrackKindOf())
            {
                throw ApiException.InvalidArgument("trackId", $"{kind.DisplayName()} goes on {(kind.IsAudio() ? "audio" : "video")} tracks.");
            }

            var warnings = new SortedSet<string>(StringComparer.Ordinal);
            var clip = Patch(GeneratorClip.Create("c_generator", 0, duration, kind), kind, color, toneHz, req.Opacity, req.GainDb, req.FadeInFrames, req.FadeOutFrames, warnings);
            var result = TimelineEdits.Place(Ids, p, [new ClipPlacement(trackId, clip)], at, mode);
            return Warn(result with { Label = "Add Generator" }, warnings);
        });
    }

    private Task<ApiResult> GeneratorUpdateAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.GeneratorUpdateRequest);
        var id = Args.Required(req.ClipId, "clipId");
        GeneratorKind? kind = req.Kind is null ? null : EnumWire.Parse(req.Kind, "kind", GeneratorKind.ColorMatte);
        var color = MatteColor(req.Color);
        var toneHz = ToneHz(req.ToneHz);
        return d.Edits.EditAsync(ctx, req, p =>
        {
            if (RequireClip(p, id) is not GeneratorClip generator)
            {
                throw ApiException.InvalidArgument("clipId", $"'{id}' is not a generator clip.");
            }

            var newKind = kind ?? generator.Kind;
            if (newKind.TrackKindOf() != generator.Kind.TrackKindOf())
            {
                throw ApiException.InvalidArgument("kind", $"A {generator.Kind.DisplayName()} cannot become a {newKind.DisplayName()}: generators keep their kind of track.");
            }

            var warnings = new SortedSet<string>(StringComparer.Ordinal);
            var result = TimelineEdits.UpdateClips(
                p,
                [id],
                c => Patch((GeneratorClip)c, newKind, color, toneHz, req.Opacity, req.GainDb, req.FadeInFrames, req.FadeOutFrames, warnings),
                "Edit Generator");
            return Warn(result, warnings);
        });
    }

    // A #RRGGBB colour in upper case (no alpha: a matte is opaque, its opacity is a separate field), or null.
    private static string? MatteColor(string? value) =>
        value is null ? null
        : value.Length == 7 ? Args.Color(value, "color", withAlpha: false)
        : throw ApiException.InvalidArgument("color", $"'color' must be #RRGGBB; it was '{value}'.");

    private static double? ToneHz(double? value) =>
        value is { } hz ? Args.InRange(hz, GeneratorClip.MinToneHz, GeneratorClip.MaxToneHz, "toneHz") : null;

    // Applies the given fields to the side the kind uses; a field of the other side is reported and ignored.
    private static GeneratorClip Patch(
        GeneratorClip g, GeneratorKind kind, string? color, double? toneHz, double? opacity, double? gainDb, long? fadeIn, long? fadeOut, ISet<string> warnings)
    {
        if (color is not null && kind != GeneratorKind.ColorMatte)
        {
            warnings.Add("color applies to colorMatte only; it was ignored.");
        }

        if (toneHz is not null && kind != GeneratorKind.Tone)
        {
            warnings.Add("toneHz applies to tone only; it was ignored.");
        }

        if (kind.IsVideo())
        {
            if (gainDb is not null)
            {
                warnings.Add("gainDb applies to tone only; it was ignored.");
            }

            return g with
            {
                Kind = kind,
                Color = kind == GeneratorKind.ColorMatte ? color ?? g.Color : g.Color,
                Video = g.Video with
                {
                    Opacity = opacity ?? g.Video.Opacity,
                    FadeInFrames = fadeIn ?? g.Video.FadeInFrames,
                    FadeOutFrames = fadeOut ?? g.Video.FadeOutFrames,
                },
            };
        }

        if (opacity is not null)
        {
            warnings.Add("opacity applies to colorMatte and bars only; it was ignored.");
        }

        return g with
        {
            Kind = kind,
            ToneHz = toneHz ?? g.ToneHz,
            Audio = g.Audio with
            {
                GainDb = gainDb ?? g.Audio.GainDb,
                FadeInFrames = fadeIn ?? g.Audio.FadeInFrames,
                FadeOutFrames = fadeOut ?? g.Audio.FadeOutFrames,
            },
        };
    }

    // edit.clip-props on a generator: the fields of its own side (video props for the matte and the bars, audio props
    // for the tone) and enabled; the rest is reported and ignored. Its colour, frequency and kind go through
    // edit.generator.update.
    private static Clip ApplyGenerator(GeneratorClip g, bool enabled, ClipPropsRequest req, ISet<string> warnings)
    {
        if (g.Kind.IsVideo())
        {
            if (req.GainDb is not null || req.AudioEffects is not null || req.Freeze is not null)
            {
                warnings.Add("Video generators take opacity, transform, color, effects, fades and enabled; gainDb, audioEffects and freeze were ignored.");
            }

            return g with { Enabled = enabled, Video = MergeVideo(g.Video, req) };
        }

        if (req.Opacity is not null || req.Transform is not null || req.Color is not null || req.Effects is not null || req.Freeze is not null)
        {
            warnings.Add("Tones take gainDb, audioEffects, fades and enabled; opacity, transform, color, effects and freeze were ignored.");
        }

        return g with { Enabled = enabled, Audio = MergeAudio(g.Audio, req) };
    }
}
