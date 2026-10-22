using Cutlass.Core.Model;

namespace Cutlass.App.Controls;

/// <summary>
/// Timeline zoom, scroll and track heights (DESIGN 5.1). Pure math without Avalonia types, so it is unit tested.
/// Lane coordinates start at the left edge of the lanes (right of the track headers):
/// <c>x = (frame - ScrollFrame) * PixelsPerFrame</c>. Content y starts below the ruler, before vertical scrolling.
/// View state is never undoable.
/// </summary>
public sealed class TimelineViewState
{
    /// <summary>The smallest zoom in pixels per frame.</summary>
    public const double MinPixelsPerFrame = 0.01;

    /// <summary>The largest zoom in pixels per frame.</summary>
    public const double MaxPixelsPerFrame = 40;

    /// <summary>The default zoom in pixels per frame.</summary>
    public const double DefaultPixelsPerFrame = 2;

    /// <summary>The width of the track header column.</summary>
    public const double HeaderWidth = 150;

    /// <summary>The height of the ruler.</summary>
    public const double RulerHeight = 28;

    /// <summary>The height of the divider between video and audio tracks.</summary>
    public const double DividerHeight = 6;

    /// <summary>The default video track height.</summary>
    public const double DefaultVideoTrackHeight = 56;

    /// <summary>The default audio track height.</summary>
    public const double DefaultAudioTrackHeight = 48;

    /// <summary>The smallest track height.</summary>
    public const double MinTrackHeight = 32;

    /// <summary>The largest track height.</summary>
    public const double MaxTrackHeight = 160;

    /// <summary>Major ruler ticks are at least this far apart, so labels never collide.</summary>
    public const double MinMajorTickPixels = 84;

    /// <summary>Minor ruler ticks are at least this far apart.</summary>
    public const double MinMinorTickPixels = 6;

    private readonly Dictionary<string, double> _heights = new(StringComparer.Ordinal);

    /// <summary>Raised after zoom, scroll, viewport or track heights changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The zoom in pixels per frame (0.01 to 40).</summary>
    public double PixelsPerFrame { get; private set; } = DefaultPixelsPerFrame;

    /// <summary>The (fractional) frame at the left edge of the lanes.</summary>
    public double ScrollFrame { get; private set; }

    /// <summary>The vertical scroll offset in pixels.</summary>
    public double ScrollY { get; private set; }

    /// <summary>The width of the lanes area in pixels (control width minus the headers).</summary>
    public double ViewportWidth { get; private set; }

    /// <summary>The height of the track area in pixels (control height minus the ruler).</summary>
    public double ViewportHeight { get; private set; }

    /// <summary>The sequence length in frames used for the horizontal scroll extent.</summary>
    public long ContentFrames { get; private set; }

    /// <summary>The height of all track rows in pixels.</summary>
    public double ContentHeight { get; private set; }

    /// <summary>The number of frames visible in the lanes.</summary>
    public double VisibleFrames => ViewportWidth > 0 ? ViewportWidth / PixelsPerFrame : 0;

    /// <summary>
    /// The horizontal scroll extent in frames: the sequence plus half a screen, so the end of the sequence can sit in
    /// the middle of the view.
    /// </summary>
    public double ExtentFrames => Math.Max(ContentFrames + (VisibleFrames / 2), VisibleFrames);

    /// <summary>The largest <see cref="ScrollFrame"/>.</summary>
    public double MaxScrollFrame => Math.Max(0, ExtentFrames - VisibleFrames);

    /// <summary>The largest <see cref="ScrollY"/>.</summary>
    public double MaxScrollY => Math.Max(0, ContentHeight - ViewportHeight);

    /// <summary>A lane x coordinate for a (fractional) frame.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The x in lane pixels.</returns>
    public double FrameToX(double frame) => (frame - ScrollFrame) * PixelsPerFrame;

    /// <summary>The exact (fractional) frame at a lane x coordinate.</summary>
    /// <param name="x">The x in lane pixels.</param>
    /// <returns>The fractional frame.</returns>
    public double XToFrame(double x) => (x / PixelsPerFrame) + ScrollFrame;

    /// <summary>The frame whose cell contains a lane x coordinate (the playhead lands on it).</summary>
    /// <param name="x">The x in lane pixels.</param>
    /// <returns>The frame.</returns>
    public long FrameAt(double x) => (long)Math.Floor(XToFrame(x));

    /// <summary>The frame boundary nearest to a lane x coordinate (razor cuts and drops land on it).</summary>
    /// <param name="x">The x in lane pixels.</param>
    /// <returns>The frame.</returns>
    public long BoundaryAt(double x) => (long)Math.Round(XToFrame(x), MidpointRounding.AwayFromZero);

    /// <summary>A whole-frame delta for a fractional frame difference.</summary>
    /// <param name="frames">The difference in frames.</param>
    /// <returns>The rounded delta.</returns>
    public static long RoundDelta(double frames) => (long)Math.Round(frames, MidpointRounding.AwayFromZero);

    /// <summary>The content y for a control y coordinate.</summary>
    /// <param name="controlY">The y in control pixels.</param>
    /// <returns>The content y.</returns>
    public double ToContentY(double controlY) => controlY - RulerHeight + ScrollY;

    /// <summary>The control y for a content y coordinate.</summary>
    /// <param name="contentY">The content y.</param>
    /// <returns>The y in control pixels.</returns>
    public double ToControlY(double contentY) => contentY + RulerHeight - ScrollY;

    /// <summary>Sets the lanes viewport size. Keeps the scroll within range.</summary>
    /// <param name="width">The lanes width.</param>
    /// <param name="height">The track area height.</param>
    public void SetViewport(double width, double height)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        if (width == ViewportWidth && height == ViewportHeight)
        {
            return;
        }

        ViewportWidth = width;
        ViewportHeight = height;
        ClampScroll();
        Raise();
    }

    /// <summary>Sets the scroll extents from the project.</summary>
    /// <param name="frames">The sequence length in frames.</param>
    /// <param name="height">The height of all track rows.</param>
    public void SetContent(long frames, double height)
    {
        frames = Math.Max(0, frames);
        height = Math.Max(0, height);
        if (frames == ContentFrames && height == ContentHeight)
        {
            return;
        }

        ContentFrames = frames;
        ContentHeight = height;
        ClampScroll();
        Raise();
    }

    /// <summary>Sets the zoom, keeping the frame under <paramref name="anchorX"/> in place.</summary>
    /// <param name="pixelsPerFrame">The new zoom (clamped to 0.01 to 40).</param>
    /// <param name="anchorX">The lane x that stays put.</param>
    public void SetZoom(double pixelsPerFrame, double anchorX)
    {
        if (double.IsNaN(pixelsPerFrame) || double.IsInfinity(pixelsPerFrame))
        {
            return;
        }

        var ppf = Math.Clamp(pixelsPerFrame, MinPixelsPerFrame, MaxPixelsPerFrame);
        if (ppf == PixelsPerFrame)
        {
            return;
        }

        var anchorFrame = XToFrame(anchorX);
        PixelsPerFrame = ppf;
        ScrollFrame = anchorFrame - (anchorX / ppf);
        ClampScroll();
        Raise();
    }

    /// <summary>Multiplies the zoom by <paramref name="factor"/> around a lane x coordinate.</summary>
    /// <param name="factor">The factor (above 1 zooms in).</param>
    /// <param name="anchorX">The lane x that stays put.</param>
    public void ZoomBy(double factor, double anchorX) => SetZoom(PixelsPerFrame * factor, anchorX);

    /// <summary>Zooms so <paramref name="frames"/> fill the viewport with a small margin and scrolls to frame 0.</summary>
    /// <param name="frames">The sequence length; 0 or less restores the default zoom.</param>
    public void ZoomToFit(long frames)
    {
        var ppf = frames > 0 && ViewportWidth > 0 ? ViewportWidth * 0.95 / frames : DefaultPixelsPerFrame;
        PixelsPerFrame = Math.Clamp(ppf, MinPixelsPerFrame, MaxPixelsPerFrame);
        ScrollFrame = 0;
        ClampScroll();
        Raise();
    }

    /// <summary>Scrolls so <paramref name="frame"/> is at the left edge (clamped).</summary>
    /// <param name="frame">The frame.</param>
    public void ScrollToFrame(double frame)
    {
        var old = ScrollFrame;
        ScrollFrame = frame;
        ClampScroll();
        if (old != ScrollFrame)
        {
            Raise();
        }
    }

    /// <summary>Scrolls horizontally by pixels (positive moves later in time).</summary>
    /// <param name="dx">The distance in pixels.</param>
    public void ScrollByPixels(double dx) => ScrollToFrame(ScrollFrame + (dx / PixelsPerFrame));

    /// <summary>Sets the vertical scroll (clamped).</summary>
    /// <param name="y">The offset in pixels.</param>
    public void SetScrollY(double y)
    {
        var clamped = Math.Clamp(y, 0, MaxScrollY);
        if (clamped == ScrollY)
        {
            return;
        }

        ScrollY = clamped;
        Raise();
    }

    /// <summary>
    /// Pages the view when <paramref name="frame"/> left it (DESIGN 5.2 playback follow): past the right edge the frame
    /// becomes the left edge, before the left edge it becomes the right edge.
    /// </summary>
    /// <param name="frame">The playhead frame.</param>
    /// <returns>True when the view scrolled.</returns>
    public bool EnsureVisible(long frame)
    {
        if (ViewportWidth <= 0)
        {
            return false;
        }

        var x = FrameToX(frame);
        var old = ScrollFrame;
        if (x >= ViewportWidth)
        {
            ScrollFrame = frame;
        }
        else if (x < 0)
        {
            ScrollFrame = frame + 1 - VisibleFrames;
        }
        else
        {
            return false;
        }

        // Paging is not clamped to the extent: the playhead may sit past the sequence end (End key).
        ScrollFrame = Math.Max(0, ScrollFrame);
        if (old == ScrollFrame)
        {
            return false;
        }

        Raise();
        return true;
    }

    /// <summary>The height of a track (its default until resized).</summary>
    /// <param name="trackId">The track id.</param>
    /// <param name="kind">The track kind (for the default).</param>
    /// <returns>The height in pixels.</returns>
    public double TrackHeight(string trackId, TrackKind kind) =>
        _heights.TryGetValue(trackId, out var h) ? h : kind == TrackKind.Video ? DefaultVideoTrackHeight : DefaultAudioTrackHeight;

    /// <summary>Sets a track height (clamped to 32 to 160).</summary>
    /// <param name="trackId">The track id.</param>
    /// <param name="height">The height in pixels.</param>
    public void SetTrackHeight(string trackId, double height)
    {
        var clamped = Math.Clamp(height, MinTrackHeight, MaxTrackHeight);
        if (_heights.TryGetValue(trackId, out var old) && old == clamped)
        {
            return;
        }

        _heights[trackId] = clamped;
        Raise();
    }

    /// <summary>
    /// Lays out the track rows: video tracks from the top down to V1, the divider, then A1 downwards. When everything
    /// fits, the block shifts down so the divider sits at the middle of the viewport where possible.
    /// </summary>
    /// <param name="sequence">The sequence.</param>
    /// <returns>The layout.</returns>
    public TrackLayout Layout(Sequence sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        var video = sequence.Tracks.Where(t => t.Kind == TrackKind.Video).ToList();
        var audio = sequence.Tracks.Where(t => t.Kind == TrackKind.Audio).ToList();
        var videoHeight = video.Sum(t => TrackHeight(t.Id, t.Kind));
        var audioHeight = audio.Sum(t => TrackHeight(t.Id, t.Kind));
        var total = videoHeight + DividerHeight + audioHeight;
        var offset = 0.0;
        if (ViewportHeight > total)
        {
            offset = Math.Clamp((ViewportHeight / 2) - videoHeight - (DividerHeight / 2), 0, ViewportHeight - total);
        }

        var rows = new List<TrackRow>(video.Count + audio.Count);
        var y = offset;
        for (var i = video.Count - 1; i >= 0; i--)
        {
            var h = TrackHeight(video[i].Id, TrackKind.Video);
            rows.Add(new TrackRow(video[i].Id, TrackKind.Video, i, y, h));
            y += h;
        }

        var divider = y;
        y += DividerHeight;
        for (var i = 0; i < audio.Count; i++)
        {
            var h = TrackHeight(audio[i].Id, TrackKind.Audio);
            rows.Add(new TrackRow(audio[i].Id, TrackKind.Audio, i, y, h));
            y += h;
        }

        // The content height excludes the centering offset, which only exists when everything fits anyway.
        return new TrackLayout(rows, divider, total);
    }

    /// <summary>
    /// Chooses ruler tick spacing (DESIGN 5.2: 1 frame up to 5 minutes and beyond) so labels are at least
    /// <see cref="MinMajorTickPixels"/> apart.
    /// </summary>
    /// <param name="timecodeBase">Frames per timecode second (30 for 29.97).</param>
    /// <returns>The major and minor steps in frames.</returns>
    public RulerSteps ChooseRulerSteps(int timecodeBase)
    {
        long fb = Math.Max(1, timecodeBase);

        // Below one second only divisors of the timecode base, so labels land on round frame numbers.
        var candidates = new List<long>();
        for (long d = 1; d < fb; d++)
        {
            if (fb % d == 0)
            {
                candidates.Add(d);
            }
        }

        long[] seconds = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600];
        candidates.AddRange(seconds.Select(s => s * fb));
        var major = candidates[^1];
        foreach (var c in candidates)
        {
            if (c * PixelsPerFrame >= MinMajorTickPixels)
            {
                major = c;
                break;
            }
        }

        var minor = major;
        foreach (var divisions in new[] { 10, 5, 4, 2 })
        {
            if (major % divisions == 0 && major / divisions * PixelsPerFrame >= MinMinorTickPixels)
            {
                minor = major / divisions;
                break;
            }
        }

        return new RulerSteps(major, minor);
    }

    private void ClampScroll()
    {
        ScrollFrame = Math.Clamp(ScrollFrame, 0, MaxScrollFrame);
        ScrollY = Math.Clamp(ScrollY, 0, MaxScrollY);
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
