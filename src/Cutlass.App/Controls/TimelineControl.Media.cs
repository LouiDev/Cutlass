using Avalonia;
using Avalonia.Media;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Media.Services;

namespace Cutlass.App.Controls;

/// <summary>
/// Clip content (DESIGN 5.1): video clips tile a filmstrip, each tile showing the source frame at its left edge, and
/// audio clips draw a min/max waveform from the peak level nearest one peak per pixel. Until a media item's sheets or
/// peaks exist the plain clip body is the placeholder; the control repaints when they arrive.
/// </summary>
public sealed partial class TimelineControl
{
    /// <summary>The strip at the top of a clip that keeps the name readable above the content.</summary>
    private const double ContentTop = 16;

    /// <summary>Content is drawn only when at least this much height is left below the name.</summary>
    private const double MinContentHeight = 10;

    private void DrawClipContent(DrawingContext context, Project project, Clip clip, Rect full, Rect lanes)
    {
        if (clip is not MediaClip media || ViewModel is not { Artifacts: { } artifacts } vm
            || TimelineQuery.FindMedia(project, media.MediaId) is not { } asset)
        {
            return;
        }

        var area = new Rect(full.Left, full.Top + ContentTop, full.Width, full.Height - ContentTop - 1);
        var visible = area.Intersect(lanes);
        if (area.Height < MinContentHeight || visible.Width <= 0)
        {
            return;
        }

        var view = vm.ViewState;
        var rate = project.Sequence.Settings.Rate;
        using (context.PushClip(visible))
        {
            if (media.Stream == StreamKind.Video && artifacts.TryGetSheet(asset) is { } sheet)
            {
                DrawFilmstrip(context, media, sheet, area, visible, view.PixelsPerFrame, rate);
            }
            else if (media.Stream == StreamKind.Audio && artifacts.TryGetPeaks(asset) is { } peaks)
            {
                DrawWaveform(context, media, peaks, area, visible, view.PixelsPerFrame, rate);
            }
        }
    }

    private static void DrawFilmstrip(DrawingContext context, MediaClip clip, ThumbnailSheet sheet, Rect area, Rect visible, double pixelsPerFrame, FrameRate rate)
    {
        var tileHeight = area.Height;
        var tileWidth = tileHeight * ThumbnailSheet.TileWidth / ThumbnailSheet.TileHeight;
        if (tileWidth < 4 || sheet.Count <= 0)
        {
            return;
        }

        var cache = FilmstripCache.Shared;
        var first = (long)Math.Floor((visible.Left - area.Left) / tileWidth);
        for (var k = Math.Max(0, first); area.Left + (k * tileWidth) < visible.Right; k++)
        {
            var x = area.Left + (k * tileWidth);

            // Each tile shows the source frame at its left edge (DESIGN 5.1).
            var frame = clip.Start + (long)Math.Floor(k * tileWidth / pixelsPerFrame);
            frame = Math.Clamp(frame, clip.Start, clip.End - 1);
            var micros = TimeMath.FloorMicrosFromFlicks(TimeMath.SourceFlicksAt(clip, frame, rate));
            var tile = sheet.TileAt(micros);
            if (cache.Get(tile.SheetPath) is not { } bitmap)
            {
                continue;
            }

            context.DrawImage(bitmap, new Rect(tile.X, tile.Y, tile.Width, tile.Height), new Rect(x, area.Top, tileWidth, tileHeight));
        }
    }

    private void DrawWaveform(DrawingContext context, MediaClip clip, PeakPyramid peaks, Rect area, Rect visible, double pixelsPerFrame, FrameRate rate)
    {
        var bins = (int)Math.Ceiling(visible.Width);
        if (bins <= 0)
        {
            return;
        }

        // 48 kHz positions from the file start, at the visible edges of the clip (fractional frames are fine for drawing).
        double SampleAt(double x)
        {
            var frames = (x - area.Left) / pixelsPerFrame;
            var flicks = clip.SourceInFlicks + (frames * rate.FlicksPerFrame * clip.SpeedPercent / 100.0);
            return flicks * TimeMath.SampleRate / TimeMath.FlicksPerSecond;
        }

        var start = (long)Math.Floor(SampleAt(visible.Left));
        var end = (long)Math.Ceiling(SampleAt(visible.Left + bins));
        var envelope = peaks.PeaksFor(start, end, bins);
        if (envelope.Length == 0)
        {
            return;
        }

        var mid = area.Top + (area.Height / 2);
        var half = Math.Max(1, (area.Height / 2) - 1);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var i = 0; i < envelope.Length; i++)
            {
                var top = mid - Math.Max(0.5, WaveformScale.Display(envelope[i].Max) * half);
                var point = new Point(visible.Left + i + 0.5, top);
                if (i == 0)
                {
                    g.BeginFigure(new Point(visible.Left, top), true);
                }

                g.LineTo(point);
            }

            for (var i = envelope.Length - 1; i >= 0; i--)
            {
                var bottom = mid - Math.Min(-0.5, WaveformScale.Display(envelope[i].Min) * half);
                g.LineTo(new Point(visible.Left + i + 0.5, bottom));
            }

            g.EndFigure(true);
        }

        context.DrawGeometry(TimelinePalette.Brush(TimelinePalette.Lighten(_palette.AudioClip, 0.45), 0.9), null, geometry);
    }

    private void DrawGainField(DrawingContext context, Track track, TrackRow row, double top)
    {
        if (!TrackGain.HasField(row.Kind == TrackKind.Audio, row.Height))
        {
            return;
        }

        var p = _palette;
        var rect = new Rect(TrackGain.FieldX, top + TrackGain.FieldY, TrackGain.FieldWidth, TrackGain.FieldHeight);
        context.DrawRectangle(TimelinePalette.Brush(p.Bg3), null, new RoundedRect(rect, 3));
        var brush = TimelinePalette.Brush(track.GainDb == 0 ? p.TextSecondary : p.TextPrimary);
        var text = Text(TrackGain.Format(track.GainDb), RulerLabelSize, brush);
        context.DrawText(text, new Point(rect.Center.X - (text.Width / 2), rect.Center.Y - (text.Height / 2)));
    }

    private void OnFilmstripLoaded(object? sender, EventArgs e) => InvalidateVisual();

    private void OnArtifactsChanged(object? sender, EventArgs e) => InvalidateVisual();
}
