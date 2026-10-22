using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Cutlass.App.Controls.Drag;
using Cutlass.App.ViewModels;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.Controls;

/// <summary>Drawing (DESIGN 5.1 order: lanes, gaps, clips, transitions, In/Out, markers, snap line, playhead).</summary>
public sealed partial class TimelineControl
{
    private const double ClipRadius = 3;
    private const double ClipLabelSize = 11;
    private const double RulerLabelSize = 10;
    private const double HeaderTextSize = 11;
    private const double HatchSpacing = 8;

    private static readonly Typeface Regular = new(FontFamily.Default);
    private static readonly Typeface SemiBold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var size = Bounds.Size;
        var p = _palette;
        context.FillRectangle(TimelinePalette.Brush(p.Bg0), new Rect(size));
        if (ViewModel is not { } vm)
        {
            return;
        }

        var project = vm.Session.Current;
        var view = vm.ViewState;
        var layout = vm.GetLayout();
        var lanes = new Rect(
            TimelineViewState.HeaderWidth,
            TimelineViewState.RulerHeight,
            Math.Max(0, size.Width - TimelineViewState.HeaderWidth),
            Math.Max(0, size.Height - TimelineViewState.RulerHeight));
        var drag = _drag?.Operation;

        using (context.PushClip(lanes))
        {
            DrawLanes(context, project, view, layout, lanes);
            DrawSelectedGap(context, vm, layout, lanes);
            DrawClips(context, vm, project, view, layout, lanes);
            DrawOutlines(context, drag, view, layout, lanes);
            DrawTransitions(context, vm, project, view, layout, lanes);
            DrawInOut(context, project, vm, view, lanes);
            DrawMarkerLines(context, project, view, lanes);
            DrawDropGhost(context, project, view, layout, lanes);
            DrawMarquee(context, drag, view, lanes);
            DrawRazorLine(context, vm, view, lanes);
        }

        DrawRuler(context, project, vm, view, lanes);
        DrawHeaders(context, project, vm, view, layout, size);
        DrawSnapLine(context, drag?.SnapFrame ?? _drop?.SnapFrame, view, lanes);
        DrawPlayhead(context, vm.Playhead, view, lanes);
        DrawCorner(context, project, vm);
        DrawTooltip(context, drag);
        DrawReadOnlyFrame(context, vm, lanes);
    }

    // AGENT_INTEGRATION 3.7: a 1 px Accent frame around the lanes while an agent holds the project.
    private void DrawReadOnlyFrame(DrawingContext context, TimelineViewModel vm, Rect lanes)
    {
        if (!vm.IsReadOnly || lanes.Width < 2 || lanes.Height < 2)
        {
            return;
        }

        var pen = new Pen(TimelinePalette.Brush(_palette.Accent), 1);
        context.DrawRectangle(null, pen, new Rect(lanes.X + 0.5, lanes.Y + 0.5, lanes.Width - 1, lanes.Height - 1));
    }

    private static double LaneX(TimelineViewState view, double frame) => TimelineViewState.HeaderWidth + view.FrameToX(frame);

    // Keeps very long clips at extreme zoom from producing huge coordinates.
    private static (double Left, double Right) ClampSpan(double left, double right, Rect lanes) =>
        (Math.Max(left, lanes.Left - 8), Math.Min(right, lanes.Right + 8));

    private static FormattedText Text(string text, double size, IBrush brush, Typeface? typeface = null) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface ?? Regular, size, brush);

    private static void Hatch(DrawingContext context, Rect rect, IPen pen)
    {
        using (context.PushClip(rect))
        {
            for (var x = rect.Left - rect.Height; x < rect.Right; x += HatchSpacing)
            {
                context.DrawLine(pen, new Point(x, rect.Bottom), new Point(x + rect.Height, rect.Top));
            }
        }
    }

    private Color ClipColor(Project project, Clip clip) => clip switch
    {
        TitleClip => _palette.TitleClip,
        MediaClip { Stream: StreamKind.Audio } => _palette.AudioClip,
        MediaClip m when TimelineQuery.FindMedia(project, m.MediaId)?.Info.IsStill == true => _palette.StillClip,
        _ => _palette.VideoClip,
    };

    private void DrawLanes(DrawingContext context, Project project, TimelineViewState view, TrackLayout layout, Rect lanes)
    {
        var p = _palette;
        var border = new Pen(TimelinePalette.Brush(p.Border), 1);
        var hatch = new Pen(TimelinePalette.Brush(p.Border, 0.9), 1);
        foreach (var row in layout.Rows)
        {
            var y = view.ToControlY(row.Top);
            var rect = new Rect(lanes.Left, y, lanes.Width, row.Height);
            if (!rect.Intersects(lanes))
            {
                continue;
            }

            if (TimelineQuery.FindTrack(project, row.TrackId) is { Locked: true })
            {
                Hatch(context, rect, hatch);
            }

            context.DrawLine(border, new Point(lanes.Left, y + row.Height - 0.5), new Point(lanes.Right, y + row.Height - 0.5));
        }

        var dividerY = view.ToControlY(layout.DividerTop);
        context.FillRectangle(TimelinePalette.Brush(p.Bg2), new Rect(lanes.Left, dividerY, lanes.Width, TimelineViewState.DividerHeight));
    }

    private void DrawSelectedGap(DrawingContext context, TimelineViewModel vm, TrackLayout layout, Rect lanes)
    {
        if (vm.SelectedGap is not { } gap || layout.Row(gap.TrackId) is not { } row)
        {
            return;
        }

        var view = vm.ViewState;
        var (left, right) = ClampSpan(LaneX(view, gap.Start), LaneX(view, gap.End), lanes);
        var rect = new Rect(left, view.ToControlY(row.Top) + 1, Math.Max(0, right - left), row.Height - 2);
        context.FillRectangle(TimelinePalette.Brush(_palette.Bg3), rect);
        context.DrawRectangle(new Pen(TimelinePalette.Brush(_palette.Selection, 0.5), 1), rect.Deflate(0.5));
    }

    private void DrawClips(DrawingContext context, TimelineViewModel vm, Project project, TimelineViewState view, TrackLayout layout, Rect lanes)
    {
        foreach (var row in layout.Rows)
        {
            var top = view.ToControlY(row.Top);
            if (top > lanes.Bottom || top + row.Height < lanes.Top || TimelineQuery.FindTrack(project, row.TrackId) is not { } track)
            {
                continue;
            }

            foreach (var clip in track.Clips)
            {
                var left = LaneX(view, clip.Start);
                var right = LaneX(view, clip.End);
                if (right < lanes.Left || left > lanes.Right)
                {
                    continue;
                }

                DrawClip(context, vm, project, clip, row, new Rect(left, top + 1, Math.Max(1, right - left), row.Height - 2), lanes);
            }
        }
    }

    private void DrawClip(DrawingContext context, TimelineViewModel vm, Project project, Clip clip, TrackRow row, Rect full, Rect lanes)
    {
        var p = _palette;
        var selected = vm.SelectedClipIds.Contains(clip.Id);
        var color = ClipColor(project, clip);
        if (selected)
        {
            color = TimelinePalette.Lighten(color, 0.12);
        }

        var (left, right) = ClampSpan(full.Left, full.Right, lanes);
        var rect = new Rect(left, full.Top, Math.Max(1, right - left), full.Height);
        using var opacity = clip.Enabled ? default(DrawingContext.PushedState?) : context.PushOpacity(0.4);
        var rounded = new RoundedRect(rect, ClipRadius);
        context.DrawRectangle(TimelinePalette.Brush(color), null, rounded);

        var view = vm.ViewState;
        // DESIGN 4.2 and 11: a clip is offline when its media is not in the project or its file is missing; it keeps
        // its body color under red stripes and gets the OFFLINE badge.
        var offline = clip is MediaClip mc &&
            (TimelineQuery.FindMedia(project, mc.MediaId) is null || vm.IsMediaOffline?.Invoke(mc.MediaId) == true);
        if (offline)
        {
            Hatch(context, rect, new Pen(TimelinePalette.Brush(p.Danger, 0.8), 2));
        }
        else
        {
            DrawClipContent(context, project, clip, full, rect);
        }

        // Fades: a diagonal shade from the clip corner to the fade length.
        var (fadeIn, fadeOut) = HitTester.Fades(clip);
        var shade = TimelinePalette.Brush(Colors.Black, 0.35);
        if (fadeIn > 0)
        {
            var fx = full.Left + (fadeIn * view.PixelsPerFrame);
            DrawTriangle(context, shade, new Point(full.Left, full.Bottom), new Point(full.Left, full.Top), new Point(fx, full.Top));
        }

        if (fadeOut > 0)
        {
            var fx = full.Right - (fadeOut * view.PixelsPerFrame);
            DrawTriangle(context, shade, new Point(full.Right, full.Bottom), new Point(full.Right, full.Top), new Point(fx, full.Top));
        }

        if (clip is MediaClip { Stream: StreamKind.Audio } audio && !offline)
        {
            DrawVolume(context, vm, audio, full, rect, selected);
        }

        using (context.PushClip(rect.Deflate(new Thickness(4, 0))))
        {
            var labelLeft = Math.Max(full.Left, lanes.Left) + 6;
            if (clip.LinkId is not null && rect.Width > 30)
            {
                // DESIGN 5.5: linked clips carry a small chain glyph before their name.
                DrawLinkGlyph(context, new Point(labelLeft, full.Top + 5));
                labelLeft += 14;
            }

            if (rect.Width > 16)
            {
                var name = TimelineQuery.DisplayName(project, clip);
                context.DrawText(Text(name, ClipLabelSize, TimelinePalette.Brush(p.TextPrimary)), new Point(labelLeft, full.Top + 3));
            }

            DrawBadges(context, project, clip, rect, full.Top + 3, offline);
        }

        // Fade handles on selected or hovered clips (DESIGN 5.1: 8x8 px corner squares).
        if (selected || _hoverClipId == clip.Id)
        {
            var handles = HitTester.FadeHandles(full.Left, full.Right, row.Height, fadeIn * view.PixelsPerFrame, fadeOut * view.PixelsPerFrame);
            if (handles is { } h)
            {
                var brush = TimelinePalette.Brush(p.Selection, 0.8);
                context.FillRectangle(brush, new Rect(h.InX, full.Top, HitTester.FadeHandleSize, HitTester.FadeHandleSize));
                context.FillRectangle(brush, new Rect(h.OutX, full.Top, HitTester.FadeHandleSize, HitTester.FadeHandleSize));
            }
        }

        if (selected)
        {
            context.DrawRectangle(null, new Pen(TimelinePalette.Brush(p.Selection), 2), new RoundedRect(rect.Deflate(1), ClipRadius));
        }
        else
        {
            context.DrawRectangle(null, new Pen(TimelinePalette.Brush(Colors.Black, 0.35), 1), new RoundedRect(rect.Deflate(0.5), ClipRadius));
        }

    }

    private void DrawBadges(DrawingContext context, Project project, Clip clip, Rect rect, double y, bool offline)
    {
        var badges = new List<(string Text, Color Color)>();
        if (offline)
        {
            badges.Add(("OFFLINE", _palette.Danger));
        }

        if (clip is MediaClip m)
        {
            if (m.SpeedPercent != 100)
            {
                badges.Add((m.SpeedPercent.ToString(CultureInfo.InvariantCulture) + "%", _palette.Bg0));
            }

            if (m.Stream == StreamKind.Video && (m.Video.Transform != Cutlass.Core.Model.Transform.Identity || m.Video.Color != ColorAdjust.Neutral || m.Video.Opacity != 1))
            {
                badges.Add(("fx", _palette.Bg0));
            }

            if (TimelineQuery.SyncOffset(project, clip.Id) is { } offset && offset != 0)
            {
                badges.Add(((offset > 0 ? "+" : string.Empty) + offset.ToString(CultureInfo.InvariantCulture), _palette.Danger));
            }
        }

        var x = rect.Right - 4;
        foreach (var (text, color) in badges)
        {
            var formatted = Text(text, RulerLabelSize, TimelinePalette.Brush(_palette.TextPrimary), SemiBold);
            var width = formatted.Width + 6;
            x -= width;
            if (x < rect.Left + 24)
            {
                break;
            }

            context.DrawRectangle(TimelinePalette.Brush(color, 0.75), null, new RoundedRect(new Rect(x, y, width, formatted.Height), 2));
            context.DrawText(formatted, new Point(x + 3, y));
            x -= 3;
        }
    }

    private void DrawLinkGlyph(DrawingContext context, Point origin)
    {
        // Two interlocking chain links, 11 x 7 px.
        var pen = new Pen(TimelinePalette.Brush(_palette.TextPrimary, 0.85), 1.3);
        context.DrawRectangle(null, pen, new RoundedRect(new Rect(origin.X, origin.Y + 1, 6.5, 5), 2.5));
        context.DrawRectangle(null, pen, new RoundedRect(new Rect(origin.X + 4.5, origin.Y + 1, 6.5, 5), 2.5));
    }

    // DESIGN 5.1 and 5.5: the volume line (clip gain plus the keyframe envelope) and the keyframe dots, on the -60 to
    // +12 dB scale with 0 dB at 83% of the clip height. Selected clips draw it brighter; they are the editable ones.
    private void DrawVolume(DrawingContext context, TimelineViewModel vm, MediaClip clip, Rect full, Rect visible, bool selected)
    {
        var p = _palette;
        var view = vm.ViewState;
        var keys = clip.Audio.Keys;
        var linePen = new Pen(TimelinePalette.Brush(p.Snap, selected ? 0.95 : 0.55), selected ? 1.5 : 1);
        double Y(double db) => VolumeScale.DbToY(db, full.Top, full.Height);
        using (context.PushClip(visible))
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(full.Left, Y(VolumeScale.LevelAt(clip, 0))), false);
                if (!keys.IsDefaultOrEmpty)
                {
                    foreach (var key in keys)
                    {
                        g.LineTo(new Point(LaneX(view, clip.Start + key.Frame + 0.5), Y(clip.Audio.GainDb + key.Db)));
                    }
                }

                g.LineTo(new Point(full.Right, Y(VolumeScale.LevelAt(clip, clip.Duration))));
                g.EndFigure(false);
            }

            context.DrawGeometry(null, linePen, geometry);
            if (keys.IsDefaultOrEmpty)
            {
                return;
            }

            var selectedKey = vm.SelectedVolumeKey is { } k && k.ClipId == clip.Id ? k.Frame : (long?)null;
            foreach (var key in keys)
            {
                var center = new Point(LaneX(view, clip.Start + key.Frame + 0.5), Y(clip.Audio.GainDb + key.Db));
                var isSelected = selectedKey == key.Frame;
                var fill = TimelinePalette.Brush(isSelected ? p.Selection : p.Snap, selected ? 1 : 0.6);
                var outline = new Pen(TimelinePalette.Brush(isSelected ? p.Accent : Colors.Black, 0.8), isSelected ? 2 : 1);
                context.DrawEllipse(fill, outline, center, VolumeScale.DotRadius, VolumeScale.DotRadius);
            }
        }
    }

    private static void DrawTriangle(DrawingContext context, IBrush brush, Point a, Point b, Point c)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(a, true);
            g.LineTo(b);
            g.LineTo(c);
            g.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geometry);
    }

    private void DrawOutlines(DrawingContext context, IDragOperation? drag, TimelineViewState view, TrackLayout layout, Rect lanes)
    {
        if (drag?.OriginProject is not { } origin)
        {
            return;
        }

        var pen = new Pen(TimelinePalette.Brush(_palette.Selection, 0.45), 1, new DashStyle([3, 3], 0));
        foreach (var id in drag.OutlineClipIds)
        {
            if (TimelineQuery.FindClip(origin, id) is not { } found || layout.Row(found.Track.Id) is not { } row)
            {
                continue;
            }

            var (left, right) = ClampSpan(LaneX(view, found.Clip.Start), LaneX(view, found.Clip.End), lanes);
            if (right <= left)
            {
                continue;
            }

            var rect = new Rect(left, view.ToControlY(row.Top) + 1, right - left, row.Height - 2);
            context.DrawRectangle(null, pen, new RoundedRect(rect.Deflate(0.5), ClipRadius));
        }
    }

    private void DrawTransitions(DrawingContext context, TimelineViewModel vm, Project project, TimelineViewState view, TrackLayout layout, Rect lanes)
    {
        var p = _palette;
        var hatch = new Pen(TimelinePalette.Brush(p.TextPrimary, 0.3), 1);
        foreach (var row in layout.Rows)
        {
            if (TimelineQuery.FindTrack(project, row.TrackId) is not { } track)
            {
                continue;
            }

            foreach (var transition in track.Transitions)
            {
                if (TimelineQuery.CutFrame(track, transition) is not { } cut)
                {
                    continue;
                }

                var (l, r) = HitTester.TransitionSpan(view, cut, transition.Duration);
                var (left, right) = ClampSpan(l + TimelineViewState.HeaderWidth, r + TimelineViewState.HeaderWidth, lanes);
                if (right < lanes.Left || left > lanes.Right)
                {
                    continue;
                }

                var bandHeight = row.Height * HitTester.TransitionBandShare;
                var rect = new Rect(left, view.ToControlY(row.Top) + ((row.Height - bandHeight) / 2), Math.Max(1, right - left), bandHeight);
                context.DrawRectangle(TimelinePalette.Brush(p.Bg3, 0.9), null, new RoundedRect(rect, 2));
                Hatch(context, rect, hatch);
                var selected = vm.SelectedTransitionId == transition.Id;
                var outline = selected ? new Pen(TimelinePalette.Brush(p.Selection), 2) : new Pen(TimelinePalette.Brush(p.Border), 1);
                context.DrawRectangle(null, outline, new RoundedRect(rect.Deflate(selected ? 1 : 0.5), 2));
                if (TransitionLimit(project, transition.Id) is not null && rect.Width >= 8)
                {
                    // DESIGN 5.5: a shortened transition gets a red corner flag (the tooltip explains it on hover).
                    var flag = Math.Min(8, Math.Min(rect.Width, rect.Height) / 2);
                    DrawTriangle(
                        context,
                        TimelinePalette.Brush(p.Danger),
                        new Point(rect.Right - flag, rect.Top),
                        new Point(rect.Right, rect.Top),
                        new Point(rect.Right, rect.Top + flag));
                }
            }
        }
    }

    private void DrawInOut(DrawingContext context, Project project, TimelineViewModel vm, TimelineViewState view, Rect lanes)
    {
        var seq = project.Sequence;
        if (seq.InFrame is null && seq.OutFrame is null)
        {
            return;
        }

        var start = seq.InFrame ?? 0;
        var end = seq.OutFrame ?? Math.Max(vm.SequenceEnd, start + 1);
        var (left, right) = ClampSpan(LaneX(view, start), LaneX(view, end), lanes);
        if (right > left)
        {
            context.FillRectangle(TimelinePalette.Brush(_palette.InOutRange), new Rect(left, lanes.Top, right - left, lanes.Height));
        }
    }

    private void DrawMarkerLines(DrawingContext context, Project project, TimelineViewState view, Rect lanes)
    {
        foreach (var marker in project.Sequence.Markers)
        {
            var x = Math.Round(LaneX(view, marker.Frame)) + 0.5;
            if (x < lanes.Left || x > lanes.Right)
            {
                continue;
            }

            var color = Color.TryParse(marker.Color, out var c) ? c : _palette.Marker;
            context.DrawLine(new Pen(TimelinePalette.Brush(color, 0.4), 1), new Point(x, lanes.Top), new Point(x, lanes.Bottom));
        }
    }

    private void DrawDropGhost(DrawingContext context, Project project, TimelineViewState view, TrackLayout layout, Rect lanes)
    {
        if (_drop is not { } drop)
        {
            return;
        }

        var outline = new Pen(TimelinePalette.Brush(_palette.Selection, 0.9), 1, new DashStyle([4, 3], 0));
        if (drop.Effect is { } effect)
        {
            DrawEffectGhost(context, effect, view, layout, lanes, outline);
            return;
        }

        foreach (var placement in drop.Placements)
        {
            if (layout.Row(placement.TrackId) is not { } row)
            {
                continue;
            }

            var start = drop.Frame + placement.Clip.Start;
            var (left, right) = ClampSpan(LaneX(view, start), LaneX(view, start + placement.Clip.Duration), lanes);
            if (right <= left)
            {
                continue;
            }

            var rect = new Rect(left, view.ToControlY(row.Top) + 1, right - left, row.Height - 2);
            context.DrawRectangle(TimelinePalette.Brush(ClipColor(project, placement.Clip), 0.5), outline, new RoundedRect(rect, ClipRadius));
        }

        if (drop.Insert)
        {
            // Insert shows where later clips will be pushed from.
            var x = Math.Round(LaneX(view, drop.Frame)) + 0.5;
            context.DrawLine(new Pen(TimelinePalette.Brush(_palette.Accent), 2), new Point(x, lanes.Top), new Point(x, lanes.Bottom));
        }
    }

    // Effects tab drops (DESIGN 4.2): the cut a transition goes to highlights with an Accent band of the transition's
    // length, a title shows a TitleClip ghost, and a fade preset outlines the clip it applies to.
    private void DrawEffectGhost(DrawingContext context, EffectDropPreview effect, TimelineViewState view, TrackLayout layout, Rect lanes, Pen outline)
    {
        if (layout.Row(effect.TrackId) is not { } row)
        {
            return;
        }

        var (left, right) = ClampSpan(LaneX(view, effect.Start), LaneX(view, effect.Start + effect.Duration), lanes);
        if (right <= left)
        {
            return;
        }

        var top = view.ToControlY(row.Top);
        var body = new Rect(left, top + 1, right - left, row.Height - 2);
        switch (effect.Category)
        {
            case EffectCategory.Transition:
                var band = Math.Max(10, row.Height * 0.4);
                var rect = new Rect(left, top + ((row.Height - band) / 2), Math.Max(2, right - left), band);
                context.DrawRectangle(TimelinePalette.Brush(_palette.Accent, 0.35), new Pen(TimelinePalette.Brush(_palette.Accent), 1.5), new RoundedRect(rect, 2));
                var cut = Math.Round(LaneX(view, effect.Start + (effect.Duration / 2.0))) + 0.5;
                context.DrawLine(new Pen(TimelinePalette.Brush(_palette.Accent), 2), new Point(cut, top + 1), new Point(cut, top + row.Height - 1));
                break;
            case EffectCategory.Title:
                context.DrawRectangle(TimelinePalette.Brush(_palette.TitleClip, 0.5), outline, new RoundedRect(body, ClipRadius));
                break;
            default:
                context.DrawRectangle(TimelinePalette.Brush(_palette.Accent, 0.15), new Pen(TimelinePalette.Brush(_palette.Accent), 2), new RoundedRect(body, ClipRadius));
                break;
        }
    }

    private void DrawMarquee(DrawingContext context, IDragOperation? drag, TimelineViewState view, Rect lanes)
    {
        if (drag is not MarqueeDrag marquee)
        {
            return;
        }

        var x0 = LaneX(view, marquee.StartFrame);
        var x1 = LaneX(view, marquee.EndFrame);
        var y0 = view.ToControlY(marquee.StartContentY);
        var y1 = view.ToControlY(marquee.EndContentY);
        var rect = new Rect(new Point(Math.Min(x0, x1), Math.Min(y0, y1)), new Point(Math.Max(x0, x1), Math.Max(y0, y1))).Intersect(lanes);
        context.DrawRectangle(TimelinePalette.Brush(_palette.Accent, 0.12), new Pen(TimelinePalette.Brush(_palette.Accent, 0.9), 1), rect);
    }

    private void DrawRazorLine(DrawingContext context, TimelineViewModel vm, TimelineViewState view, Rect lanes)
    {
        if (vm.Tool != EditTool.Razor || _razorFrame is not { } frame || _drag is not null)
        {
            return;
        }

        var x = Math.Round(LaneX(view, frame)) + 0.5;
        context.DrawLine(new Pen(TimelinePalette.Brush(_palette.Selection, 0.8), 1, new DashStyle([2, 2], 0)), new Point(x, lanes.Top), new Point(x, lanes.Bottom));
    }

    private void DrawRuler(DrawingContext context, Project project, TimelineViewModel vm, TimelineViewState view, Rect lanes)
    {
        var p = _palette;
        var ruler = new Rect(lanes.Left, 0, lanes.Width, TimelineViewState.RulerHeight);
        context.FillRectangle(TimelinePalette.Brush(p.Bg2), ruler);
        using (context.PushClip(ruler))
        {
            var rate = project.Sequence.Settings.Rate;
            var steps = view.ChooseRulerSteps(rate.TimecodeBase);
            var tickPen = new Pen(TimelinePalette.Brush(p.TextSecondary, 0.7), 1);
            var labelBrush = TimelinePalette.Brush(p.TextSecondary);
            var first = (long)Math.Floor(view.ScrollFrame / steps.Minor) * steps.Minor;
            var last = view.XToFrame(lanes.Width) + steps.Major;
            for (var f = Math.Max(0, first); f <= last; f += steps.Minor)
            {
                var x = Math.Round(LaneX(view, f)) + 0.5;
                var major = f % steps.Major == 0;
                var tick = major ? 10 : 4;
                context.DrawLine(tickPen, new Point(x, ruler.Bottom - tick), new Point(x, ruler.Bottom));
                if (major)
                {
                    context.DrawText(Text(Timecode.Format(f, rate), RulerLabelSize, labelBrush), new Point(x + 3, 3));
                }
            }

            // In/Out on the ruler: the shaded range plus brackets.
            var seq = project.Sequence;
            if (seq.InFrame is not null || seq.OutFrame is not null)
            {
                var start = seq.InFrame ?? 0;
                var end = seq.OutFrame ?? Math.Max(vm.SequenceEnd, start + 1);
                var (left, right) = ClampSpan(LaneX(view, start), LaneX(view, end), lanes);
                var band = new Rect(left, ruler.Bottom - 5, Math.Max(0, right - left), 5);
                context.FillRectangle(TimelinePalette.Brush(p.TextPrimary, 0.25), band);
                var bracket = new Pen(TimelinePalette.Brush(p.TextPrimary), 2);
                if (seq.InFrame is { } i)
                {
                    var x = LaneX(view, i) + 1;
                    context.DrawLine(bracket, new Point(x, ruler.Bottom - 12), new Point(x, ruler.Bottom));
                }

                if (seq.OutFrame is { } o)
                {
                    var x = LaneX(view, o) - 1;
                    context.DrawLine(bracket, new Point(x, ruler.Bottom - 12), new Point(x, ruler.Bottom));
                }
            }

            foreach (var marker in seq.Markers)
            {
                var x = Math.Round(LaneX(view, marker.Frame)) + 0.5;
                var color = Color.TryParse(marker.Color, out var c) ? c : p.Marker;
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(x - 4.5, 14), true);
                    g.LineTo(new Point(x + 4.5, 14));
                    g.LineTo(new Point(x + 4.5, 20));
                    g.LineTo(new Point(x, 25));
                    g.LineTo(new Point(x - 4.5, 20));
                    g.EndFigure(true);
                }

                context.DrawGeometry(TimelinePalette.Brush(color), null, geometry);
                if (marker.Name.Length > 0)
                {
                    // Named markers show their name beside the head, in the marker colour.
                    var label = Text(marker.Name, RulerLabelSize, TimelinePalette.Brush(color));
                    using (context.PushClip(new Rect(x + 6, 12, 120, 14)))
                    {
                        context.DrawText(label, new Point(x + 6, 13));
                    }
                }
            }
        }

        context.DrawLine(new Pen(TimelinePalette.Brush(p.Border), 1), new Point(lanes.Left, ruler.Bottom - 0.5), new Point(lanes.Right, ruler.Bottom - 0.5));
    }

    private void DrawHeaders(DrawingContext context, Project project, TimelineViewModel vm, TimelineViewState view, TrackLayout layout, Size size)
    {
        var p = _palette;
        var column = new Rect(0, TimelineViewState.RulerHeight, TimelineViewState.HeaderWidth, Math.Max(0, size.Height - TimelineViewState.RulerHeight));
        context.FillRectangle(TimelinePalette.Brush(p.Bg2), column);
        using (context.PushClip(column))
        {
            var border = new Pen(TimelinePalette.Brush(p.Border), 1);
            foreach (var row in layout.Rows)
            {
                var top = view.ToControlY(row.Top);
                if (top > column.Bottom || top + row.Height < column.Top || TimelineQuery.FindTrack(project, row.TrackId) is not { } track)
                {
                    continue;
                }

                if (vm.SelectedTrackId == track.Id)
                {
                    // The selected track (last header clicked): Remove Track and the header menu act on it.
                    context.FillRectangle(TimelinePalette.Brush(p.Bg3), new Rect(0, top, TimelineViewState.HeaderWidth, row.Height));
                }

                if (track.Locked)
                {
                    Hatch(context, new Rect(0, top, TimelineViewState.HeaderWidth, row.Height), new Pen(TimelinePalette.Brush(p.Border, 0.6), 1));
                }

                foreach (var button in HitTester.HeaderButtons(row.Kind))
                {
                    DrawHeaderButton(context, track, row, button, top);
                }

                DrawGainField(context, track, row, top);

                var nameLeft = 40.0;
                var nameRight = HitTester.HeaderButtons(row.Kind).Where(b => b.Zone != HitZone.HeaderTarget).Min(b => b.X) - 4;
                using (context.PushClip(new Rect(nameLeft, top, Math.Max(0, nameRight - nameLeft), row.Height)))
                {
                    context.DrawText(Text(track.Name, HeaderTextSize, TimelinePalette.Brush(p.TextPrimary)), new Point(nameLeft, top + 8));
                }

                context.DrawLine(border, new Point(0, top + row.Height - 0.5), new Point(TimelineViewState.HeaderWidth, top + row.Height - 0.5));
            }

            context.FillRectangle(
                TimelinePalette.Brush(p.Bg1),
                new Rect(0, view.ToControlY(layout.DividerTop), TimelineViewState.HeaderWidth, TimelineViewState.DividerHeight));
        }

        context.DrawLine(
            new Pen(TimelinePalette.Brush(p.Border), 1),
            new Point(TimelineViewState.HeaderWidth - 0.5, 0),
            new Point(TimelineViewState.HeaderWidth - 0.5, size.Height));
    }

    private void DrawHeaderButton(DrawingContext context, Track track, TrackRow row, HeaderButton button, double top)
    {
        var p = _palette;
        var rect = new Rect(button.X, top + button.Y, button.Width, button.Height);
        if (rect.Bottom > top + row.Height)
        {
            return;
        }

        if (button.Zone == HitZone.HeaderTarget)
        {
            var label = (row.Kind == TrackKind.Video ? "V" : "A") + (row.KindIndex + 1).ToString(CultureInfo.InvariantCulture);
            var fill = track.Targeted ? p.Accent : p.Bg3;
            context.DrawRectangle(TimelinePalette.Brush(fill), null, new RoundedRect(rect, 4));
            var text = Text(label, HeaderTextSize, TimelinePalette.Brush(track.Targeted ? p.OnAccent : p.TextSecondary), SemiBold);
            context.DrawText(text, new Point(rect.Center.X - (text.Width / 2), rect.Center.Y - (text.Height / 2)));
            return;
        }

        var (active, icon, fallback) = button.Zone switch
        {
            HitZone.HeaderVisibility => (!track.Hidden, _iconEye, "o"),
            HitZone.HeaderLock => (track.Locked, _iconLock, "L"),
            HitZone.HeaderMute => (track.Muted, _iconMute, "M"),
            HitZone.HeaderSolo => (track.Solo, _iconSolo, "S"),
            _ => (false, (Geometry?)null, "?"),
        };

        // Eye is "on" when visible; lock, mute and solo light up when engaged.
        var color = button.Zone switch
        {
            HitZone.HeaderVisibility => active ? p.TextSecondary : p.TextDisabled,
            HitZone.HeaderSolo => active ? p.Snap : p.TextSecondary,
            _ => active ? p.Accent : p.TextSecondary,
        };

        if (active && button.Zone != HitZone.HeaderVisibility)
        {
            context.DrawRectangle(TimelinePalette.Brush(p.Bg3), null, new RoundedRect(rect, 4));
        }

        var brush = TimelinePalette.Brush(color);
        if (icon is not null)
        {
            const double iconSize = 14;
            var scale = iconSize / 24;
            var origin = new Point(rect.Center.X - (iconSize / 2), rect.Center.Y - (iconSize / 2));
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y)))
            {
                context.DrawGeometry(brush, null, icon);
            }
        }
        else
        {
            var text = Text(fallback, HeaderTextSize, brush, SemiBold);
            context.DrawText(text, new Point(rect.Center.X - (text.Width / 2), rect.Center.Y - (text.Height / 2)));
        }

        if (button.Zone == HitZone.HeaderVisibility && !active)
        {
            context.DrawLine(new Pen(brush, 1.5), rect.TopRight + new Point(-4, 4), rect.BottomLeft + new Point(4, -4));
        }
    }

    private void DrawSnapLine(DrawingContext context, long? snapFrame, TimelineViewState view, Rect lanes)
    {
        if (snapFrame is not { } frame)
        {
            return;
        }

        var x = Math.Round(LaneX(view, frame)) + 0.5;
        if (x >= lanes.Left && x <= lanes.Right)
        {
            context.DrawLine(new Pen(TimelinePalette.Brush(_palette.Snap), 1), new Point(x, 0), new Point(x, lanes.Bottom));
        }
    }

    private void DrawPlayhead(DrawingContext context, long playhead, TimelineViewState view, Rect lanes)
    {
        var x = Math.Round(LaneX(view, playhead)) + 0.5;
        if (x < lanes.Left - 6 || x > lanes.Right + 6)
        {
            return;
        }

        var brush = TimelinePalette.Brush(_palette.Playhead);
        using (context.PushClip(new Rect(lanes.Left - 1, 0, lanes.Width + 1, lanes.Bottom)))
        {
            context.DrawLine(new Pen(brush, 1), new Point(x, 14), new Point(x, lanes.Bottom));
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(x - 6, 2), true);
                g.LineTo(new Point(x + 6, 2));
                g.LineTo(new Point(x + 6, 11));
                g.LineTo(new Point(x, 17));
                g.LineTo(new Point(x - 6, 11));
                g.EndFigure(true);
            }

            context.DrawGeometry(brush, null, geometry);
        }
    }

    private void DrawCorner(DrawingContext context, Project project, TimelineViewModel vm)
    {
        var p = _palette;
        var corner = new Rect(0, 0, TimelineViewState.HeaderWidth, TimelineViewState.RulerHeight);
        context.FillRectangle(TimelinePalette.Brush(p.Bg2), corner);
        var text = Text(Timecode.Format(vm.Playhead, project.Sequence.Settings.Rate), 12, TimelinePalette.Brush(p.TextPrimary), SemiBold);
        context.DrawText(text, new Point(8, (corner.Height - text.Height) / 2));
        var border = new Pen(TimelinePalette.Brush(p.Border), 1);
        context.DrawLine(border, new Point(0, corner.Bottom - 0.5), new Point(corner.Right, corner.Bottom - 0.5));
    }

    private void DrawTooltip(DrawingContext context, IDragOperation? drag)
    {
        if (drag?.Tooltip is not { } tip || _drag is null)
        {
            return;
        }

        var p = _palette;
        var text = Text(tip, HeaderTextSize, TimelinePalette.Brush(drag.TooltipIsLimit ? p.Danger : p.TextPrimary), SemiBold);
        var x = Math.Min(_drag.Context.X + 14, Bounds.Width - text.Width - 12);
        var y = Math.Max(_drag.Context.Y - text.Height - 14, TimelineViewState.RulerHeight + 2);
        var box = new Rect(x, y, text.Width + 10, text.Height + 6);
        context.DrawRectangle(TimelinePalette.Brush(p.Bg3, 0.95), new Pen(TimelinePalette.Brush(p.Border), 1), new RoundedRect(box, 4));
        context.DrawText(text, new Point(x + 5, y + 3));
    }
}
