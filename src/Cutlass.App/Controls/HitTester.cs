using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls;

/// <summary>
/// Maps a control point to a <see cref="HitResult"/> with the precedence of DESIGN 5.4: keyframe dot (selected audio
/// clips), fade handle, transition band (its ends resize it), edge, volume line (selected audio clips), body. In the
/// ruler, marker heads take precedence over scrubbing. Pure: no Avalonia types, unit tested.
/// </summary>
public static class HitTester
{
    /// <summary>The edge zone width inside each clip end.</summary>
    public const double EdgeWidth = 6;

    /// <summary>Clips narrower than this split into thirds (start edge, body, end edge).</summary>
    public const double NarrowClipWidth = 18;

    /// <summary>The fade handle size.</summary>
    public const double FadeHandleSize = 8;

    /// <summary>Fade handles exist only on clips at least this wide.</summary>
    public const double FadeHandleMinClipWidth = 40;

    /// <summary>Fade handles exist only on tracks at least this tall.</summary>
    public const double FadeHandleMinTrackHeight = 40;

    /// <summary>The transition band's share of the track height.</summary>
    public const double TransitionBandShare = 0.4;

    /// <summary>A transition band is at least this wide on each side of its cut.</summary>
    public const double TransitionMinHalfWidth = 4;

    /// <summary>The playhead grab distance in pixels.</summary>
    public const double PlayheadGrab = 3;

    /// <summary>The height of the header resize zone at the bottom of a row.</summary>
    public const double ResizeGrab = 4;

    /// <summary>The grab width of each end of a transition band.</summary>
    public const double TransitionEdgeGrab = 4;

    /// <summary>Transition bands narrower than this have no resizable ends (the whole band selects).</summary>
    public const double TransitionMinEdgeBand = 24;

    /// <summary>The top of the marker heads in the ruler.</summary>
    public const double MarkerTop = 12;

    /// <summary>The half width of a marker head's grab area.</summary>
    public const double MarkerGrab = 5;

    private static readonly HeaderButton[] VideoButtons =
    [
        new(HitZone.HeaderTarget, 6, 6, 28, 18),
        new(HitZone.HeaderVisibility, 102, 5, 20, 20),
        new(HitZone.HeaderLock, 124, 5, 20, 20),
    ];

    private static readonly HeaderButton[] AudioButtons =
    [
        new(HitZone.HeaderTarget, 6, 6, 28, 18),
        new(HitZone.HeaderMute, 80, 5, 20, 20),
        new(HitZone.HeaderSolo, 102, 5, 20, 20),
        new(HitZone.HeaderLock, 124, 5, 20, 20),
    ];

    /// <summary>The header buttons of a track kind (the renderer draws them at the same places).</summary>
    /// <param name="kind">The track kind.</param>
    /// <returns>The buttons.</returns>
    public static IReadOnlyList<HeaderButton> HeaderButtons(TrackKind kind) => kind == TrackKind.Video ? VideoButtons : AudioButtons;

    /// <summary>The fade lengths of a clip (title fades, or the video or audio fades of a media clip).</summary>
    /// <param name="clip">The clip.</param>
    /// <returns>The fade-in and fade-out lengths in frames.</returns>
    public static (long In, long Out) Fades(Clip clip) => clip switch
    {
        TitleClip t => (t.FadeInFrames, t.FadeOutFrames),
        MediaClip { Stream: StreamKind.Video } m => (m.Video.FadeInFrames, m.Video.FadeOutFrames),
        MediaClip m => (m.Audio.FadeInFrames, m.Audio.FadeOutFrames),
        _ => (0, 0),
    };

    /// <summary>
    /// The fade handle squares of a clip in lane coordinates (x, y relative to the row top), or null when the clip or
    /// track is too small for them.
    /// </summary>
    /// <param name="left">The clip's left x.</param>
    /// <param name="right">The clip's right x.</param>
    /// <param name="trackHeight">The row height.</param>
    /// <param name="fadeInPx">The fade-in length in pixels.</param>
    /// <param name="fadeOutPx">The fade-out length in pixels.</param>
    /// <returns>The left x of the fade-in and fade-out squares (both at y 1), or null.</returns>
    public static (double InX, double OutX)? FadeHandles(double left, double right, double trackHeight, double fadeInPx, double fadeOutPx)
    {
        if (right - left < FadeHandleMinClipWidth || trackHeight < FadeHandleMinTrackHeight)
        {
            return null;
        }

        var inX = Math.Clamp(left + fadeInPx - (FadeHandleSize / 2), left, right - FadeHandleSize);
        var outX = Math.Clamp(right - fadeOutPx - (FadeHandleSize / 2), left, right - FadeHandleSize);
        if (fadeInPx <= 0)
        {
            inX = left;
        }

        if (fadeOutPx <= 0)
        {
            outX = right - FadeHandleSize;
        }

        return (inX, outX);
    }

    /// <summary>The transition band of a transition in lane x coordinates.</summary>
    /// <param name="view">The view state.</param>
    /// <param name="cut">The cut frame.</param>
    /// <param name="duration">The transition duration.</param>
    /// <returns>The left and right x.</returns>
    public static (double Left, double Right) TransitionSpan(TimelineViewState view, long cut, long duration)
    {
        ArgumentNullException.ThrowIfNull(view);
        var (a, b) = TimelineQuery.TransitionHalves(duration);
        var cutX = view.FrameToX(cut);
        var left = Math.Min(view.FrameToX(cut - a), cutX - TransitionMinHalfWidth);
        var right = Math.Max(view.FrameToX(cut + b), cutX + TransitionMinHalfWidth);
        return (left, right);
    }

    /// <summary>The x of a volume keyframe dot in lane coordinates (keys sit at the centre of their frame).</summary>
    /// <param name="view">The view state.</param>
    /// <param name="clip">The clip.</param>
    /// <param name="key">The key.</param>
    /// <returns>The lane x.</returns>
    public static double KeyX(TimelineViewState view, Clip clip, VolumeKey key)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(key);
        return view.FrameToX(clip.Start + key.Frame + 0.5);
    }

    /// <summary>
    /// True when a clip shows its keyframe dots and volume line as interactive: a selected audio media clip on an
    /// unlocked track.
    /// </summary>
    /// <param name="track">The clip's track.</param>
    /// <param name="clip">The clip.</param>
    /// <param name="selectedClipIds">The selection, or null.</param>
    /// <returns>True when editable.</returns>
    public static bool HasEditableVolume(Track track, Clip clip, IReadOnlySet<string>? selectedClipIds)
    {
        ArgumentNullException.ThrowIfNull(track);
        return clip is MediaClip { Stream: StreamKind.Audio } && !track.Locked && selectedClipIds is not null && selectedClipIds.Contains(clip.Id);
    }

    /// <summary>Hit tests a point of the control.</summary>
    /// <param name="project">The project as drawn.</param>
    /// <param name="view">The view state.</param>
    /// <param name="layout">The track layout for the project.</param>
    /// <param name="x">The x in control pixels.</param>
    /// <param name="y">The y in control pixels.</param>
    /// <param name="playhead">The playhead frame.</param>
    /// <param name="selectedClipIds">The selected clips (keyframe dots and the volume line exist only on selected audio clips), or null.</param>
    /// <returns>The hit.</returns>
    public static HitResult Hit(
        Project project, TimelineViewState view, TrackLayout layout, double x, double y, long playhead, IReadOnlySet<string>? selectedClipIds = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(layout);
        var lx = x - TimelineViewState.HeaderWidth;
        var frame = view.XToFrame(lx);
        if (y < TimelineViewState.RulerHeight)
        {
            if (x < TimelineViewState.HeaderWidth)
            {
                return HitResult.Nothing;
            }

            if (y >= MarkerTop)
            {
                // The nearest marker head under the pointer wins.
                Marker? best = null;
                var bestDistance = double.MaxValue;
                foreach (var marker in project.Sequence.Markers)
                {
                    var distance = Math.Abs(lx - view.FrameToX(marker.Frame));
                    if (distance <= MarkerGrab && distance < bestDistance)
                    {
                        best = marker;
                        bestDistance = distance;
                    }
                }

                if (best is not null)
                {
                    return new HitResult(HitZone.Marker, null, null, null, frame, MarkerId: best.Id);
                }
            }

            return new HitResult(HitZone.Ruler, null, null, null, frame);
        }

        var contentY = view.ToContentY(y);
        var row = layout.RowAt(contentY);
        if (x < TimelineViewState.HeaderWidth)
        {
            return row is null ? HitResult.Nothing : HitHeader(row, x, contentY, frame);
        }

        if (row is null)
        {
            return new HitResult(HitZone.Lane, null, null, null, frame);
        }

        var track = TimelineQuery.FindTrack(project, row.TrackId);
        if (track is null)
        {
            return new HitResult(HitZone.Lane, row.TrackId, null, null, frame);
        }

        var yInRow = contentY - row.Top;
        Clip? clip = null;
        double left = 0, right = 0;
        foreach (var c in track.Clips)
        {
            var l = view.FrameToX(c.Start);
            var r = view.FrameToX(c.End);
            if (lx >= l && lx < r)
            {
                clip = c;
                left = l;
                right = r;
                break;
            }
        }

        var editableVolume = clip is not null && HasEditableVolume(track, clip, selectedClipIds);
        var clipHeight = row.Height - 2;

        // 1. Keyframe dots (selected audio clips).
        if (editableVolume && clip is MediaClip audio && !audio.Audio.Keys.IsDefaultOrEmpty)
        {
            for (var i = 0; i < audio.Audio.Keys.Length; i++)
            {
                var key = audio.Audio.Keys[i];
                var kx = KeyX(view, audio, key);
                var ky = VolumeScale.DbToY(audio.Audio.GainDb + key.Db, 1, clipHeight);
                if (Math.Abs(lx - kx) <= VolumeScale.DotRadius + 1 && Math.Abs(yInRow - ky) <= VolumeScale.DotRadius + 1)
                {
                    return new HitResult(HitZone.Keyframe, track.Id, audio.Id, null, frame, KeyIndex: i);
                }
            }
        }

        // 2. Fade handles.
        if (clip is not null)
        {
            var (fadeIn, fadeOut) = Fades(clip);
            var handles = FadeHandles(left, right, row.Height, fadeIn * view.PixelsPerFrame, fadeOut * view.PixelsPerFrame);
            if (handles is { } h && yInRow >= 1 && yInRow < 1 + FadeHandleSize)
            {
                if (lx >= h.InX && lx < h.InX + FadeHandleSize)
                {
                    return new HitResult(HitZone.FadeInHandle, track.Id, clip.Id, null, frame);
                }

                if (lx >= h.OutX && lx < h.OutX + FadeHandleSize)
                {
                    return new HitResult(HitZone.FadeOutHandle, track.Id, clip.Id, null, frame);
                }
            }
        }

        // 3. Transition bands.
        var bandTop = row.Height * (1 - TransitionBandShare) / 2;
        if (yInRow >= bandTop && yInRow < bandTop + (row.Height * TransitionBandShare))
        {
            foreach (var transition in track.Transitions)
            {
                if (TimelineQuery.CutFrame(track, transition) is not { } cut)
                {
                    continue;
                }

                var (tl, tr) = TransitionSpan(view, cut, transition.Duration);
                if (lx >= tl && lx < tr)
                {
                    var zone = HitZone.Transition;
                    if (tr - tl >= TransitionMinEdgeBand)
                    {
                        zone = lx < tl + TransitionEdgeGrab ? HitZone.TransitionStart : lx >= tr - TransitionEdgeGrab ? HitZone.TransitionEnd : zone;
                    }

                    return new HitResult(zone, track.Id, null, transition.Id, frame);
                }
            }
        }

        // 4. Edges, 6. body.
        if (clip is not null)
        {
            var width = right - left;
            var edge = width < NarrowClipWidth ? width / 3 : EdgeWidth;
            var zone = lx < left + edge ? HitZone.ClipStart : lx >= right - edge ? HitZone.ClipEnd : HitZone.ClipBody;

            // 5. The volume line (selected audio clips).
            if (zone == HitZone.ClipBody && editableVolume && clip is MediaClip lineClip)
            {
                var lineY = VolumeScale.DbToY(VolumeScale.LevelAt(lineClip, frame - clip.Start), 1, clipHeight);
                if (Math.Abs(yInRow - lineY) <= VolumeScale.LineGrab)
                {
                    zone = HitZone.VolumeLine;
                }
            }

            return new HitResult(zone, track.Id, clip.Id, null, frame);
        }

        if (Math.Abs(lx - view.FrameToX(playhead)) <= PlayheadGrab)
        {
            return new HitResult(HitZone.Playhead, track.Id, null, null, frame);
        }

        if (TimelineQuery.GapAt(track, (long)Math.Floor(frame)) is { } gap)
        {
            return new HitResult(HitZone.Gap, track.Id, null, null, frame, gap.Start, gap.End);
        }

        return new HitResult(HitZone.Lane, track.Id, null, null, frame);
    }

    private static HitResult HitHeader(TrackRow row, double x, double contentY, double frame)
    {
        var yInRow = contentY - row.Top;
        if (yInRow >= row.Height - ResizeGrab)
        {
            return new HitResult(HitZone.HeaderResize, row.TrackId, null, null, frame);
        }

        foreach (var button in HeaderButtons(row.Kind))
        {
            if (button.Contains(x, yInRow))
            {
                return new HitResult(button.Zone, row.TrackId, null, null, frame);
            }
        }

        if (TrackGain.HasField(row.Kind == TrackKind.Audio, row.Height) && TrackGain.Contains(x, yInRow))
        {
            return new HitResult(HitZone.HeaderGain, row.TrackId, null, null, frame);
        }

        return new HitResult(HitZone.Header, row.TrackId, null, null, frame);
    }
}
