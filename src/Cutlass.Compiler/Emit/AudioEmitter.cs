using Cutlass.Compiler.Graph;
using Cutlass.Compiler.Planning;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Compiler.Emit;

/// <summary>
/// Emits the audio graph (TECH_BRIEF 7.3 and 7.6): one exact-length segment per clip placed with <c>adelay</c>, one
/// <c>amix</c> per track, then either the export mix (Mixed) or one stereo pair per track (PerTrack).
/// </summary>
internal static class AudioEmitter
{
    private const long MarginFlicks = TimeMath.FlicksPerSecond / 10;

    private const string Aformat = "aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo";

    /// <summary>Emits the audio graph and closes <c>[aout]</c>.</summary>
    /// <param name="ctx">The compile context.</param>
    /// <returns>The number of output channels.</returns>
    public static int Emit(EmitContext ctx)
    {
        var tracks = ctx.Project.Sequence.Tracks.Where(t => t.Kind == TrackKind.Audio).ToList();
        return ctx.Request.AudioLayout == AudioLayout.PerTrack ? EmitPerTrack(ctx, tracks) : EmitMixed(ctx, tracks);
    }

    private static int EmitMixed(EmitContext ctx, List<Track> tracks)
    {
        var anySolo = tracks.Any(t => t.Solo);
        var included = new List<(int Index, GraphChain Chain)>();
        for (var t = 0; t < tracks.Count; t++)
        {
            var track = tracks[t];
            if (track.Muted || (anySolo && !track.Solo))
            {
                continue;
            }

            if (EmitTrack(ctx, t, track, applyTrackGain: true) is { } chain)
            {
                included.Add((t, chain));
            }
        }

        if (included.Count == 0)
        {
            Silence(ctx).Close("aout");
            return 2;
        }

        GraphChain mix;
        if (included.Count == 1)
        {
            // A single track continues in the same chain.
            mix = included[0].Chain;
        }
        else
        {
            var labels = included.Select(p => p.Chain.Close("a" + FfNum.Integer(p.Index))).ToList();
            mix = ctx.Graph.StartJoin(labels, Amix(labels.Count));
        }

        if (ctx.Request.LimitPeaks)
        {
            // level defaults to true (rescales) and latency to false (delays), so both are set explicitly.
            mix.Then("alimiter=limit=0.98:level=0:latency=1");
        }

        PadToWindow(ctx, mix).Close("aout");
        return 2;
    }

    private static int EmitPerTrack(EmitContext ctx, List<Track> tracks)
    {
        if (tracks.Count == 0)
        {
            Silence(ctx).Close("aout");
            return 2;
        }

        // Every track is present (empty ones as silence); mute, solo and track gain are applied by the live mixer.
        var chains = new List<GraphChain>(tracks.Count);
        for (var t = 0; t < tracks.Count; t++)
        {
            chains.Add(EmitTrack(ctx, t, tracks[t], applyTrackGain: false) ?? Silence(ctx));
        }

        if (chains.Count == 1)
        {
            chains[0].Then("anull").Close("aout");
            return 2;
        }

        var labels = chains.Select((c, t) => c.Close("a" + FfNum.Integer(t))).ToList();
        ctx.Graph.StartJoin(labels, "amerge=inputs=" + FfNum.Integer(labels.Count)).Close("aout");
        return 2 * labels.Count;
    }

    // One track: its segments mixed (or a single segment continued), padded and trimmed to S. Null without segments.
    private static GraphChain? EmitTrack(EmitContext ctx, int trackIndex, Track track, bool applyTrackGain)
    {
        var request = ctx.Request;
        var segments = RenderPlanner.PlanAudioTrack(track, request.StartFrame, request.EndFrame);
        var chains = new List<GraphChain>(segments.Length);
        foreach (var segment in segments)
        {
            if (EmitSegment(ctx, segment) is { } chain)
            {
                chains.Add(chain);
            }
        }

        if (chains.Count == 0)
        {
            return null;
        }

        GraphChain trackChain;
        if (chains.Count == 1)
        {
            trackChain = chains[0];
        }
        else
        {
            var prefix = "a" + FfNum.Integer(trackIndex) + "s";
            var labels = chains.Select((c, n) => c.Close(prefix + FfNum.Integer(n))).ToList();
            trackChain = ctx.Graph.StartJoin(labels, Amix(labels.Count));
        }

        PadToWindow(ctx, trackChain);
        if (applyTrackGain && track.GainDb != 0)
        {
            trackChain.Then("volume=" + FfNum.Decimal(track.GainDb) + "dB");
        }

        return trackChain;
    }

    private static GraphChain? EmitSegment(EmitContext ctx, PlannedSegment segment)
    {
        if (segment.Clip is not MediaClip { Stream: StreamKind.Audio } clip)
        {
            ctx.Warn($"Clip {segment.Clip.Id} cannot be heard on an audio track and is skipped.");
            return null;
        }

        var info = ctx.Media(clip.MediaId);
        var audio = info?.Asset.Info.Audio;
        if (info is null || audio is null)
        {
            ctx.Warn($"Media {clip.MediaId} is offline; clip {clip.Id} is silent.");
            return null;
        }

        var speed = clip.SpeedPercent > 0 ? clip.SpeedPercent : 100;
        var n = segment.Length;
        var factors = AtempoFactors(speed);

        // Seek FloorMicros(s(g0)); a position before the source start (prevented by the validator) clamps to 0.
        var seek = Math.Max(0, TimeMath.FloorMicrosFromFlicks(TimeMath.SourceFlicksAt(clip, segment.Start, ctx.Rate)));
        var span = (long)TimeMath.FloorDiv((Int128)n * ctx.Rate.FlicksPerFrame * speed, 100) + MarginFlicks;

        // atempo pre-roll (TECH_BRIEF 7.3): read 0.2 s earlier, padding with silence what lies before the file start.
        long deficitSamples = 0;
        if (factors.Count > 0)
        {
            var preRoll = Math.Min(seek, AtempoPreRoll.PreRollMicros);
            deficitSamples = ((AtempoPreRoll.PreRollMicros - preRoll) * 6 + 62) / 125;
            seek -= preRoll;
            span += AtempoPreRoll.PreRollMicros * TimeMath.FlicksPerSecond / 1_000_000;
        }

        var input = ctx.Inputs.Add(
            info.Original.Path, seek, TimeMath.FloorMicrosFromFlicks(span), false, null, segment.Start, speed);

        var e0 = segment.ExtentStart;
        var samples = FfNum.Integer(ctx.SamplesAt(segment.End) - ctx.SamplesAt(segment.Start));
        var chain = ctx.Graph.StartInput(input, audio.StreamIndex).Then("aresample=48000:async=1:first_pts=0");
        if (factors.Count > 0)
        {
            if (deficitSamples > 0)
            {
                chain.Then("adelay=" + FfNum.Integer(deficitSamples) + "S:all=1");
            }

            foreach (var factor in factors)
            {
                chain.Then("atempo=" + FfNum.Decimal(factor));
            }

            // atrim counts samples, so the lead cut is exact; the timestamps restart at 0 for the pts-based filters.
            chain.Then("atrim=start_sample=" + FfNum.Integer(AtempoPreRoll.HeadTrimSamples(speed)))
                .Then("asetpts=PTS-STARTPTS");
        }

        chain.Then(Aformat).Then("apad=whole_len=" + samples).Then("atrim=end_sample=" + samples);
        var offsetSamples = ctx.SamplesAt(segment.Start) - ctx.SamplesAt(e0);
        if (offsetSamples > 0)
        {
            chain.Then("asetpts=PTS-STARTPTS+" + FfNum.Integer(offsetSamples));
        }

        if (!clip.Audio.Keys.IsDefaultOrEmpty)
        {
            chain.Then(VolumeEnvelope.Filter(clip.Audio.Keys, clip.Start - e0, clip.Audio.GainDb, ctx.Rate));
        }
        else if (clip.Audio.GainDb != 0)
        {
            chain.Then("volume=" + FfNum.Decimal(clip.Audio.GainDb) + "dB");
        }

        if (segment.Head is { } head)
        {
            var (start, end) = RenderPlanner.TransitionRegion(head, clip.Start);
            chain.Then(Afade(ctx, "in", start, end, e0));
        }
        else if (clip.Audio.FadeInFrames > 0)
        {
            chain.Then(Afade(ctx, "in", clip.Start, clip.Start + Math.Min(clip.Audio.FadeInFrames, clip.Duration), e0));
        }

        if (segment.Tail is { } tail)
        {
            var (start, end) = RenderPlanner.TransitionRegion(tail, clip.End);
            chain.Then(Afade(ctx, "out", start, end, e0));
        }
        else if (clip.Audio.FadeOutFrames > 0)
        {
            chain.Then(Afade(ctx, "out", clip.End - Math.Min(clip.Audio.FadeOutFrames, clip.Duration), clip.End, e0));
        }

        var delay = ctx.SamplesAt(segment.Start) - ctx.SamplesAt(ctx.Request.StartFrame);
        return chain.Then("asetpts=PTS-STARTPTS").Then("adelay=" + FfNum.Integer(delay) + "S:all=1");
    }

    /// <summary>The atempo steps for a speed, each within [0.5, 2.0], multiplying to the speed exactly.</summary>
    /// <param name="speedPercent">The speed in percent.</param>
    /// <returns>The factors; empty at 100%.</returns>
    public static IReadOnlyList<double> AtempoFactors(int speedPercent)
    {
        var factors = new List<double>();
        long num = speedPercent, den = 100;
        while (num > 2 * den)
        {
            factors.Add(2.0);
            den *= 2;
        }

        while (2 * num < den)
        {
            factors.Add(0.5);
            num *= 2;
        }

        if (num != den)
        {
            factors.Add((double)num / den);
        }

        return factors;
    }

    private static string Afade(EmitContext ctx, string type, long fromFrame, long toFrame, long extentStart)
    {
        var ss = ctx.SamplesAt(fromFrame) - ctx.SamplesAt(extentStart);
        var ns = ctx.SamplesAt(toFrame) - ctx.SamplesAt(fromFrame);
        return "afade=t=" + type + ":ss=" + FfNum.Integer(ss) + ":ns=" + FfNum.Integer(ns) + ":curve=tri";
    }

    private static string Amix(int inputs) =>
        "amix=inputs=" + FfNum.Integer(inputs) + ":duration=longest:dropout_transition=0:normalize=0";

    private static GraphChain PadToWindow(EmitContext ctx, GraphChain chain)
    {
        var samples = FfNum.Integer(ctx.WindowSamples);
        return chain.Then("apad=whole_len=" + samples).Then("atrim=end_sample=" + samples);
    }

    private static GraphChain Silence(EmitContext ctx) =>
        ctx.Graph.StartSource("anullsrc=r=48000:cl=stereo")
            .Then(Aformat)
            .Then("atrim=end_sample=" + FfNum.Integer(ctx.WindowSamples));
}
