using Cutlass.Compiler.Export;
using Cutlass.Compiler.Graph;
using Cutlass.Compiler.Planning;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Compiler.Emit;

/// <summary>
/// Emits the video graph (TECH_BRIEF 7.2 to 7.5 and 7.7): the black base, every visible video track bottom-up as
/// runs of segments joined with <c>concat</c> and <c>xfade</c>, each run overlaid at its first frame, and the tail.
/// </summary>
internal static class VideoEmitter
{
    /// <summary>The minimum input span margin: 0.1 s in flicks.</summary>
    private const long MinMarginFlicks = TimeMath.FlicksPerSecond / 10;

    private const string BaseLabel = "base";

    private const string SetParams = "setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv";

    /// <summary>The time base the setpts step works in: one flick (TECH_BRIEF 7.2, step 5).</summary>
    private static readonly string FlickTimeBase = "settb=1/" + FfNum.Integer(TimeMath.FlicksPerSecond);

    /// <summary>Emits the whole video graph and closes <c>[vout]</c>.</summary>
    /// <param name="ctx">The compile context.</param>
    /// <returns>The number of frames <c>[vout]</c> produces.</returns>
    public static long Emit(EmitContext ctx)
    {
        var request = ctx.Request;
        var current = ctx.Graph.StartSource("color=c=black:s=" + ctx.CanvasSize + ":r=" + FfNum.Rate(ctx.Rate))
            .Then("trim=end_frame=" + FfNum.Integer(ctx.WindowFrames))
            .Then("format=yuv420p");
        var currentLabel = BaseLabel;
        var composite = 0;
        var trackIndex = -1;
        foreach (var track in ctx.Project.Sequence.Tracks)
        {
            if (track.Kind != TrackKind.Video)
            {
                continue;
            }

            trackIndex++;
            if (track.Hidden)
            {
                continue;
            }

            var counters = new TrackCounters();
            var runs = RenderPlanner.PlanVideoTrack(track, request.StartFrame, request.EndFrame);
            for (var m = 0; m < runs.Length; m++)
            {
                var (runLabel, x, y) = EmitRun(ctx, trackIndex, m, runs[m], counters);
                current.Close(currentLabel);
                current = ctx.Graph.StartJoin(
                    [currentLabel, runLabel],
                    "overlay=x=" + FfNum.Integer(x) + ":y=" + FfNum.Integer(y) + ":format=yuv420:eof_action=pass");
                currentLabel = "c" + FfNum.Integer(composite++);
            }
        }

        return EmitTail(ctx, current);
    }

    /// <summary>Maps a transition kind to its <c>xfade</c> transition name.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The xfade name.</returns>
    public static string XfadeName(TransitionKind kind) => kind switch
    {
        TransitionKind.DipToBlack => "fadeblack",
        TransitionKind.DipToWhite => "fadewhite",
        TransitionKind.WipeLeft => "wipeleft",
        TransitionKind.WipeRight => "wiperight",
        TransitionKind.SlideLeft => "slideleft",
        TransitionKind.SlideRight => "slideright",
        TransitionKind.CircleOpen => "circleopen",
        TransitionKind.WipeUp => "wipeup",
        TransitionKind.WipeDown => "wipedown",
        TransitionKind.WipeTopLeft => "wipetl",
        TransitionKind.WipeTopRight => "wipetr",
        TransitionKind.WipeBottomLeft => "wipebl",
        TransitionKind.WipeBottomRight => "wipebr",
        TransitionKind.SlideUp => "slideup",
        TransitionKind.SlideDown => "slidedown",
        TransitionKind.SmoothLeft => "smoothleft",
        TransitionKind.SmoothRight => "smoothright",
        TransitionKind.SmoothUp => "smoothup",
        TransitionKind.SmoothDown => "smoothdown",
        TransitionKind.CircleClose => "circleclose",
        TransitionKind.CircleCrop => "circlecrop",
        TransitionKind.RectCrop => "rectcrop",
        TransitionKind.Radial => "radial",
        TransitionKind.Distance => "distance",
        TransitionKind.HorizontalOpen => "horzopen",
        TransitionKind.HorizontalClose => "horzclose",
        TransitionKind.VerticalOpen => "vertopen",
        TransitionKind.VerticalClose => "vertclose",
        TransitionKind.DiagonalTopLeft => "diagtl",
        TransitionKind.DiagonalTopRight => "diagtr",
        TransitionKind.DiagonalBottomLeft => "diagbl",
        TransitionKind.DiagonalBottomRight => "diagbr",
        TransitionKind.Dissolve => "dissolve",
        TransitionKind.Pixelize => "pixelize",
        TransitionKind.FadeGrays => "fadegrays",
        TransitionKind.FadeFast => "fadefast",
        TransitionKind.FadeSlow => "fadeslow",
        TransitionKind.SqueezeHorizontal => "squeezeh",
        TransitionKind.SqueezeVertical => "squeezev",
        TransitionKind.ZoomIn => "zoomin",

        // Crossfade, and an audio crossfade that ended up on a video track, dissolve. Every other kind is mapped above
        // (TimelineCompilerTests.XfadeNamesCoverEveryVideoKind checks that only these two reach this arm). All names
        // exist in ffmpeg 6.0's xfade; the 6.1 additions (hlwind, cover*, reveal*) are deliberately absent.
        _ => "fade",
    };

    private static long EmitTail(EmitContext ctx, GraphChain current)
    {
        var request = ctx.Request;
        var frames = ctx.WindowFrames;
        if (request.FrameStep > 1)
        {
            current.Then("framestep=step=" + FfNum.Integer(request.FrameStep));
            frames = (frames + request.FrameStep - 1) / request.FrameStep;
        }

        current.Then(SetParams);
        if (request.Tail.Kind != OutputTailKind.Gif)
        {
            current.Then("format=" + request.Tail.PixelFormat).Close("vout");
            return frames;
        }

        current.Then("fps=15").Then("split").Close(["g0", "g1"]);
        ctx.Graph.StartJoin(["g0"], "palettegen=stats_mode=diff").Close("p");
        ctx.Graph.StartJoin(["g1", "p"], "paletteuse=dither=bayer:bayer_scale=4").Close("vout");

        // fps=15 rounds to the nearest output frame; the count is the nearest integer to frames * 15 / R.
        var rate = ctx.Rate.Value;
        return (long)(((Int128)2 * frames * 15 * rate.Den + rate.Num) / (2 * (Int128)rate.Num));
    }

    private static (string Label, long X, long Y) EmitRun(
        EmitContext ctx,
        int trackIndex,
        int runIndex,
        PlannedRun run,
        TrackCounters counters)
    {
        var placement = FfNum.Integer(run.VisibleStart - ctx.Request.StartFrame);
        var runLabel = "v" + FfNum.Integer(trackIndex) + "r" + FfNum.Integer(runIndex);
        if (run.RunClipCount == 1)
        {
            // A run of one clip skips the canvas and is overlaid directly at its position.
            var single = EmitSegment(ctx, run.Segments[0]);
            single.Chain.Then("setpts=PTS-STARTPTS+" + placement).Close(runLabel);
            return (runLabel, single.X, single.Y);
        }

        // A run of several clips takes the canvas step even when the window needs only one of its segments, so each
        // frame takes the same pixel path in every window (TECH_BRIEF 7.5).
        var multi = run.Segments.Length > 1;
        var labels = new List<string>(run.Segments.Length);
        GraphChain? lone = null;
        foreach (var segment in run.Segments)
        {
            var output = EmitSegment(ctx, segment);
            var n = FfNum.Integer(counters.Segments++);
            var canvas = CanvasStep(ctx, trackIndex, n, output);
            if (multi)
            {
                labels.Add(output.Chain.Close("v" + FfNum.Integer(trackIndex) + "s" + n));
            }
            else
            {
                lone = output.Chain;
            }

            if (canvas is not null)
            {
                TransparentCanvas(ctx, segment.Length).Close(canvas);
            }
        }

        var final = multi ? Join(ctx, trackIndex, run, labels, counters) : lone!;
        if (run.RunHasTransition && !run.HasXfade)
        {
            // xfade accepts only 4:4:4 formats, so a full render converts every frame of this run to yuva444p on the
            // way to the transition. A window that needs no xfade converts them the same way to stay byte-identical.
            final.Then("format=yuva444p");
        }

        if (run.IsWidened)
        {
            final.Then("trim=start_frame=" + FfNum.Integer(run.TrimStart) + ":end_frame=" + FfNum.Integer(run.TrimEnd));
        }

        final.Then("setpts=PTS-STARTPTS+" + placement).Close(runLabel);
        return (runLabel, 0, 0);
    }

    // Joins the canvas-sized segments of a run: pending cut segments are concatenated before each xfade, and the last
    // join stays open so the run continues in the same chain.
    private static GraphChain Join(EmitContext ctx, int trackIndex, PlannedRun run, List<string> labels, TrackCounters counters)
    {
        var prefix = "v" + FfNum.Integer(trackIndex);
        List<string> pending = [labels[0]];
        var accumulated = run.Segments[0].Length;
        for (var i = 1; i < run.Segments.Length; i++)
        {
            var length = run.Segments[i].Length;
            if (run.Joins[i - 1] is not { } transition)
            {
                pending.Add(labels[i]);
                accumulated += length;
                continue;
            }

            var left = pending.Count == 1
                ? pending[0]
                : Concat(ctx, pending).Close(prefix + "j" + FfNum.Integer(counters.Joins++));
            var xfade = ctx.Graph.StartJoin(
                [left, labels[i]],
                "xfade=transition=" + XfadeName(transition.Kind)
                + ":duration=" + ctx.Seconds(transition.Duration)
                + ":offset=" + ctx.Seconds(accumulated - transition.Duration));
            accumulated += length - transition.Duration;
            if (i == run.Segments.Length - 1)
            {
                return xfade;
            }

            pending = [xfade.Close(prefix + "x" + FfNum.Integer(counters.Xfades++))];
        }

        return Concat(ctx, pending);
    }

    // concat always outputs in the 1/1000000 time base; settb restores 1/R, which the frame offsets of setpts and the
    // inputs of xfade rely on (the rescale rounds each microsecond timestamp back to its exact frame).
    private static GraphChain Concat(EmitContext ctx, List<string> labels) =>
        ctx.Graph.StartJoin(labels, "concat=n=" + FfNum.Integer(labels.Count) + ":v=1:a=0")
            .Then("settb=" + FfNum.Integer(ctx.Rate.Value.Den) + "/" + FfNum.Integer(ctx.Rate.Value.Num));

    // The canvas step (TECH_BRIEF 7.4): every segment of a run of several clips becomes canvas sized. Returns the label
    // of the transparent canvas a transformed segment is overlaid on, which the caller defines after the segment.
    private static string? CanvasStep(EmitContext ctx, int trackIndex, string n, VideoSegmentOutput output)
    {
        var chain = output.Chain;
        switch (output.Placement)
        {
            case SegmentPlacement.IdentityFit when output.Width != ctx.Width || output.Height != ctx.Height:
                chain.Then(
                    "pad=w=" + FfNum.Integer(ctx.Width) + ":h=" + FfNum.Integer(ctx.Height)
                    + ":x=" + FfNum.Integer(output.X) + ":y=" + FfNum.Integer(output.Y) + ":color=black@0");
                return null;

            case SegmentPlacement.Transformed:
                // [cv][seg]overlay written as ",[cv]overlay": the labelled canvas is the main input (never pad to place).
                // threads=1: the slice-threaded blend onto a main input with alpha races when the overlaid pixels are
                // partly transparent (opacity, fades, source alpha), which made identical renders differ.
                var canvas = "cv" + FfNum.Integer(trackIndex) + "s" + n;
                chain.ThenWithMainInput(
                    canvas,
                    "overlay=x=" + FfNum.Integer(output.X) + ":y=" + FfNum.Integer(output.Y) + ":format=yuv420:eof_action=pass:threads=1");
                return canvas;

            default:
                return null;
        }
    }

    private static GraphChain TransparentCanvas(EmitContext ctx, long length) =>
        ctx.Graph.StartSource("color=c=black@0:s=" + ctx.CanvasSize + ":r=" + FfNum.Rate(ctx.Rate))
            .Then("trim=end_frame=" + FfNum.Integer(length))
            .Then("format=yuva420p");

    private static VideoSegmentOutput EmitSegment(EmitContext ctx, PlannedSegment segment)
    {
        switch (segment.Clip)
        {
            case TitleClip title:
                var chain = TitleEmitter.EmitSource(ctx, title, segment);
                OpacityAndFades(ctx, chain, segment, title.Opacity, title.FadeInFrames, title.FadeOutFrames);
                return new VideoSegmentOutput(chain, SegmentPlacement.CanvasSized, 0, 0, ctx.Width, ctx.Height);

            case MediaClip { Stream: StreamKind.Video } media:
                return EmitMedia(ctx, media, segment);

            case GeneratorClip generator when generator.Kind.IsVideo():
                return EmitGenerator(ctx, generator, segment);

            default:
                ctx.Warn($"Clip {segment.Clip.Id} cannot be shown on a video track and renders as a gap.");
                return StandIn(ctx, segment);
        }
    }

    // An offline or unusable clip keeps its timing as a transparent canvas, so the run's joins stay exact.
    private static VideoSegmentOutput StandIn(EmitContext ctx, PlannedSegment segment) =>
        new(TransparentCanvas(ctx, segment.Length), SegmentPlacement.CanvasSized, 0, 0, ctx.Width, ctx.Height);

    private static VideoSegmentOutput EmitMedia(EmitContext ctx, MediaClip clip, PlannedSegment segment)
    {
        var info = ctx.Media(clip.MediaId);
        var video = info?.Asset.Info.Video;
        if (info is null || video is null)
        {
            ctx.Warn($"Media {clip.MediaId} is offline; clip {clip.Id} renders as a gap.");
            return StandIn(ctx, segment);
        }

        var n = segment.Length;
        var speed = clip.SpeedPercent > 0 ? clip.SpeedPercent : 100;
        var frameFlicks = ctx.Rate.FlicksPerFrame;
        int inputIndex;
        int streamIndex;
        string[] setpts = [];
        // A frozen still is a no-op: stills already loop one frame.
        var frozen = clip.Freeze && !info.Asset.Info.IsStill;
        if (info.Asset.Info.IsStill)
        {
            // Stills loop at the sequence rate with no seek and no setpts.
            var span = (n * frameFlicks) + Math.Max(MinMarginFlicks, 2 * frameFlicks);
            inputIndex = ctx.Inputs.Add(
                info.Original.Path, null, TimeMath.FloorMicrosFromFlicks(span), true, ctx.Rate.Value, segment.Start, 100);
            streamIndex = video.StreamIndex;
        }
        else if (frozen)
        {
            // Freeze frame (effects batch 4, TECH_BRIEF 7.3 step 2): the seek resolves at SourceInFlicks (SourceFlicksAt
            // returns it for every frame of a frozen clip), the input spans one source frame plus the margin, and the
            // speed marker 0 keeps InputTable from sharing it with a moving use of the same source at the same frame.
            // setpts only shifts by d0, never divides by the speed; the chain below trims to the first decoded frame.
            var seek = ResolveSeek(ctx, info, video, clip, segment.Start);
            var sourceRate = SourceRate(video, ctx.Rate);
            var sourceFrame = (long)TimeMath.FloorDiv((Int128)TimeMath.FlicksPerSecond * sourceRate.Den, sourceRate.Num);
            var span = Math.Max(seek.D0, 0) + Math.Max(MinMarginFlicks, 2 * sourceFrame);
            inputIndex = ctx.Inputs.Add(seek.Path, seek.SeekMicros, TimeMath.FloorMicrosFromFlicks(span), false, null, segment.Start, 0);
            streamIndex = seek.StreamIndex;
            setpts = SetPts(seek.D0, 100);
        }
        else
        {
            var seek = ResolveSeek(ctx, info, video, clip, segment.Start);
            var sourceRate = SourceRate(video, ctx.Rate);
            var sourceFrame = (long)TimeMath.FloorDiv((Int128)TimeMath.FlicksPerSecond * sourceRate.Den, sourceRate.Num);
            var span = (long)TimeMath.FloorDiv((Int128)n * frameFlicks * speed, 100)
                + Math.Max(seek.D0, 0)
                + Math.Max(MinMarginFlicks, 2 * sourceFrame);
            inputIndex = ctx.Inputs.Add(
                seek.Path, seek.SeekMicros, TimeMath.FloorMicrosFromFlicks(span), false, null, segment.Start, speed);
            streamIndex = seek.StreamIndex;
            setpts = SetPts(seek.D0, speed);
        }

        var chain = ctx.Graph.StartInput(inputIndex, streamIndex);
        foreach (var filter in setpts)
        {
            chain.Then(filter);
        }

        var count = FfNum.Integer(n);
        chain.Then("fps=" + FfNum.Rate(ctx.Rate) + ":start_time=0:round=up");
        if (frozen)
        {
            // Keep the first decoded frame only, so the clone pad below repeats it for the whole segment.
            chain.Then("trim=end_frame=1");
        }

        chain.Then("tpad=stop_mode=clone:stop=" + count)
            .Then("trim=end_frame=" + count);

        var g = Geometry.Compute(ctx, video, clip.Video.Transform);
        Crop(chain, g);
        chain.Then(
            "scale=w=" + FfNum.Integer(g.Width) + ":h=" + FfNum.Integer(g.Height) + ":flags=" + ctx.ScaleFlags
            + ":out_color_matrix=bt709:out_range=tv");
        return EffectsAndPlacement(ctx, chain, segment, clip.Id, clip.Video, g);
    }

    // Effects batch 5 (TECH_BRIEF 7.3, generators): a lavfi source at the canvas size and rate, trimmed to the segment,
    // then cropped and scaled exactly like a media clip whose source is the canvas, with the same BT.709 output options.
    // The scale is emitted for an untransformed generator too: it is a no-op resize, but it fixes the colour matrix and
    // range of the chain, so ffmpeg negotiates the same conversions in a one-frame window as in the full render. The
    // matte is drawn as rgb24, so that scale converts it with the BT.709 matrix (the color source alone encodes BT.601
    // values); smptehdbars writes BT.709 limited-range frames already.
    private static VideoSegmentOutput EmitGenerator(EmitContext ctx, GeneratorClip clip, PlannedSegment segment)
    {
        var size = ctx.CanvasSize + ":r=" + FfNum.Rate(ctx.Rate);
        GraphChain chain;
        if (clip.Kind == GeneratorKind.Bars)
        {
            chain = ctx.Graph.StartSource("smptehdbars=s=" + size).Then("trim=end_frame=" + FfNum.Integer(segment.Length));
        }
        else
        {
            var color = clip.Color is { Length: 7 } && TitleEmitter.FormatColor(clip.Color) is { } valid ? valid : null;
            if (color is null)
            {
                ctx.Warn($"The matte colour '{clip.Color}' of clip {clip.Id} is not #RRGGBB; black is used.");
                color = "0x000000";
            }

            chain = ctx.Graph.StartSource("color=c=" + color + ":s=" + size)
                .Then("trim=end_frame=" + FfNum.Integer(segment.Length))
                .Then("format=rgb24");
        }

        var g = Geometry.Compute(ctx, ctx.Width, ctx.Height, clip.Video.Transform);
        Crop(chain, g);
        chain.Then(
            "scale=w=" + FfNum.Integer(g.Width) + ":h=" + FfNum.Integer(g.Height) + ":flags=" + ctx.ScaleFlags
            + ":out_color_matrix=bt709:out_range=tv");
        return EffectsAndPlacement(ctx, chain, segment, clip.Id, clip.Video, g);
    }

    private static void Crop(GraphChain chain, Geometry g)
    {
        if (g.IsCropped)
        {
            chain.Then(
                "crop=w=iw*" + FfNum.Decimal(1 - g.Left - g.Right) + ":h=ih*" + FfNum.Decimal(1 - g.Top - g.Bottom)
                + ":x=iw*" + FfNum.Decimal(g.Left) + ":y=ih*" + FfNum.Decimal(g.Top));
        }
    }

    // The part shared by media clips and generators once the picture has its final size: TECH_BRIEF 7.3 steps 5 and 6,
    // flips, then the RGB-domain colour pair (converted back to BT.709 yuv420p), eq, hue, blur, sharpen and vignette;
    // after the alpha format the chroma key and the user rotation (transparent fill); then opacity, the fades and the
    // placement. Neutral emits nothing.
    private static VideoSegmentOutput EffectsAndPlacement(
        EmitContext ctx, GraphChain chain, PlannedSegment segment, string clipId, VideoProps props, Geometry g)
    {
        var fx = props.Effects;
        Then(chain, VideoEffectFilters.FlipH(fx));
        Then(chain, VideoEffectFilters.FlipV(fx));
        Then(chain, VideoEffectFilters.Temperature(fx));
        Then(chain, VideoEffectFilters.Tint(fx));
        Then(chain, VideoEffectFilters.RgbToYuv(fx));
        Then(chain, Eq(props.Color));
        Then(chain, VideoEffectFilters.Hue(fx));
        Then(chain, VideoEffectFilters.Blur(fx, ctx.PixelScale));
        Then(chain, VideoEffectFilters.Sharpen(fx));
        Then(chain, VideoEffectFilters.Vignette(fx));

        chain.Then("setsar=1").Then("format=yuva420p");
        Then(chain, VideoEffectFilters.ChromaKey(ctx, clipId, fx));
        Then(chain, VideoEffectFilters.Rotate(fx));
        OpacityAndFades(ctx, chain, segment, props.Opacity, props.FadeInFrames, props.FadeOutFrames);

        if (g.IsIdentity)
        {
            var placement = g.Width == ctx.Width && g.Height == ctx.Height ? SegmentPlacement.CanvasSized : SegmentPlacement.IdentityFit;
            return new VideoSegmentOutput(chain, placement, (ctx.Width - g.Width) / 2, (ctx.Height - g.Height) / 2, g.Width, g.Height);
        }

        return new VideoSegmentOutput(chain, SegmentPlacement.Transformed, g.X, g.Y, g.Width, g.Height);
    }

    private static Rational SourceRate(VideoStreamInfo video, FrameRate sequenceRate) =>
        video.RFrameRate.IsPositive ? video.RFrameRate
        : video.AvgFrameRate.IsPositive ? video.AvgFrameRate
        : sequenceRate.Value;

    // TECH_BRIEF 7.2: pick ordinal i with pts_i <= s0 (index, else CFR math), seek to floor_us(pts_file(i)) relative
    // to the file's start, and carry d0 = s0 - P into setpts.
    private static SeekPlan ResolveSeek(EmitContext ctx, MediaRenderInfo info, VideoStreamInfo video, MediaClip clip, long frame)
    {
        const long F = TimeMath.FlicksPerSecond;
        var original = info.Original;
        var s0 = TimeMath.FlicksFromMicros(original.FormatStartMicros) + TimeMath.SourceFlicksAt(clip, frame, ctx.Rate);
        long ordinal;
        long pFlicks;
        long seekMicros;
        if (original.Index is { } index)
        {
            var i = index.FloorOrdinal(s0);
            ordinal = i;
            pFlicks = index.FlicksAt(i);
            seekMicros = index.FloorMicrosAt(i) - original.FormatStartMicros;
        }
        else
        {
            // CFR fallback: i = max(0, floor((s0 - firstPts) * r)), P = firstPts + i / r, all exact.
            var r = SourceRate(video, ctx.Rate);
            var tb = video.TimeBase.IsPositive ? video.TimeBase : new Rational(1, TimeMath.MicrosPerSecond);
            var startPts = video.TimeBase.IsPositive ? video.StartPts : original.FormatStartMicros;
            var iNum = (((Int128)s0 * tb.Den) - ((Int128)startPts * tb.Num * F)) * r.Num;
            var iDen = (Int128)F * tb.Den * r.Den;
            var i = TimeMath.FloorDiv(iNum, iDen);
            if (i < 0)
            {
                i = 0;
            }

            var pNum = ((Int128)startPts * tb.Num * r.Num) + (i * r.Den * tb.Den);
            var pDen = (Int128)tb.Den * r.Num;
            ordinal = (long)i;
            pFlicks = (long)TimeMath.FloorDiv(pNum * F, pDen);
            seekMicros = (long)TimeMath.FloorDiv(pNum * TimeMath.MicrosPerSecond, pDen) - original.FormatStartMicros;
        }

        var path = original.Path;
        var stream = video.StreamIndex;
        if (ctx.Request.UseProxies && info.Proxy is { Index: { } proxyIndex } proxy && ordinal < proxyIndex.Count)
        {
            // The ordinal comes from the original; its pts from the proxy, whose only stream is the video
            // (TECH_BRIEF 6.6). A proxy without an index cannot be seeked by ordinal and is not used.
            path = proxy.Path;
            stream = 0;
            seekMicros = proxyIndex.FloorMicrosAt((int)ordinal) - proxy.FormatStartMicros;
        }

        // A first frame before the file's start time would give a negative seek; ffmpeg starts at 0 anyway.
        return new SeekPlan(path, Math.Max(0, seekMicros), s0 - pFlicks, stream);
    }

    // TECH_BRIEF 7.2 step 5. setpts truncates its result to the stream time base, so at a coarse time base (1 ms in
    // Matroska) a fractional d0 or a speed division moved frames into the wrong output slot. The timestamps first move
    // to the flick time base (an exact rescale for every common time base) and d0 is an exact number of flicks.
    private static string[] SetPts(long d0, int speedPercent)
    {
        if (d0 == 0 && speedPercent == 100)
        {
            return [];
        }

        var speed = FfNum.Decimal(speedPercent / 100.0);
        if (d0 == 0)
        {
            return [FlickTimeBase, "setpts=PTS/" + speed];
        }

        var shift = (d0 > 0 ? "-" : "+") + FfNum.Integer(Math.Abs(d0));
        return [FlickTimeBase, speedPercent == 100 ? "setpts=PTS" + shift : "setpts=(PTS" + shift + ")/" + speed];
    }

    private static void Then(GraphChain chain, string? filter)
    {
        if (filter is not null)
        {
            chain.Then(filter);
        }
    }

    private static string? Eq(ColorAdjust color)
    {
        var parts = new List<string>(4);
        if (color.Brightness != 0)
        {
            parts.Add("brightness=" + FfNum.Decimal(color.Brightness));
        }

        if (color.Contrast != 1)
        {
            parts.Add("contrast=" + FfNum.Decimal(color.Contrast));
        }

        if (color.Saturation != 1)
        {
            parts.Add("saturation=" + FfNum.Decimal(color.Saturation));
        }

        if (color.Gamma != 1)
        {
            parts.Add("gamma=" + FfNum.Decimal(color.Gamma));
        }

        return parts.Count == 0 ? null : "eq=" + string.Join(':', parts);
    }

    // Opacity, then the user fades in clip-extent time (TECH_BRIEF 7.3 steps 6 and 7). A fade at an end that has a
    // transition is ignored; a segment that starts inside its extent shifts its timestamps by the offset first.
    private static void OpacityAndFades(EmitContext ctx, GraphChain chain, PlannedSegment segment, double opacity, long fadeIn, long fadeOut)
    {
        if (opacity < 1)
        {
            chain.Then("lut=a=val*" + FfNum.Decimal(Math.Max(0, opacity)));
        }

        var duration = segment.Clip.Duration;
        var fadeInFrames = segment.Head is null ? Math.Clamp(fadeIn, 0, duration) : 0;
        var fadeOutFrames = segment.Tail is null ? Math.Clamp(fadeOut, 0, duration) : 0;
        if (fadeInFrames == 0 && fadeOutFrames == 0)
        {
            return;
        }

        var offset = segment.Offset;
        if (offset > 0)
        {
            chain.Then("setpts=PTS-STARTPTS+" + FfNum.Integer(offset));
        }

        if (fadeInFrames > 0)
        {
            chain.Then("fade=t=in:st=" + ctx.Seconds(segment.Clip.Start - segment.ExtentStart) + ":d=" + ctx.Seconds(fadeInFrames) + ":alpha=1");
        }

        if (fadeOutFrames > 0)
        {
            chain.Then(
                "fade=t=out:st=" + ctx.Seconds(segment.ExtentLength - fadeOutFrames) + ":d=" + ctx.Seconds(fadeOutFrames) + ":alpha=1");
        }

        if (offset > 0)
        {
            chain.Then("setpts=PTS-STARTPTS");
        }
    }

    /// <summary>Per-track label counters.</summary>
    private sealed class TrackCounters
    {
        public int Segments { get; set; }

        public int Joins { get; set; }

        public int Xfades { get; set; }
    }

    /// <summary>Where and how the input is read for one segment.</summary>
    private readonly record struct SeekPlan(string Path, long SeekMicros, long D0, int StreamIndex);

    /// <summary>The scale size and canvas position of a media segment (TECH_BRIEF 7.3, geometry).</summary>
    private readonly record struct Geometry(
        double Left,
        double Top,
        double Right,
        double Bottom,
        long Width,
        long Height,
        long X,
        long Y,
        bool IsIdentity)
    {
        public bool IsCropped => Left > 0 || Top > 0 || Right > 0 || Bottom > 0;

        public static Geometry Compute(EmitContext ctx, VideoStreamInfo video, Transform transform) =>
            Compute(
                ctx,
                video.DisplayWidth > 0 ? video.DisplayWidth : Math.Max(1, video.Width),
                video.DisplayHeight > 0 ? video.DisplayHeight : Math.Max(1, video.Height),
                transform);

        // dw and dh are the display size of the source; a generator passes the canvas size.
        public static Geometry Compute(EmitContext ctx, double dw, double dh, Transform transform)
        {
            var (l, r) = CropPair(transform.CropLeft, transform.CropRight);
            var (t, b) = CropPair(transform.CropTop, transform.CropBottom);

            // A non-positive scale cannot be drawn; it is treated as fit.
            var u = transform.Scale > 0 ? transform.Scale : 1;
            var fs = Math.Min(ctx.Width / dw, ctx.Height / dh);
            var width = Even(dw * (1 - l - r) * fs * u);
            var height = Even(dh * (1 - t - b) * fs * u);
            var x = Round((ctx.Width / 2.0) + (transform.X * ctx.PixelScale) - (dw * fs * u / 2) + (l * dw * fs * u));
            var y = Round((ctx.Height / 2.0) + (transform.Y * ctx.PixelScale) - (dh * fs * u / 2) + (t * dh * fs * u));
            var identity = transform.X == 0 && transform.Y == 0 && transform.Scale == 1 && l == 0 && t == 0 && r == 0 && b == 0;
            return new Geometry(l, t, r, b, width, height, x, y, identity);
        }

        // Crops are fractions 0 to 0.9; a pair that would leave less than 10% is scaled down to leave 10%.
        private static (double A, double B) CropPair(double a, double b)
        {
            a = double.IsFinite(a) ? Math.Clamp(a, 0, 0.9) : 0;
            b = double.IsFinite(b) ? Math.Clamp(b, 0, 0.9) : 0;
            var sum = a + b;
            if (sum > 0.9)
            {
                a = a * 0.9 / sum;
                b = b * 0.9 / sum;
            }

            return (a, b);
        }

        // "Round" and "even" are unspecified; halves round away from zero, and sizes are at least 2.
        private static long Round(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);

        private static long Even(double value) => Math.Max(2, 2 * (long)Math.Round(value / 2, MidpointRounding.AwayFromZero));
    }
}
