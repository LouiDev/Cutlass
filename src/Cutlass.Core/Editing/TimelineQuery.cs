using System.Collections.Immutable;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Core.Editing;

/// <summary>
/// Read-only questions about a project: lookups, clips at a frame, edit points, gaps, runs, link groups, source handles
/// and transition budgets. Everything here is pure; the edits, the validator, the compiler and the timeline control
/// share these definitions.
/// </summary>
public static class TimelineQuery
{
    /// <summary>The handle reported for material without a source limit (titles, stills, media of unknown length).</summary>
    public const long Unbounded = long.MaxValue / 4;

    /// <summary>Finds a track by id.</summary>
    /// <param name="project">The project.</param>
    /// <param name="trackId">The track id.</param>
    /// <returns>The track, or null.</returns>
    public static Track? FindTrack(Project project, string trackId)
    {
        foreach (var track in project.Sequence.Tracks)
        {
            if (track.Id == trackId)
            {
                return track;
            }
        }

        return null;
    }

    /// <summary>Finds a clip and its track by clip id.</summary>
    /// <param name="project">The project.</param>
    /// <param name="clipId">The clip id.</param>
    /// <returns>The track and clip, or null.</returns>
    public static (Track Track, Clip Clip)? FindClip(Project project, string clipId)
    {
        foreach (var track in project.Sequence.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.Id == clipId)
                {
                    return (track, clip);
                }
            }
        }

        return null;
    }

    /// <summary>Finds a transition and its track by transition id.</summary>
    /// <param name="project">The project.</param>
    /// <param name="transitionId">The transition id.</param>
    /// <returns>The track and transition, or null.</returns>
    public static (Track Track, Transition Transition)? FindTransition(Project project, string transitionId)
    {
        foreach (var track in project.Sequence.Tracks)
        {
            foreach (var transition in track.Transitions)
            {
                if (transition.Id == transitionId)
                {
                    return (track, transition);
                }
            }
        }

        return null;
    }

    /// <summary>Finds a media asset by id.</summary>
    /// <param name="project">The project.</param>
    /// <param name="mediaId">The media id.</param>
    /// <returns>The asset, or null.</returns>
    public static MediaAsset? FindMedia(Project project, string mediaId)
    {
        foreach (var media in project.Media)
        {
            if (media.Id == mediaId)
            {
                return media;
            }
        }

        return null;
    }

    /// <summary>The media asset a clip plays, or null for titles and dangling references.</summary>
    /// <param name="project">The project.</param>
    /// <param name="clip">The clip.</param>
    /// <returns>The asset, or null.</returns>
    public static MediaAsset? MediaOf(Project project, Clip clip) =>
        clip is MediaClip m ? FindMedia(project, m.MediaId) : null;

    /// <summary>The tracks of one kind in model order (V1 upward, or A1 downward).</summary>
    /// <param name="project">The project.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The tracks of that kind.</returns>
    public static ImmutableArray<Track> TracksOfKind(Project project, TrackKind kind) =>
        [.. project.Sequence.Tracks.Where(t => t.Kind == kind)];

    /// <summary>The index of a track among the tracks of its kind (V1 and A1 are 0), or -1 when not found.</summary>
    /// <param name="project">The project.</param>
    /// <param name="trackId">The track id.</param>
    /// <returns>The kind index, or -1.</returns>
    public static int KindIndex(Project project, string trackId)
    {
        var track = FindTrack(project, trackId);
        if (track is null)
        {
            return -1;
        }

        var index = 0;
        foreach (var t in project.Sequence.Tracks)
        {
            if (t.Id == trackId)
            {
                return index;
            }

            if (t.Kind == track.Kind)
            {
                index++;
            }
        }

        return -1;
    }

    /// <summary>The clip of a track that covers <paramref name="frame"/> (<c>Start &lt;= frame &lt; End</c>), or null.</summary>
    /// <param name="track">The track.</param>
    /// <param name="frame">The timeline frame.</param>
    /// <returns>The clip, or null.</returns>
    public static Clip? ClipAt(Track track, long frame)
    {
        var clips = track.Clips;
        int lo = 0, hi = clips.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var c = clips[mid];
            if (frame < c.Start)
            {
                hi = mid - 1;
            }
            else if (frame >= c.End)
            {
                lo = mid + 1;
            }
            else
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>Every clip covering <paramref name="frame"/>, in track order.</summary>
    /// <param name="project">The project.</param>
    /// <param name="frame">The timeline frame.</param>
    /// <returns>The track and clip pairs.</returns>
    public static ImmutableArray<(Track Track, Clip Clip)> ClipsAt(Project project, long frame)
    {
        var builder = ImmutableArray.CreateBuilder<(Track, Clip)>();
        foreach (var track in project.Sequence.Tracks)
        {
            var clip = ClipAt(track, frame);
            if (clip is not null)
            {
                builder.Add((track, clip));
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// The sorted, distinct clip starts and ends on the given tracks (all tracks when <paramref name="trackIds"/> is
    /// null). Up and Down step through these.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="trackIds">The tracks to consider, or null for all.</param>
    /// <returns>The edit points in ascending order.</returns>
    public static ImmutableArray<long> EditPoints(Project project, IReadOnlyCollection<string>? trackIds = null)
    {
        var points = new SortedSet<long>();
        foreach (var track in project.Sequence.Tracks)
        {
            if (trackIds is not null && !trackIds.Contains(track.Id))
            {
                continue;
            }

            foreach (var clip in track.Clips)
            {
                points.Add(clip.Start);
                points.Add(clip.End);
            }
        }

        return [.. points];
    }

    /// <summary>The last edit point strictly before <paramref name="frame"/>, or null.</summary>
    /// <param name="project">The project.</param>
    /// <param name="frame">The current frame.</param>
    /// <param name="trackIds">The tracks to consider, or null for all.</param>
    /// <returns>The previous edit point, or null.</returns>
    public static long? PreviousEditPoint(Project project, long frame, IReadOnlyCollection<string>? trackIds = null)
    {
        long? best = null;
        foreach (var p in EditPoints(project, trackIds))
        {
            if (p < frame)
            {
                best = p;
            }
        }

        return best;
    }

    /// <summary>The first edit point strictly after <paramref name="frame"/>, or null.</summary>
    /// <param name="project">The project.</param>
    /// <param name="frame">The current frame.</param>
    /// <param name="trackIds">The tracks to consider, or null for all.</param>
    /// <returns>The next edit point, or null.</returns>
    public static long? NextEditPoint(Project project, long frame, IReadOnlyCollection<string>? trackIds = null)
    {
        foreach (var p in EditPoints(project, trackIds))
        {
            if (p > frame)
            {
                return p;
            }
        }

        return null;
    }

    /// <summary>The sequence length: the largest clip end on any track, or 0 when empty.</summary>
    /// <param name="project">The project.</param>
    /// <returns>The length in frames.</returns>
    public static long Duration(Project project)
    {
        long end = 0;
        foreach (var track in project.Sequence.Tracks)
        {
            if (track.Clips.Length > 0)
            {
                end = Math.Max(end, track.Clips[^1].End);
            }
        }

        return end;
    }

    /// <summary>
    /// The gaps of a track: the empty ranges before the first clip and between clips (not after the last one).
    /// </summary>
    /// <param name="track">The track.</param>
    /// <returns>The gaps as <c>[Start, End)</c> ranges in ascending order.</returns>
    public static ImmutableArray<(long Start, long End)> Gaps(Track track)
    {
        var builder = ImmutableArray.CreateBuilder<(long, long)>();
        long cursor = 0;
        foreach (var clip in track.Clips)
        {
            if (clip.Start > cursor)
            {
                builder.Add((cursor, clip.Start));
            }

            cursor = Math.Max(cursor, clip.End);
        }

        return builder.ToImmutable();
    }

    /// <summary>The gap of a track that contains <paramref name="frame"/>, or null.</summary>
    /// <param name="track">The track.</param>
    /// <param name="frame">The timeline frame.</param>
    /// <returns>The gap, or null when the frame is inside a clip or after the last clip.</returns>
    public static (long Start, long End)? GapAt(Track track, long frame)
    {
        foreach (var gap in Gaps(track))
        {
            if (frame >= gap.Start && frame < gap.End)
            {
                return gap;
            }
        }

        return null;
    }

    /// <summary>Maximal runs of butting, enabled clips on a track (TECH_BRIEF 7.4).</summary>
    /// <param name="track">The track.</param>
    /// <returns>The runs in timeline order; each run is non-empty.</returns>
    public static ImmutableArray<ImmutableArray<Clip>> Runs(Track track)
    {
        var runs = ImmutableArray.CreateBuilder<ImmutableArray<Clip>>();
        var current = ImmutableArray.CreateBuilder<Clip>();
        foreach (var clip in track.Clips)
        {
            if (!clip.Enabled)
            {
                if (current.Count > 0)
                {
                    runs.Add(current.ToImmutable());
                    current.Clear();
                }

                continue;
            }

            if (current.Count > 0 && current[^1].End != clip.Start)
            {
                runs.Add(current.ToImmutable());
                current.Clear();
            }

            current.Add(clip);
        }

        if (current.Count > 0)
        {
            runs.Add(current.ToImmutable());
        }

        return runs.ToImmutable();
    }

    /// <summary>The ids of every clip in the link group of <paramref name="clipId"/>, including itself.</summary>
    /// <param name="project">The project.</param>
    /// <param name="clipId">The clip id.</param>
    /// <returns>The group in track order; just the clip when it is unlinked; empty when it does not exist.</returns>
    public static ImmutableArray<string> LinkGroup(Project project, string clipId)
    {
        var found = FindClip(project, clipId);
        if (found is null)
        {
            return ImmutableArray<string>.Empty;
        }

        var link = found.Value.Clip.LinkId;
        if (link is null)
        {
            return [clipId];
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var track in project.Sequence.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (clip.LinkId == link)
                {
                    builder.Add(clip.Id);
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>Expands a set of clip ids by their link groups (linked selection).</summary>
    /// <param name="project">The project.</param>
    /// <param name="clipIds">The clip ids.</param>
    /// <returns>The ids plus every linked partner, without duplicates, in input order then track order.</returns>
    public static ImmutableArray<string> WithLinkedPartners(Project project, IEnumerable<string> clipIds)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in clipIds)
        {
            foreach (var member in LinkGroup(project, id))
            {
                if (seen.Add(member))
                {
                    result.Add(member);
                }
            }
        }

        return [.. result];
    }

    /// <summary>Filters ids (clips, transitions, markers, tracks, media) down to the ones that exist in the project.</summary>
    /// <param name="project">The project.</param>
    /// <param name="ids">The ids to filter, for example the selection after undo.</param>
    /// <returns>The existing ids in input order.</returns>
    public static ImmutableArray<string> ExistingIds(Project project, IEnumerable<string> ids)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in project.Sequence.Tracks)
        {
            all.Add(track.Id);
            foreach (var clip in track.Clips)
            {
                all.Add(clip.Id);
            }

            foreach (var transition in track.Transitions)
            {
                all.Add(transition.Id);
            }
        }

        foreach (var marker in project.Sequence.Markers)
        {
            all.Add(marker.Id);
        }

        foreach (var media in project.Media)
        {
            all.Add(media.Id);
        }

        return [.. ids.Where(all.Contains)];
    }

    /// <summary>
    /// The two halves of a transition of <paramref name="duration"/> frames: <c>a = d / 2</c> (floor) before the cut
    /// and <c>b = d - a</c> after it.
    /// </summary>
    /// <param name="duration">The transition duration.</param>
    /// <returns>The frames before and after the cut.</returns>
    public static (long A, long B) TransitionHalves(long duration)
    {
        var a = duration / 2;
        return (a, duration - a);
    }

    /// <summary>The cut frame of a transition (the left clip's end), or null when its left clip is not on the track.</summary>
    /// <param name="track">The track holding the transition.</param>
    /// <param name="transition">The transition.</param>
    /// <returns>The cut frame, or null.</returns>
    public static long? CutFrame(Track track, Transition transition)
    {
        foreach (var clip in track.Clips)
        {
            if (clip.Id == transition.LeftClipId)
            {
                return clip.End;
            }
        }

        return null;
    }

    /// <summary>The transition on the cut at <paramref name="cutFrame"/> of a track, or null.</summary>
    /// <param name="track">The track.</param>
    /// <param name="cutFrame">The cut frame (the left clip's end).</param>
    /// <returns>The transition, or null.</returns>
    public static Transition? TransitionAtCut(Track track, long cutFrame)
    {
        foreach (var transition in track.Transitions)
        {
            if (CutFrame(track, transition) == cutFrame)
            {
                return transition;
            }
        }

        return null;
    }

    /// <summary>
    /// How far a clip renders beyond its body because of transitions: <c>Head</c> frames before its start (the
    /// <c>a</c> of its incoming transition) and <c>Tail</c> frames after its end (the <c>b</c> of its outgoing one).
    /// The clip's extent is <c>[Start - Head, End + Tail)</c>.
    /// </summary>
    /// <param name="track">The track holding the clip.</param>
    /// <param name="clip">The clip.</param>
    /// <returns>The head and tail extensions in frames.</returns>
    public static (long Head, long Tail) Extent(Track track, Clip clip)
    {
        long head = 0, tail = 0;
        foreach (var transition in track.Transitions)
        {
            var (a, b) = TransitionHalves(transition.Duration);
            if (transition.RightClipId == clip.Id)
            {
                head = a;
            }

            if (transition.LeftClipId == clip.Id)
            {
                tail = b;
            }
        }

        return (head, tail);
    }

    /// <summary>True when the clip's source has limits (a non-still media file with a known duration).</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="media">The clip's media, or null.</param>
    /// <returns>False for titles, stills and media without a known duration.</returns>
    public static bool IsBounded(Clip clip, MediaAsset? media) =>
        clip is MediaClip && media is not null && !media.Info.IsStill && media.Info.DurationMicros > 0;

    /// <summary>
    /// The number of timeline frames the clip could extend before its <see cref="Clip.Start"/> while every frame still
    /// maps to a source time of at least 0: <c>floor(SourceIn * 100 / (FlicksPerFrame * Speed))</c>. Negative when the
    /// clip already starts before the source. <see cref="Unbounded"/> for unbounded material.
    /// </summary>
    /// <param name="clip">The clip.</param>
    /// <param name="media">The clip's media, or null.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The head handle in frames.</returns>
    public static long HeadHandle(Clip clip, MediaAsset? media, FrameRate rate)
    {
        if (!IsBounded(clip, media))
        {
            return Unbounded;
        }

        var m = (MediaClip)clip;
        var perFrame = (Int128)rate.FlicksPerFrame * Math.Max(1, m.SpeedPercent);
        return (long)TimeMath.FloorDiv((Int128)m.SourceInFlicks * 100, perFrame);
    }

    /// <summary>
    /// The number of timeline frames the clip could extend after its <see cref="Clip.End"/> while every shown frame
    /// still maps to a source time below the media length. Negative when the clip already runs past the source end.
    /// <see cref="Unbounded"/> for unbounded material.
    /// </summary>
    /// <param name="clip">The clip.</param>
    /// <param name="media">The clip's media, or null.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The tail handle in frames.</returns>
    public static long TailHandle(Clip clip, MediaAsset? media, FrameRate rate)
    {
        if (!IsBounded(clip, media))
        {
            return Unbounded;
        }

        var m = (MediaClip)clip;
        return MaxFramesFrom(m, media!, rate, m.SpeedPercent) - clip.Duration;
    }

    /// <summary>The head handle of a clip of this project (see <see cref="HeadHandle(Clip, MediaAsset?, FrameRate)"/>).</summary>
    /// <param name="project">The project.</param>
    /// <param name="clip">The clip.</param>
    /// <returns>The head handle in frames.</returns>
    public static long HeadHandle(Project project, Clip clip) =>
        HeadHandle(clip, MediaOf(project, clip), project.Sequence.Settings.Rate);

    /// <summary>The tail handle of a clip of this project (see <see cref="TailHandle(Clip, MediaAsset?, FrameRate)"/>).</summary>
    /// <param name="project">The project.</param>
    /// <param name="clip">The clip.</param>
    /// <returns>The tail handle in frames.</returns>
    public static long TailHandle(Project project, Clip clip) =>
        TailHandle(clip, MediaOf(project, clip), project.Sequence.Settings.Rate);

    /// <summary>
    /// The number of frames a media clip starting at its <see cref="MediaClip.SourceInFlicks"/> can show at the given
    /// speed before reaching the end of the source: the count of offsets <c>k &gt;= 0</c> with
    /// <c>SourceIn + floor(k * FlicksPerFrame * speed / 100) &lt; length</c>. <see cref="Unbounded"/> for unbounded media.
    /// </summary>
    /// <param name="clip">The clip (its source in point is used).</param>
    /// <param name="media">The media asset.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <param name="speedPercent">The speed to assume.</param>
    /// <returns>The maximum duration in frames.</returns>
    public static long MaxFramesFrom(MediaClip clip, MediaAsset media, FrameRate rate, int speedPercent)
    {
        if (!IsBounded(clip, media))
        {
            return Unbounded;
        }

        var remaining = (Int128)TimeMath.FlicksFromMicros(media.Info.DurationMicros) - clip.SourceInFlicks;
        if (remaining <= 0)
        {
            return 0;
        }

        // floor(k * q / 100) < R  <=>  k * q < 100 * R  <=>  k < ceil(100 * R / q); the count of such k >= 0.
        var perFrame = (Int128)rate.FlicksPerFrame * Math.Max(1, speedPercent);
        var numerator = remaining * 100;
        return (long)((numerator + perFrame - 1) / perFrame);
    }

    /// <summary>
    /// The largest duration a transition on the cut between <paramref name="left"/> and <paramref name="right"/> may
    /// have: <c>a</c> must fit in the left clip (after its own incoming transition) and in the right clip's head
    /// handle, and <c>b</c> must fit in the right clip (before its own outgoing transition) and the left clip's tail
    /// handle. Any transition already on this cut is ignored.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="track">The track holding both clips.</param>
    /// <param name="left">The outgoing clip.</param>
    /// <param name="right">The incoming clip.</param>
    /// <returns>The maximum duration in frames (below 2 means no transition fits).</returns>
    public static long MaxTransitionDuration(Project project, Track track, Clip left, Clip right) =>
        TransitionBudget(project, track, left, right, out _);

    /// <summary>The frames a linked clip leads the partner that plays the other stream of the same media.</summary>
    /// <remarks>
    /// Positive means the clip shows later source than its partner at the same timeline frame; moving the clip right by
    /// this many frames puts it back in sync. Null when the clip is unlinked, has no such partner, or the speeds differ.
    /// </remarks>
    /// <param name="project">The project.</param>
    /// <param name="clipId">The clip id.</param>
    /// <returns>The offset in frames (0 when in sync), or null.</returns>
    public static long? SyncOffset(Project project, string clipId)
    {
        if (FindClip(project, clipId)?.Clip is not MediaClip clip || clip.LinkId is null)
        {
            return null;
        }

        MediaClip? partner = null;
        foreach (var track in project.Sequence.Tracks)
        {
            foreach (var c in track.Clips)
            {
                if (partner is null && c is MediaClip m && m.Id != clip.Id && m.LinkId == clip.LinkId
                    && m.MediaId == clip.MediaId && m.Stream != clip.Stream)
                {
                    partner = m;
                }
            }
        }

        if (partner is null || partner.SpeedPercent != clip.SpeedPercent)
        {
            return null;
        }

        var rate = project.Sequence.Settings.Rate;
        var perFrame100 = (Int128)rate.FlicksPerFrame * clip.SpeedPercent;          // flicks per frame, times 100
        var delta100 = (((Int128)clip.SourceInFlicks - partner.SourceInFlicks) * 100)
            - ((Int128)(clip.Start - partner.Start) * perFrame100);
        // Round to the nearest frame, halves away from zero.
        var negative = delta100 < 0;
        var abs = negative ? -delta100 : delta100;
        var frames = (long)((abs + (perFrame100 / 2)) / perFrame100);
        return negative ? -frames : frames;
    }

    /// <summary>A short display name for a clip: the media file name or the first line of a title.</summary>
    /// <param name="project">The project.</param>
    /// <param name="clip">The clip.</param>
    /// <returns>The name.</returns>
    public static string DisplayName(Project project, Clip clip)
    {
        switch (clip)
        {
            case MediaClip m:
                var media = FindMedia(project, m.MediaId);
                if (media is null)
                {
                    return m.MediaId;
                }

                var path = media.Path.Replace('\\', '/');
                var slash = path.LastIndexOf('/');
                return slash >= 0 ? path[(slash + 1)..] : path;
            case TitleClip t:
                var line = t.Title.Text.Split('\n')[0].Trim();
                return line.Length == 0 ? "Title" : line.Length > 40 ? line[..40] : line;
            default:
                return clip.Id;
        }
    }

    /// <summary>The transition budget with the reason that limits it (used for clamp messages).</summary>
    /// <param name="project">The project.</param>
    /// <param name="track">The track.</param>
    /// <param name="left">The outgoing clip.</param>
    /// <param name="right">The incoming clip.</param>
    /// <param name="reason">A user-facing reason, for example <c>"not enough source after intro.mov"</c>.</param>
    /// <returns>The maximum duration.</returns>
    internal static long TransitionBudget(Project project, Track track, Clip left, Clip right, out string reason)
    {
        long incomingB = 0, outgoingA = 0;
        foreach (var t in track.Transitions)
        {
            if (t.RightClipId == left.Id)
            {
                incomingB = TransitionHalves(t.Duration).B;
            }

            if (t.LeftClipId == right.Id)
            {
                outgoingA = TransitionHalves(t.Duration).A;
            }
        }

        var rate = project.Sequence.Settings.Rate;
        var leftRoom = left.Duration - incomingB;
        var rightHead = HeadHandle(right, MediaOf(project, right), rate);
        var rightRoom = right.Duration - outgoingA;
        var leftTail = TailHandle(left, MediaOf(project, left), rate);
        var max = Budget(leftRoom, rightHead, rightRoom, leftTail, out var limit);
        reason = BudgetReason(limit, DisplayName(project, left), DisplayName(project, right));
        return max;
    }

    /// <summary>
    /// Combines the four limits of a transition into its maximum duration. <paramref name="limit"/> names the binding
    /// limit: 0 the left clip's tail handle, 1 the right clip's length, 2 the right clip's head handle, 3 the left
    /// clip's length.
    /// </summary>
    /// <param name="leftRoom">Frames of the left clip available for <c>a</c>.</param>
    /// <param name="rightHead">Source frames before the right clip's start (limits <c>a</c>).</param>
    /// <param name="rightRoom">Frames of the right clip available for <c>b</c>.</param>
    /// <param name="leftTail">Source frames after the left clip's end (limits <c>b</c>).</param>
    /// <param name="limit">The binding limit code.</param>
    /// <returns>The maximum duration (may be below 2).</returns>
    internal static long Budget(long leftRoom, long rightHead, long rightRoom, long leftTail, out int limit)
    {
        // a = floor(d/2) <= aMax  <=>  d <= 2*aMax + 1;  b = ceil(d/2) <= bMax  <=>  d <= 2*bMax.
        var aMax = Math.Max(0, Math.Min(leftRoom, rightHead));
        var bMax = Math.Max(0, Math.Min(rightRoom, leftTail));
        var byA = aMax >= Unbounded ? Unbounded : (2 * aMax) + 1;
        var byB = bMax >= Unbounded ? Unbounded : 2 * bMax;
        if (byB <= byA)
        {
            limit = leftTail < rightRoom ? 0 : 1;
            return byB;
        }

        limit = rightHead < leftRoom ? 2 : 3;
        return byA;
    }

    /// <summary>The user-facing reason for a <see cref="Budget"/> limit code.</summary>
    /// <param name="limit">The limit code.</param>
    /// <param name="leftName">The left clip's display name.</param>
    /// <param name="rightName">The right clip's display name.</param>
    /// <returns>For example <c>"not enough source after intro.mov"</c>.</returns>
    internal static string BudgetReason(int limit, string leftName, string rightName) => limit switch
    {
        0 => $"not enough source after {leftName}",
        1 => $"{rightName} is too short",
        2 => $"not enough source before {rightName}",
        _ => $"{leftName} is too short",
    };
}
