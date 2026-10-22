using System.Collections.Immutable;
using System.Globalization;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Core.Editing;

/// <summary>Placing and moving clips: drops, paste, three-point edits, moves, nudges and sync.</summary>
public static partial class TimelineEdits
{
    /// <summary>
    /// Places clips at <paramref name="at"/> (drops, paste, three-point edits). Each placement's clip start is an offset
    /// from <paramref name="at"/>. Overwrite trims, splits or removes covered clips; Insert first opens room on every
    /// unlocked track. Placed clips get new ids, and placeholder link groups become new link groups.
    /// </summary>
    /// <remarks>
    /// The first clip with a video stream placed into a never-used sequence sets the sequence size (rounded to even) and
    /// rate (nearest supported), converting the placement by time; a warning reports it for the toast. Placements on
    /// locked or missing tracks, or of the wrong kind, are skipped with a warning. Durations clamp to the source.
    /// </remarks>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="clips">The clips to place.</param>
    /// <param name="at">The placement frame.</param>
    /// <param name="mode">Overwrite or insert.</param>
    /// <returns>The result; <see cref="EditResult.Select"/> holds the new clip ids.</returns>
    public static EditResult Place(IIdGenerator ids, Project project, IReadOnlyList<ClipPlacement> clips, long at, EditMode mode)
    {
        var label = mode == EditMode.Insert ? "Insert" : "Overwrite";
        if (clips.Count == 0)
        {
            return EditResult.Unchanged(project, label);
        }

        var w = new Work(project, ids);
        if (at < 0)
        {
            w.Warn("Clips cannot start before frame 0.");
            at = 0;
        }

        var placements = clips.ToList();
        if (!project.Sequence.HasHadClips)
        {
            at = AdoptSettings(w, placements, at);
        }

        var items = new List<(TrackWork Track, Clip Clip)>();
        foreach (var placement in placements)
        {
            var track = w.Track(placement.TrackId);
            if (track is null)
            {
                w.Warn("The target track no longer exists.");
                continue;
            }

            if (track.Locked)
            {
                w.Warn($"{track.Name} is locked.");
                continue;
            }

            if (!Fits(w, track, placement.Clip, out var why))
            {
                w.Warn(why);
                continue;
            }

            var clip = placement.Clip;
            items.Add((track, clip with { Start = at + clip.Start, Duration = Math.Max(1, clip.Duration) }));
        }

        if (items.Count == 0)
        {
            return EditResult.Unchanged(project, label, [.. w.Warnings]);
        }

        var minStart = items.Min(i => i.Clip.Start);
        if (minStart < 0)
        {
            items = [.. items.Select(i => (i.Track, i.Clip with { Start = i.Clip.Start - minStart }))];
        }

        var linkCounts = items.Where(i => i.Clip.LinkId is not null && i.Clip is not TitleClip)
            .GroupBy(i => i.Clip.LinkId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        var placed = new List<(TrackWork Track, Clip Clip)>();
        foreach (var (track, original) in items)
        {
            var clip = ClampPlacedToSource(w, original);
            if (clip is null)
            {
                continue;
            }

            string? link = null;
            if (clip is not TitleClip && clip.LinkId is { } placeholder && linkCounts.GetValueOrDefault(placeholder) >= 2)
            {
                if (!links.TryGetValue(placeholder, out link))
                {
                    link = ids.NewId(IIdGenerator.LinkPrefix);
                    links[placeholder] = link;
                }
            }

            placed.Add((track, SanitizeClip(clip with { Id = ids.NewId(IIdGenerator.ClipPrefix), LinkId = link })));
        }

        if (placed.Count == 0)
        {
            return EditResult.Unchanged(project, label, [.. w.Warnings]);
        }

        if (mode == EditMode.Insert)
        {
            var point = placed.Min(p => p.Clip.Start);
            var span = placed.Max(p => p.Clip.End) - point;
            w.InsertShift(point, span);
        }

        foreach (var (track, clip) in placed)
        {
            w.ClearRange(track, clip.Start, clip.End);
        }

        foreach (var (track, clip) in placed)
        {
            track.Add(clip);
        }

        return w.Finish(label, placed.Select(p => p.Clip.Id));
    }

    /// <summary>
    /// Builds placements for a media source drop or a three-point edit: a video clip on <paramref name="videoTrackId"/>
    /// and a linked audio clip on <paramref name="audioTrackId"/> (either may be null, and streams the media lacks are
    /// skipped). Clip ids and the link id are placeholders that <see cref="Place"/> replaces.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="mediaId">The media to place.</param>
    /// <param name="sourceInFlicks">The source in point (ignored for stills).</param>
    /// <param name="durationFrames">The clip length in timeline frames.</param>
    /// <param name="videoTrackId">The video target, or null.</param>
    /// <param name="audioTrackId">The audio target, or null.</param>
    /// <returns>The placements (empty when the media is missing or nothing applies).</returns>
    public static ImmutableArray<ClipPlacement> SourcePlacements(
        Project project, string mediaId, long sourceInFlicks, long durationFrames, string? videoTrackId, string? audioTrackId)
    {
        var media = TimelineQuery.FindMedia(project, mediaId);
        if (media is null)
        {
            return ImmutableArray<ClipPlacement>.Empty;
        }

        var useVideo = videoTrackId is not null && media.Info.Video is not null;
        var useAudio = audioTrackId is not null && media.Info.Audio is not null;
        var link = useVideo && useAudio ? "l_source" : null;
        var source = media.Info.IsStill ? 0 : Math.Max(0, sourceInFlicks);
        var duration = Math.Max(1, durationFrames);
        var builder = ImmutableArray.CreateBuilder<ClipPlacement>();
        if (useVideo)
        {
            builder.Add(new ClipPlacement(videoTrackId!, new MediaClip(
                "c_source_v", 0, duration, true, link, mediaId, StreamKind.Video, source, 100, VideoProps.Default, AudioProps.Default)));
        }

        if (useAudio)
        {
            builder.Add(new ClipPlacement(audioTrackId!, new MediaClip(
                "c_source_a", 0, duration, true, link, mediaId, StreamKind.Audio, source, 100, VideoProps.Default, AudioProps.Default)));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// The timeline length of a source range (DESIGN 5.5): from <paramref name="sourceInFlicks"/> to the exclusive
    /// <paramref name="sourceOutFlicks"/> rounded up to whole sequence frames, or to the end of the media without an
    /// Out. Stills (and media without a known end) take <paramref name="stillDurationFrames"/>. Used by three-point
    /// edits and by Source monitor drags onto the timeline.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="mediaId">The media.</param>
    /// <param name="sourceInFlicks">The source In (negative values count as 0).</param>
    /// <param name="sourceOutFlicks">The source Out (exclusive), or null for the rest of the media.</param>
    /// <param name="stillDurationFrames">The default still length in frames.</param>
    /// <returns>The length in frames, or 0 when the media is missing or the range is empty.</returns>
    public static long SourceRangeFrames(Project project, string mediaId, long sourceInFlicks, long? sourceOutFlicks, long stillDurationFrames)
    {
        ArgumentNullException.ThrowIfNull(project);
        var media = TimelineQuery.FindMedia(project, mediaId);
        if (media is null)
        {
            return 0;
        }

        if (media.Info.IsStill)
        {
            return Math.Max(1, stillDurationFrames);
        }

        var rate = project.Sequence.Settings.Rate;
        var source = Math.Max(0, sourceInFlicks);
        if (sourceOutFlicks is { } outFlicks && outFlicks > source)
        {
            var span = outFlicks - source;
            return (long)(((Int128)span + rate.FlicksPerFrame - 1) / rate.FlicksPerFrame);
        }

        var probe = new MediaClip("c_probe", 0, 1, true, null, mediaId, StreamKind.Video, source, 100, VideoProps.Default, AudioProps.Default);
        var max = TimelineQuery.MaxFramesFrom(probe, media, rate, 100);
        return max >= TimelineQuery.Unbounded ? Math.Max(1, stillDurationFrames) : Math.Max(0, max);
    }

    /// <summary>
    /// A three-point edit (<c>,</c> insert and <c>.</c> overwrite, DESIGN 5.5): places the source In/Out range (or
    /// the whole media from the source In) on the topmost targeted unlocked video and audio tracks, at the timeline In
    /// if set, else at the playhead. With no source In/Out but a timeline In and Out, the timeline range sets the
    /// duration. Stills use <paramref name="stillDurationFrames"/> unless the timeline range sets it.
    /// </summary>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="mediaId">The source media.</param>
    /// <param name="sourceInFlicks">The source In, or null.</param>
    /// <param name="sourceOutFlicks">The source Out (exclusive), or null.</param>
    /// <param name="playhead">The playhead frame.</param>
    /// <param name="mode">Insert or overwrite.</param>
    /// <param name="stillDurationFrames">The default still length in frames.</param>
    /// <returns>The result of the placement.</returns>
    public static EditResult SourceEdit(
        IIdGenerator ids,
        Project project,
        string mediaId,
        long? sourceInFlicks,
        long? sourceOutFlicks,
        long playhead,
        EditMode mode,
        long stillDurationFrames)
    {
        var label = mode == EditMode.Insert ? "Insert" : "Overwrite";
        var media = TimelineQuery.FindMedia(project, mediaId);
        if (media is null)
        {
            return EditResult.Unchanged(project, label, "The source media is not in the project.");
        }

        // "Topmost" is visual: the highest targeted video track and the first targeted audio track below the divider.
        var video = media.Info.Video is null
            ? null
            : TimelineQuery.TracksOfKind(project, TrackKind.Video).LastOrDefault(t => t.Targeted && !t.Locked);
        var audio = media.Info.Audio is null
            ? null
            : TimelineQuery.TracksOfKind(project, TrackKind.Audio).FirstOrDefault(t => t.Targeted && !t.Locked);
        if (video is null && audio is null)
        {
            return EditResult.Unchanged(project, label, "No targeted, unlocked track can take this media.");
        }

        var seq = project.Sequence;
        var at = seq.InFrame ?? playhead;
        var source = media.Info.IsStill ? 0 : Math.Max(0, sourceInFlicks ?? 0);
        var timelineRange = seq.InFrame is { } i && seq.OutFrame is { } o && o > i ? o - i : (long?)null;
        long duration;
        if (sourceInFlicks is null && sourceOutFlicks is null && timelineRange is { } range)
        {
            duration = range;
        }
        else
        {
            duration = SourceRangeFrames(project, mediaId, source, sourceOutFlicks, stillDurationFrames);
        }

        if (duration < 1)
        {
            return EditResult.Unchanged(project, label, "The source range is empty.");
        }

        var placements = SourcePlacements(project, mediaId, source, duration, video?.Id, audio?.Id);
        return Place(ids, project, placements, at, mode);
    }

    /// <summary>
    /// Moves clips by <paramref name="deltaFrames"/> and <paramref name="deltaTracks"/> (DESIGN 5.5). Vertical moves
    /// stay within a track kind: the offset is a kind index offset applied to each kind separately and clamped to the
    /// existing tracks, so a linked partner follows by the same offset when that track exists. Overwrite clears the
    /// destination; insert opens room on every unlocked track. Clips on locked tracks do not move, and nothing moves
    /// onto a locked track or before frame 0. Transitions between two moved clips travel with them.
    /// </summary>
    /// <param name="ids">The id generator (overwrite and insert may split clips).</param>
    /// <param name="project">The project.</param>
    /// <param name="clipIds">The clips to move (include link partners for linked selection).</param>
    /// <param name="deltaFrames">The horizontal offset in frames.</param>
    /// <param name="deltaTracks">The kind index offset (positive is V2 from V1, or A2 from A1).</param>
    /// <param name="mode">Overwrite or insert.</param>
    /// <returns>The result; <see cref="EditResult.Select"/> holds the moved clip ids.</returns>
    public static EditResult Move(
        IIdGenerator ids, Project project, IReadOnlyCollection<string> clipIds, long deltaFrames, int deltaTracks, EditMode mode)
    {
        const string label = "Move";
        var w = new Work(project, ids);
        var moving = new List<(TrackWork Track, Clip Clip)>();
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

            moving.Add((found.Track, found.Track.Clips[found.Index]));
        }

        if (moving.Count == 0)
        {
            return EditResult.Unchanged(project, label, [.. w.Warnings]);
        }

        var minStart = moving.Min(m => m.Clip.Start);
        if (deltaFrames < -minStart)
        {
            w.Warn("Clips cannot move before frame 0.");
            deltaFrames = -minStart;
        }

        var targets = new Dictionary<TrackWork, TrackWork>();
        foreach (var kind in new[] { TrackKind.Video, TrackKind.Audio })
        {
            var kindTracks = w.OfKind(kind);
            var indices = moving.Where(m => m.Track.Kind == kind).Select(m => kindTracks.IndexOf(m.Track)).ToList();
            if (indices.Count == 0)
            {
                continue;
            }

            var offset = Math.Clamp(deltaTracks, -indices.Min(), kindTracks.Count - 1 - indices.Max());
            if (offset != 0 && indices.Any(i => kindTracks[i + offset].Locked))
            {
                w.Warn("Clips cannot move onto a locked track.");
                offset = 0;
            }

            foreach (var i in indices.Distinct())
            {
                targets[kindTracks[i]] = kindTracks[i + offset];
            }
        }

        if (deltaFrames == 0 && targets.All(t => ReferenceEquals(t.Key, t.Value)))
        {
            return EditResult.Unchanged(project, label, [.. w.Warnings]);
        }

        var movingIds = moving.Select(m => m.Clip.Id).ToHashSet(StringComparer.Ordinal);
        var carried = new List<(TrackWork Track, Transition Transition)>();
        foreach (var source in moving.Select(m => m.Track).Distinct())
        {
            foreach (var tr in source.Transitions.Where(t => movingIds.Contains(t.LeftClipId) && movingIds.Contains(t.RightClipId)).ToList())
            {
                carried.Add((targets[source], tr));
                source.Transitions.Remove(tr);
            }
        }

        foreach (var (track, clip) in moving)
        {
            track.Clips.RemoveAt(track.IndexOf(clip.Id));
        }

        var placed = moving.Select(m => (Track: targets[m.Track], Clip: m.Clip with { Start = m.Clip.Start + deltaFrames })).ToList();
        if (mode == EditMode.Insert)
        {
            var point = placed.Min(p => p.Clip.Start);
            w.InsertShift(point, placed.Max(p => p.Clip.End) - point);
        }

        foreach (var (track, clip) in placed)
        {
            w.ClearRange(track, clip.Start, clip.End);
        }

        foreach (var (track, clip) in placed)
        {
            track.Add(clip);
        }

        foreach (var (track, transition) in carried)
        {
            track.Transitions.Add(transition);
        }

        return w.Finish(label, movingIds);
    }

    /// <summary>Nudges clips horizontally (Alt+Left/Right) in overwrite mode.</summary>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="clipIds">The clips to nudge.</param>
    /// <param name="deltaFrames">The offset in frames.</param>
    /// <returns>The result, labelled "Nudge".</returns>
    public static EditResult Nudge(IIdGenerator ids, Project project, IReadOnlyCollection<string> clipIds, long deltaFrames) =>
        Move(ids, project, clipIds, deltaFrames, 0, EditMode.Overwrite) with { Label = "Nudge" };

    /// <summary>
    /// "Move into sync" from a sync badge: moves the clip by its <see cref="TimelineQuery.SyncOffset"/> so it lines up
    /// with the partner playing the other stream of the same media (overwrite).
    /// </summary>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="clipId">The drifted clip.</param>
    /// <returns>The result, labelled "Move into Sync".</returns>
    public static EditResult MoveIntoSync(IIdGenerator ids, Project project, string clipId)
    {
        const string label = "Move into Sync";
        var offset = TimelineQuery.SyncOffset(project, clipId);
        if (offset is null or 0)
        {
            return EditResult.Unchanged(project, label);
        }

        return Move(ids, project, [clipId], offset.Value, 0, EditMode.Overwrite) with { Label = label };
    }

    private static bool Fits(Work w, TrackWork track, Clip clip, out string why)
    {
        why = string.Empty;
        switch (clip)
        {
            case TitleClip when track.Kind != TrackKind.Video:
                why = "Titles go on video tracks.";
                return false;
            case MediaClip m:
                var expected = track.Kind == TrackKind.Video ? StreamKind.Video : StreamKind.Audio;
                if (m.Stream != expected)
                {
                    why = $"{(m.Stream == StreamKind.Video ? "Video" : "Audio")} clips cannot go on {track.Name}.";
                    return false;
                }

                var media = w.MediaOf(m);
                if (media is null)
                {
                    why = "The clip's media is not in the project.";
                    return false;
                }

                if ((m.Stream == StreamKind.Video && media.Info.Video is null) || (m.Stream == StreamKind.Audio && media.Info.Audio is null))
                {
                    why = $"{w.Name(m)} has no {(m.Stream == StreamKind.Video ? "video" : "audio")} stream.";
                    return false;
                }

                return true;
            default:
                return true;
        }
    }

    private static Clip? ClampPlacedToSource(Work w, Clip clip)
    {
        if (clip is not MediaClip m)
        {
            return clip;
        }

        if (w.IsStill(m))
        {
            return m.SourceInFlicks == 0 ? m : m with { SourceInFlicks = 0 };
        }

        if (m.SourceInFlicks < 0)
        {
            m = m with { SourceInFlicks = 0 };
        }

        var media = w.MediaOf(m)!;
        var max = TimelineQuery.MaxFramesFrom(m, media, w.Rate, m.SpeedPercent);
        if (max < 1)
        {
            w.Warn($"Nothing of {w.Name(m)} is left after the source In.");
            return null;
        }

        if (m.Duration > max)
        {
            w.Warn($"Shortened to {max} frames: not enough source in {w.Name(m)}.");
            return m with { Duration = max };
        }

        return m;
    }

    /// <summary>
    /// For a never-used sequence: adopts the size and rate of the first placed video media and converts the placements
    /// and the placement frame by time. Returns the (converted) placement frame.
    /// </summary>
    private static long AdoptSettings(Work w, List<ClipPlacement> placements, long at)
    {
        foreach (var placement in placements)
        {
            if (placement.Clip is not MediaClip { Stream: StreamKind.Video } m || w.MediaOf(m) is not { Info.Video: { } info } media)
            {
                continue;
            }

            var width = EvenSize(info.DisplayWidth > 0 ? info.DisplayWidth : info.Width);
            var height = EvenSize(info.DisplayHeight > 0 ? info.DisplayHeight : info.Height);
            var rate = w.Rate;
            if (!media.Info.IsStill)
            {
                var measured = info.AvgFrameRate.IsPositive ? info.AvgFrameRate : info.RFrameRate;
                rate = measured.IsPositive ? FrameRate.Nearest(measured) : rate;
            }

            var settings = new SequenceSettings(width, height, rate);
            if (settings == w.Settings)
            {
                return at;
            }

            var oldRate = w.Rate;
            ConvertTimes(w, oldRate, rate);
            w.Settings = settings;
            if (oldRate != rate)
            {
                for (var i = 0; i < placements.Count; i++)
                {
                    var c = placements[i].Clip;
                    var start = ConvertFrame(c.Start, oldRate, rate);
                    var end = Math.Max(start + 1, ConvertFrame(c.End, oldRate, rate));
                    placements[i] = placements[i] with { Clip = ScaleClipTimes(c with { Start = start, Duration = end - start }, oldRate, rate) };
                }

                at = ConvertFrame(at, oldRate, rate);
            }

            w.Warn(string.Create(
                CultureInfo.InvariantCulture,
                $"Sequence set to {width}x{height} at {rate.DisplayName} fps to match {w.Name(m)}."));
            return at;
        }

        return at;
    }

    private static int EvenSize(int value) => Math.Clamp(value + (value & 1), 16, 16384);
}
