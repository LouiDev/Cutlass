using System.Collections.Immutable;
using System.Globalization;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Core.Editing;

/// <summary>Clip properties, speed, links, markers, In/Out, media and sequence settings.</summary>
public static partial class TimelineEdits
{
    /// <summary>
    /// Sets the speed of media clips (Ctrl+R), 25 to 400 percent. The source range is kept, so the duration becomes
    /// <c>round(Duration * oldSpeed / newSpeed)</c>, clamped to the source. With <paramref name="rippleFollowing"/>,
    /// later clips on the clip's track move by the length change, and other unlocked tracks ripple by the first clip's
    /// change; without it the new length stops at the next clip. Volume keyframes scale with the clip.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="clipIds">The clips (titles and stills are skipped).</param>
    /// <param name="percent">The new speed in percent.</param>
    /// <param name="rippleFollowing">True to ripple following clips (the dialog default).</param>
    /// <returns>The result.</returns>
    public static EditResult SetSpeed(Project project, IReadOnlyCollection<string> clipIds, int percent, bool rippleFollowing)
    {
        const string label = "Speed";
        var w = new Work(project);
        if (percent is < MinSpeedPercent or > MaxSpeedPercent)
        {
            w.Warn($"Speed is limited to {MinSpeedPercent} to {MaxSpeedPercent} percent.");
            percent = Math.Clamp(percent, MinSpeedPercent, MaxSpeedPercent);
        }

        var targets = new List<(TrackWork Track, MediaClip Clip)>();
        foreach (var id in clipIds.Distinct(StringComparer.Ordinal))
        {
            if (w.Find(id) is not { } found)
            {
                continue;
            }

            var clip = found.Track.Clips[found.Index];
            if (found.Track.Locked)
            {
                w.Warn($"{found.Track.Name} is locked.");
            }
            else if (clip is not MediaClip m || w.IsStill(m))
            {
                w.Warn("Speed applies to video and audio clips only.");
            }
            else if (m.SpeedPercent != percent)
            {
                targets.Add((found.Track, m));
            }
        }

        if (targets.Count == 0)
        {
            return EditResult.Unchanged(project, label, [.. w.Warnings]);
        }

        var primary = targets[0];
        long primaryDelta = 0;
        foreach (var (track, clip) in targets.OrderByDescending(t => t.Clip.Start))
        {
            var index = track.IndexOf(clip.Id);
            var current = (MediaClip)track.Clips[index];
            var oldDuration = current.Duration;
            var scaled = (Int128)oldDuration * current.SpeedPercent;
            var duration = Math.Max(1, (long)(((2 * scaled) + percent) / (2 * percent)));
            var max = TimelineQuery.MaxFramesFrom(current, w.MediaOf(current)!, w.Rate, percent);
            if (duration > max)
            {
                w.Warn($"Shortened to fit the source of {w.Name(current)}.");
                duration = Math.Max(1, max);
            }

            if (!rippleFollowing && index + 1 < track.Clips.Count && current.Start + duration > track.Clips[index + 1].Start)
            {
                w.Warn("Shortened to fit before the next clip.");
                duration = Math.Max(1, track.Clips[index + 1].Start - current.Start);
            }

            var keys = current.Audio.Keys.IsDefaultOrEmpty
                ? current.Audio.Keys
                : [.. current.Audio.Keys.Select(k => k with { Frame = (long)((Int128)k.Frame * duration / oldDuration) })];
            track.Clips[index] = SanitizeClip(current with
            {
                SpeedPercent = percent,
                Duration = duration,
                Audio = current.Audio with { Keys = keys },
            });
            if (rippleFollowing)
            {
                ShiftAfter(track, index, duration - oldDuration);
            }

            if (clip.Id == primary.Clip.Id)
            {
                primaryDelta = duration - oldDuration;
            }
        }

        if (rippleFollowing)
        {
            w.RippleOthers([.. targets.Select(t => t.Track)], primary.Clip.End, primaryDelta);
        }

        return w.Finish(label);
    }

    /// <summary>
    /// Applies an inspector change to clips as one edit (one undo step for several clips). The update may change any
    /// property except identity and placement: id, start, duration, link, media, stream, source in point and speed are
    /// restored (use the dedicated edits), and the result is sanitized (fades, keyframes, ranges).
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="clipIds">The clips.</param>
    /// <param name="update">Returns the changed clip; must keep the clip type.</param>
    /// <param name="label">The undo label, for example "Opacity".</param>
    /// <returns>The result.</returns>
    public static EditResult UpdateClips(Project project, IReadOnlyCollection<string> clipIds, Func<Clip, Clip> update, string label)
    {
        var w = new Work(project);
        foreach (var id in clipIds.Distinct(StringComparer.Ordinal))
        {
            if (w.Find(id) is not { } found)
            {
                continue;
            }

            if (found.Track.Locked)
            {
                w.Warn($"{found.Track.Name} is locked.");
                continue;
            }

            var clip = found.Track.Clips[found.Index];
            var updated = update(clip);
            if (updated is null || updated.GetType() != clip.GetType())
            {
                w.Warn("A clip cannot change its type.");
                continue;
            }

            var restored = updated with { Id = clip.Id, Start = clip.Start, Duration = clip.Duration, LinkId = clip.LinkId };
            if (restored is MediaClip rm && clip is MediaClip om)
            {
                restored = rm with
                {
                    MediaId = om.MediaId,
                    Stream = om.Stream,
                    SourceInFlicks = om.SourceInFlicks,
                    SpeedPercent = om.SpeedPercent,
                };
            }

            if (!restored.Equals(updated))
            {
                w.Warn("Position, length, links, source and speed change only through their own edits.");
            }

            found.Track.Clips[found.Index] = SanitizeClip(restored);
        }

        return w.Finish(label);
    }

    /// <summary>Enables or disables clips (Shift+E).</summary>
    /// <param name="project">The project.</param>
    /// <param name="clipIds">The clips.</param>
    /// <param name="enabled">The new state.</param>
    /// <returns>The result.</returns>
    public static EditResult SetEnabled(Project project, IReadOnlyCollection<string> clipIds, bool enabled) =>
        UpdateClips(project, clipIds, c => c with { Enabled = enabled }, enabled ? "Enable" : "Disable");

    /// <summary>Puts media clips into one new link group (Ctrl+L). Titles are never linked; at least two clips are needed.</summary>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="clipIds">The clips.</param>
    /// <returns>The result.</returns>
    public static EditResult Link(IIdGenerator ids, Project project, IReadOnlyCollection<string> clipIds)
    {
        const string label = "Link";
        var w = new Work(project, ids);
        var members = new List<(TrackWork Track, int Index)>();
        foreach (var id in clipIds.Distinct(StringComparer.Ordinal))
        {
            if (w.Find(id) is not { } found)
            {
                continue;
            }

            if (found.Track.Locked)
            {
                w.Warn($"{found.Track.Name} is locked.");
            }
            else if (found.Track.Clips[found.Index] is TitleClip)
            {
                w.Warn("Titles cannot be linked.");
            }
            else
            {
                members.Add(found);
            }
        }

        if (members.Count < 2)
        {
            return EditResult.Unchanged(project, label, [.. w.Warnings, "Select at least two clips to link."]);
        }

        var link = ids.NewId(IIdGenerator.LinkPrefix);
        foreach (var (track, index) in members)
        {
            track.Clips[index] = track.Clips[index] with { LinkId = link };
        }

        return w.Finish(label);
    }

    /// <summary>Removes clips from their link groups. A group left with a single member is dissolved.</summary>
    /// <param name="project">The project.</param>
    /// <param name="clipIds">The clips.</param>
    /// <returns>The result.</returns>
    public static EditResult Unlink(Project project, IReadOnlyCollection<string> clipIds)
    {
        const string label = "Unlink";
        var w = new Work(project);
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in clipIds.Distinct(StringComparer.Ordinal))
        {
            if (w.Find(id) is not { } found || found.Track.Clips[found.Index].LinkId is not { } link)
            {
                continue;
            }

            if (found.Track.Locked)
            {
                w.Warn($"{found.Track.Name} is locked.");
                continue;
            }

            groups.Add(link);
            found.Track.Clips[found.Index] = found.Track.Clips[found.Index] with { LinkId = null };
        }

        foreach (var link in groups)
        {
            var left = w.Tracks.SelectMany(t => t.Clips.Select((c, i) => (Track: t, Index: i, Clip: c)))
                .Where(x => x.Clip.LinkId == link).ToList();
            if (left.Count == 1 && !left[0].Track.Locked)
            {
                left[0].Track.Clips[left[0].Index] = left[0].Clip with { LinkId = null };
            }
        }

        return w.Finish(label);
    }

    /// <summary>Adds a marker (M) at a frame (clamped to 0 or later).</summary>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="frame">The frame.</param>
    /// <param name="name">The label.</param>
    /// <param name="color">The colour as <c>#RRGGBB</c>; invalid values use the default.</param>
    /// <returns>The result; <see cref="EditResult.Select"/> holds the marker id.</returns>
    public static EditResult AddMarker(IIdGenerator ids, Project project, long frame, string name = "", string color = DefaultMarkerColor)
    {
        var w = new Work(project, ids);
        var marker = new Marker(ids.NewId(IIdGenerator.MarkerPrefix), Math.Max(0, frame), name ?? string.Empty, ValidColor(color) ? color : DefaultMarkerColor);
        w.Markers.Add(marker);
        return w.Finish("Add Marker", [marker.Id]);
    }

    /// <summary>Edits a marker (drag, rename, recolour). The id is kept, the frame clamps to 0, invalid colours are ignored.</summary>
    /// <param name="project">The project.</param>
    /// <param name="markerId">The marker.</param>
    /// <param name="update">Returns the changed marker.</param>
    /// <returns>The result.</returns>
    public static EditResult UpdateMarker(Project project, string markerId, Func<Marker, Marker> update)
    {
        const string label = "Edit Marker";
        var w = new Work(project);
        var index = w.Markers.FindIndex(m => m.Id == markerId);
        if (index < 0)
        {
            return EditResult.Unchanged(project, label, "The marker no longer exists.");
        }

        var old = w.Markers[index];
        var updated = update(old);
        w.Markers[index] = new Marker(
            old.Id,
            Math.Max(0, updated.Frame),
            updated.Name ?? string.Empty,
            ValidColor(updated.Color) ? updated.Color : old.Color);
        return w.Finish(label);
    }

    /// <summary>Moves a marker to a frame (clamped to 0 or later).</summary>
    /// <param name="project">The project.</param>
    /// <param name="markerId">The marker.</param>
    /// <param name="frame">The new frame.</param>
    /// <returns>The result.</returns>
    public static EditResult MoveMarker(Project project, string markerId, long frame) =>
        UpdateMarker(project, markerId, m => m with { Frame = frame }) with { Label = "Move Marker" };

    /// <summary>Removes markers.</summary>
    /// <param name="project">The project.</param>
    /// <param name="markerIds">The markers.</param>
    /// <returns>The result.</returns>
    public static EditResult RemoveMarkers(Project project, IReadOnlyCollection<string> markerIds)
    {
        var w = new Work(project);
        w.Markers.RemoveAll(m => markerIds.Contains(m.Id));
        return w.Finish("Remove Marker");
    }

    /// <summary>
    /// Sets the In and Out points (I, O, X, Ctrl+Shift+X clears both with nulls). Out is exclusive. In clamps to 0 or
    /// later, Out to 1 or later, and an Out at or before In moves to <c>In + 1</c>.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="inFrame">The In point, or null to clear.</param>
    /// <param name="outFrame">The Out point (exclusive), or null to clear.</param>
    /// <returns>The result.</returns>
    public static EditResult SetInOut(Project project, long? inFrame, long? outFrame)
    {
        var w = new Work(project);
        var i = inFrame is { } a ? Math.Max(0, a) : (long?)null;
        var o = outFrame is { } b ? Math.Max(1, b) : (long?)null;
        if (i is { } iv && o is { } ov && ov <= iv)
        {
            w.Warn("Out must be after In.");
            o = iv + 1;
        }

        w.InFrame = i;
        w.OutFrame = o;
        return w.Finish(inFrame is null && outFrame is null ? "Clear In/Out" : "Set In/Out");
    }

    /// <summary>
    /// Adds media assets, or replaces assets with the same id (re-probe, relink). Clips of a replaced asset are
    /// trimmed to fit its new length, and removed when the stream they play is gone.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="assets">The assets.</param>
    /// <returns>The result.</returns>
    public static EditResult ImportMedia(Project project, IReadOnlyCollection<MediaAsset> assets)
    {
        var w = new Work(project);
        var media = project.Media.ToList();
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            var index = media.FindIndex(m => m.Id == asset.Id);
            if (index < 0)
            {
                media.Add(asset);
            }
            else if (media[index] != asset)
            {
                media[index] = asset;
                replaced.Add(asset.Id);
            }
        }

        w.Media = [.. media];
        if (replaced.Count > 0)
        {
            w.ClampToSource(replaced);
        }

        return w.Finish(replaced.Count > 0 ? "Update Media" : "Import");
    }

    /// <summary>Removes media assets and every clip that uses them (on every track).</summary>
    /// <param name="project">The project.</param>
    /// <param name="mediaIds">The media ids.</param>
    /// <returns>The result.</returns>
    public static EditResult RemoveMedia(Project project, IReadOnlyCollection<string> mediaIds)
    {
        var w = new Work(project);
        w.Media = [.. project.Media.Where(m => !mediaIds.Contains(m.Id))];
        foreach (var track in w.Tracks)
        {
            // The bin owns media, so its clips go even from locked tracks; anything else would leave dangling clips.
            track.Clips.RemoveAll(c => c is MediaClip m && mediaIds.Contains(m.MediaId));
        }

        return w.Finish("Remove Media");
    }

    /// <summary>
    /// Changes the sequence size and rate. Sizes round up to even (16 to 16384) and unsupported rates snap to the
    /// nearest supported one. A rate change converts every position by time: clips, transitions, fades, keyframes,
    /// markers and In/Out; clips keep at least one frame and stay in order, and anything that no longer fits its
    /// source is trimmed.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="settings">The new settings.</param>
    /// <returns>The result.</returns>
    public static EditResult SetSequenceSettings(Project project, SequenceSettings settings)
    {
        var w = new Work(project);
        var rate = settings.Rate;
        if (!rate.IsSupported)
        {
            rate = rate.Value.IsPositive ? FrameRate.Nearest(rate.Value) : FrameRate.Fps30;
            w.Warn(string.Create(CultureInfo.InvariantCulture, $"Using the nearest supported rate, {rate.DisplayName} fps."));
        }

        var normalized = new SequenceSettings(EvenSize(settings.Width), EvenSize(settings.Height), rate);
        if (normalized.Width != settings.Width || normalized.Height != settings.Height)
        {
            w.Warn(string.Create(CultureInfo.InvariantCulture, $"Using {normalized.Width}x{normalized.Height}: sizes are even."));
        }

        if (normalized == project.Sequence.Settings)
        {
            return EditResult.Unchanged(project, "Sequence Settings", [.. w.Warnings]);
        }

        ConvertTimes(w, project.Sequence.Settings.Rate, rate);
        w.Settings = normalized;
        w.ClampToSource(null);
        return w.Finish("Sequence Settings");
    }

    private static bool ValidColor(string? color) =>
        color is { Length: 7 } && color[0] == '#' && IsHex(color.AsSpan(1));

    private static bool IsHex(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Converts every timeline position of the working state from one rate to another by time.</summary>
    private static void ConvertTimes(Work w, FrameRate from, FrameRate to)
    {
        if (from == to)
        {
            return;
        }

        foreach (var track in w.Tracks)
        {
            track.Sort();
            long previousEnd = 0;
            for (var i = 0; i < track.Clips.Count; i++)
            {
                var c = track.Clips[i];
                var start = Math.Max(ConvertFrame(c.Start, from, to), previousEnd);
                var end = Math.Max(ConvertFrame(c.End, from, to), start + 1);
                track.Clips[i] = ScaleClipTimes(c with { Start = start, Duration = end - start }, from, to);
                previousEnd = end;
            }

            track.Transitions = [.. track.Transitions.Select(t => t with { Duration = Math.Max(MinTransitionFrames, ConvertFrame(t.Duration, from, to)) })];
        }

        for (var i = 0; i < w.Markers.Count; i++)
        {
            w.Markers[i] = w.Markers[i] with { Frame = ConvertFrame(w.Markers[i].Frame, from, to) };
        }

        w.InFrame = w.InFrame is { } inFrame ? ConvertFrame(inFrame, from, to) : null;
        w.OutFrame = w.OutFrame is { } outFrame ? Math.Max(1, ConvertFrame(outFrame, from, to)) : null;
        if (w.InFrame is { } iv && w.OutFrame is { } ov && ov <= iv)
        {
            w.OutFrame = iv + 1;
        }
    }

    /// <summary>Scales a clip's fades and keyframes (clip-relative frames) from one rate to another.</summary>
    private static Clip ScaleClipTimes(Clip clip, FrameRate from, FrameRate to)
    {
        long F(long x) => ConvertFrame(x, from, to);
        var scaled = clip switch
        {
            MediaClip m => m with
            {
                Video = m.Video with { FadeInFrames = F(m.Video.FadeInFrames), FadeOutFrames = F(m.Video.FadeOutFrames) },
                Audio = m.Audio with
                {
                    FadeInFrames = F(m.Audio.FadeInFrames),
                    FadeOutFrames = F(m.Audio.FadeOutFrames),
                    Keys = m.Audio.Keys.IsDefaultOrEmpty ? m.Audio.Keys : [.. m.Audio.Keys.Select(k => k with { Frame = F(k.Frame) })],
                },
            },
            TitleClip t => t with { FadeInFrames = F(t.FadeInFrames), FadeOutFrames = F(t.FadeOutFrames) },
            _ => clip,
        };
        return SanitizeClip(scaled);
    }
}
