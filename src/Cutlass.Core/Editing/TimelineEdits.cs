using System.Collections.Immutable;
using System.Diagnostics;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Core.Editing;

/// <summary>
/// Every timeline edit, as pure functions from a <see cref="Project"/> to an <see cref="EditResult"/> (TECH_BRIEF 3.3,
/// DESIGN 5.5). Edits never throw on user input: they clamp and add a warning. An edit that changes nothing returns the
/// input reference. Edits that create ids take an <see cref="IIdGenerator"/> first. Every result passes
/// <see cref="TimelineValidator"/>; DEBUG builds verify that after each edit.
/// </summary>
/// <remarks>
/// Shared rules: locked tracks are never edited or shifted; transitions whose clips stop butting are removed, and the
/// rest are re-clamped to both clip lengths and both source handles after every edit (earlier cuts win).
/// </remarks>
public static partial class TimelineEdits
{
    /// <summary>The slowest clip speed in percent.</summary>
    public const int MinSpeedPercent = 25;

    /// <summary>The fastest clip speed in percent.</summary>
    public const int MaxSpeedPercent = 400;

    /// <summary>The shortest transition in frames.</summary>
    public const long MinTransitionFrames = 2;

    /// <summary>The default marker colour (the Snap / Marker token).</summary>
    public const string DefaultMarkerColor = "#F5D547";

    /// <summary>The lowest clip, keyframe and track gain in dB (bottom of the volume scale).</summary>
    public const double MinGainDb = -60;

    /// <summary>The highest clip, keyframe and track gain in dB (top of the volume scale).</summary>
    public const double MaxGainDb = 12;

    /// <summary>Converts a frame position from one rate to another by time, rounding to the nearest frame.</summary>
    /// <param name="frame">The frame at <paramref name="from"/>.</param>
    /// <param name="from">The original rate.</param>
    /// <param name="to">The new rate.</param>
    /// <returns>The frame at <paramref name="to"/> showing the same time.</returns>
    public static long ConvertFrame(long frame, FrameRate from, FrameRate to)
    {
        if (from == to)
        {
            return frame;
        }

        // frame * (to / from) = frame * to.Num * from.Den / (to.Den * from.Num), rounded half up.
        var num = (Int128)frame * to.Value.Num * from.Value.Den;
        var den = (Int128)to.Value.Den * from.Value.Num;
        return (long)TimeMath.FloorDiv((2 * num) + den, 2 * den);
    }

    /// <summary>
    /// Clamps every property of a clip to its legal range (speed, opacity, transform, colour, gain, fades within the
    /// duration, keyframes inside <c>[0, Duration)</c> and sorted). Returns the same instance when nothing changes.
    /// </summary>
    /// <param name="clip">The clip.</param>
    /// <returns>The sanitized clip.</returns>
    public static Clip SanitizeClip(Clip clip)
    {
        switch (clip)
        {
            case MediaClip m:
                var speed = Math.Clamp(m.SpeedPercent, MinSpeedPercent, MaxSpeedPercent);
                var video = SanitizeVideo(m.Video, m.Duration);
                var audio = SanitizeAudio(m.Audio, m.Duration);
                return speed == m.SpeedPercent && ReferenceEquals(video, m.Video) && ReferenceEquals(audio, m.Audio)
                    ? m
                    : m with { SpeedPercent = speed, Video = video, Audio = audio };
            case TitleClip t:
                var title = SanitizeTitle(t.Title);
                var opacity = Clamp(t.Opacity, 0, 1, 1);
                var (fadeIn, fadeOut) = ClampFades(t.FadeInFrames, t.FadeOutFrames, t.Duration);
                return ReferenceEquals(title, t.Title) && opacity.Equals(t.Opacity) && fadeIn == t.FadeInFrames
                    && fadeOut == t.FadeOutFrames && t.LinkId is null
                    ? t
                    : t with { Title = title, Opacity = opacity, FadeInFrames = fadeIn, FadeOutFrames = fadeOut, LinkId = null };
            default:
                return clip;
        }
    }

    /// <summary>The volume envelope of keyframes at a clip-relative frame: linear between keys, constant outside them.</summary>
    /// <param name="keys">The keyframes, sorted by frame.</param>
    /// <param name="frame">The clip-relative frame.</param>
    /// <returns>The level in dB (0 when there are no keys).</returns>
    public static double EnvelopeAt(ImmutableArray<VolumeKey> keys, long frame)
    {
        if (keys.IsDefaultOrEmpty)
        {
            return 0;
        }

        if (frame <= keys[0].Frame)
        {
            return keys[0].Db;
        }

        for (var i = 1; i < keys.Length; i++)
        {
            if (frame <= keys[i].Frame)
            {
                var k0 = keys[i - 1];
                var k1 = keys[i];
                var t = (double)(frame - k0.Frame) / (k1.Frame - k0.Frame);
                return k0.Db + ((k1.Db - k0.Db) * t);
            }
        }

        return keys[^1].Db;
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsNaN(value) ? fallback : Math.Clamp(value, min, max);

    private static (long In, long Out) ClampFades(long fadeIn, long fadeOut, long duration)
    {
        var fi = Math.Clamp(fadeIn, 0, Math.Max(0, duration));
        var fo = Math.Clamp(fadeOut, 0, Math.Max(0, duration - fi));
        return (fi, fo);
    }

    private static VideoProps SanitizeVideo(VideoProps v, long duration)
    {
        var tr = v.Transform;
        var cropLeft = Clamp(tr.CropLeft, 0, 0.9, 0);
        var cropTop = Clamp(tr.CropTop, 0, 0.9, 0);
        var cropRight = Math.Min(Clamp(tr.CropRight, 0, 0.9, 0), 0.9 - cropLeft);
        var cropBottom = Math.Min(Clamp(tr.CropBottom, 0, 0.9, 0), 0.9 - cropTop);
        var transform = new Transform(
            Clamp(tr.X, -100_000, 100_000, 0),
            Clamp(tr.Y, -100_000, 100_000, 0),
            Clamp(tr.Scale, 0.01, 100, 1),
            cropLeft,
            cropTop,
            Math.Max(0, cropRight),
            Math.Max(0, cropBottom));
        var c = v.Color;
        var color = new ColorAdjust(
            Clamp(c.Brightness, -1, 1, 0),
            Clamp(c.Contrast, 0, 2, 1),
            Clamp(c.Saturation, 0, 3, 1),
            Clamp(c.Gamma, 0.1, 4, 1));
        var (fadeIn, fadeOut) = ClampFades(v.FadeInFrames, v.FadeOutFrames, duration);
        var result = new VideoProps(
            transform == tr ? tr : transform,
            Clamp(v.Opacity, 0, 1, 1),
            color == c ? c : color,
            fadeIn,
            fadeOut);
        return result == v ? v : result;
    }

    private static AudioProps SanitizeAudio(AudioProps a, long duration)
    {
        var keys = a.Keys.IsDefault ? ImmutableArray<VolumeKey>.Empty : a.Keys;
        var ordered = true;
        for (var i = 0; i < keys.Length; i++)
        {
            var k = keys[i];
            if (k.Frame < 0 || k.Frame >= duration || double.IsNaN(k.Db) || k.Db < MinGainDb || k.Db > MaxGainDb
                || (i > 0 && keys[i - 1].Frame >= k.Frame))
            {
                ordered = false;
                break;
            }
        }

        if (!ordered)
        {
            // Keep the last key per frame, drop keys outside the clip, clamp levels.
            var byFrame = new SortedDictionary<long, double>();
            foreach (var k in keys)
            {
                if (k.Frame >= 0 && k.Frame < duration)
                {
                    byFrame[k.Frame] = Clamp(k.Db, MinGainDb, MaxGainDb, 0);
                }
            }

            keys = [.. byFrame.Select(kv => new VolumeKey(kv.Key, kv.Value))];
        }

        var (fadeIn, fadeOut) = ClampFades(a.FadeInFrames, a.FadeOutFrames, duration);
        var gain = Clamp(a.GainDb, MinGainDb, MaxGainDb, 0);
        if (ordered && !a.Keys.IsDefault && gain.Equals(a.GainDb) && fadeIn == a.FadeInFrames && fadeOut == a.FadeOutFrames)
        {
            return a;
        }

        return new AudioProps(gain, fadeIn, fadeOut, keys);
    }

    private static TitleSpec SanitizeTitle(TitleSpec t)
    {
        var result = t with
        {
            Text = t.Text ?? string.Empty,
            Font = string.IsNullOrWhiteSpace(t.Font) ? TitleSpec.Default.Font : t.Font,
            Size = Clamp(t.Size, 1, 2000, TitleSpec.Default.Size),
            OutlineWidth = Clamp(t.OutlineWidth, 0, 100, 0),
            BoxPadding = Clamp(t.BoxPadding, 0, 1000, 0),
            OffsetX = Clamp(t.OffsetX, -100_000, 100_000, 0),
            OffsetY = Clamp(t.OffsetY, -100_000, 100_000, 0),
        };
        return result == t ? t : result;
    }

    private static Clip WithFades(Clip clip, long? fadeIn, long? fadeOut) => clip switch
    {
        MediaClip m => m with
        {
            Video = m.Video with { FadeInFrames = fadeIn ?? m.Video.FadeInFrames, FadeOutFrames = fadeOut ?? m.Video.FadeOutFrames },
            Audio = m.Audio with { FadeInFrames = fadeIn ?? m.Audio.FadeInFrames, FadeOutFrames = fadeOut ?? m.Audio.FadeOutFrames },
        },
        TitleClip t => t with { FadeInFrames = fadeIn ?? t.FadeInFrames, FadeOutFrames = fadeOut ?? t.FadeOutFrames },
        _ => clip,
    };

    [Conditional("DEBUG")]
    private static void DebugCheck(Project before, Project after, string label)
    {
        var errors = TimelineValidator.Check(after);
        if (errors.IsEmpty || !TimelineValidator.IsValid(before))
        {
            return;
        }

        throw new InvalidOperationException($"Edit '{label}' produced an invalid project: {string.Join("; ", errors)}");
    }

    /// <summary>A mutable copy of one track used while an edit runs.</summary>
    private sealed class TrackWork
    {
        public TrackWork(Track track)
        {
            Original = track;
            Props = track;
            Clips = [.. track.Clips];
            Transitions = [.. track.Transitions];
        }

        public Track Original { get; }

        /// <summary>The track's properties (name, flags); its Clips and Transitions are ignored.</summary>
        public Track Props { get; set; }

        public List<Clip> Clips { get; }

        public List<Transition> Transitions { get; set; }

        public string Id => Props.Id;

        public TrackKind Kind => Props.Kind;

        public bool Locked => Props.Locked;

        public string Name => Props.Name;

        public int IndexOf(string clipId) => Clips.FindIndex(c => c.Id == clipId);

        public int IndexAt(long frame) => Clips.FindIndex(c => c.Start <= frame && frame < c.End);

        public void Add(Clip clip)
        {
            var index = Clips.FindIndex(c => c.Start > clip.Start);
            Clips.Insert(index < 0 ? Clips.Count : index, clip);
        }

        public void Sort()
        {
            var ordered = true;
            for (var i = 1; i < Clips.Count && ordered; i++)
            {
                ordered = Clips[i - 1].Start <= Clips[i].Start;
            }

            if (ordered)
            {
                return;
            }

            var sorted = Clips.OrderBy(c => c.Start).ToList();
            Clips.Clear();
            Clips.AddRange(sorted);
        }

        public bool Overlaps(long start, long end) => Clips.Exists(c => c.Start < end && c.End > start);

        public long? CutOf(Transition transition)
        {
            var left = Clips.Find(c => c.Id == transition.LeftClipId);
            return left?.End;
        }

        /// <summary>Shifts every clip starting at or after <paramref name="from"/>; when moving left, only if they fit.</summary>
        /// <returns>False when moving left would collide (the track is then left alone).</returns>
        public bool ShiftFrom(long from, long delta)
        {
            if (delta == 0)
            {
                return true;
            }

            var first = Clips.FindIndex(c => c.Start >= from);
            if (first < 0)
            {
                return true;
            }

            if (delta < 0)
            {
                var previousEnd = first > 0 ? Clips[first - 1].End : 0;
                if (Clips[first].Start + delta < previousEnd)
                {
                    return false;
                }
            }

            for (var i = first; i < Clips.Count; i++)
            {
                Clips[i] = Clips[i] with { Start = Clips[i].Start + delta };
            }

            return true;
        }

        public void RenameTransitionLeft(string oldId, string newId)
        {
            for (var i = 0; i < Transitions.Count; i++)
            {
                if (Transitions[i].LeftClipId == oldId)
                {
                    Transitions[i] = Transitions[i] with { LeftClipId = newId };
                }
            }
        }

        public Track Build()
        {
            var unchanged = ReferenceEquals(Props, Original)
                && Clips.SequenceEqual(Original.Clips)
                && Transitions.SequenceEqual(Original.Transitions);
            if (unchanged)
            {
                return Original;
            }

            return Props with { Clips = [.. Clips], Transitions = [.. Transitions] };
        }
    }

    /// <summary>The mutable working state of one edit. <see cref="Finish"/> turns it back into an immutable result.</summary>
    private sealed class Work
    {
        private readonly Dictionary<string, string> _rightLinks = new(StringComparer.Ordinal);
        private Dictionary<string, MediaAsset>? _mediaById;
        private ImmutableArray<MediaAsset> _media;

        public Work(Project project, IIdGenerator? ids = null)
        {
            Original = project;
            Ids = ids;
            Settings = project.Sequence.Settings;
            _media = project.Media;
            Markers = [.. project.Sequence.Markers];
            InFrame = project.Sequence.InFrame;
            OutFrame = project.Sequence.OutFrame;
            HasHadClips = project.Sequence.HasHadClips;
            Tracks = [.. project.Sequence.Tracks.Select(t => new TrackWork(t))];
        }

        public Project Original { get; }

        public IIdGenerator? Ids { get; }

        public SequenceSettings Settings { get; set; }

        public ImmutableArray<MediaAsset> Media
        {
            get => _media;
            set
            {
                _media = value;
                _mediaById = null;
            }
        }

        public List<Marker> Markers { get; }

        public long? InFrame { get; set; }

        public long? OutFrame { get; set; }

        public bool HasHadClips { get; set; }

        public List<TrackWork> Tracks { get; }

        public List<string> Warnings { get; } = [];

        public FrameRate Rate => Settings.Rate;

        public void Warn(string message)
        {
            if (!Warnings.Contains(message))
            {
                Warnings.Add(message);
            }
        }

        public TrackWork? Track(string trackId) => Tracks.Find(t => t.Id == trackId);

        public (TrackWork Track, int Index)? Find(string clipId)
        {
            foreach (var track in Tracks)
            {
                var index = track.IndexOf(clipId);
                if (index >= 0)
                {
                    return (track, index);
                }
            }

            return null;
        }

        public List<TrackWork> OfKind(TrackKind kind) => [.. Tracks.Where(t => t.Kind == kind)];

        public MediaAsset? MediaOf(Clip clip)
        {
            if (clip is not MediaClip m)
            {
                return null;
            }

            _mediaById ??= Media.GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            return _mediaById.GetValueOrDefault(m.MediaId);
        }

        public bool IsStill(Clip clip) => MediaOf(clip)?.Info.IsStill == true;

        public long Head(Clip clip) => TimelineQuery.HeadHandle(clip, MediaOf(clip), Rate);

        public long Tail(Clip clip) => TimelineQuery.TailHandle(clip, MediaOf(clip), Rate);

        public string Name(Clip clip)
        {
            if (clip is MediaClip m && MediaOf(m) is { } media)
            {
                var path = media.Path.Replace('\\', '/');
                var slash = path.LastIndexOf('/');
                return slash >= 0 ? path[(slash + 1)..] : path;
            }

            return clip is TitleClip t ? (t.Title.Text.Split('\n')[0].Trim() is { Length: > 0 } line ? line : "Title") : clip.Id;
        }

        public string NewId(string prefix) =>
            (Ids ?? throw new InvalidOperationException("This edit needs an id generator.")).NewId(prefix);

        /// <summary>The link group for right-hand pieces of clips split during this edit: one new group per original group.</summary>
        public string? RightLink(string? link)
        {
            if (link is null)
            {
                return null;
            }

            if (!_rightLinks.TryGetValue(link, out var mapped))
            {
                mapped = NewId(IIdGenerator.LinkPrefix);
                _rightLinks[link] = mapped;
            }

            return mapped;
        }

        /// <summary>Moves a clip's start to <paramref name="newStart"/> keeping its end; the source follows (stills excepted).</summary>
        public Clip TrimHeadTo(Clip clip, long newStart)
        {
            if (newStart == clip.Start)
            {
                return clip;
            }

            var shift = newStart - clip.Start;
            var duration = clip.End - newStart;
            if (clip is MediaClip m)
            {
                var source = IsStill(m) ? m.SourceInFlicks : TimeMath.SourceFlicksAt(m, newStart, Rate);
                var audio = m.Audio.Keys.IsDefaultOrEmpty
                    ? m.Audio
                    : m.Audio with { Keys = [.. m.Audio.Keys.Select(k => k with { Frame = k.Frame - shift })] };
                return SanitizeClip(m with { Start = newStart, Duration = duration, SourceInFlicks = source, Audio = audio });
            }

            return SanitizeClip(clip with { Start = newStart, Duration = duration });
        }

        /// <summary>Moves a clip's end to <paramref name="newEnd"/> keeping its start.</summary>
        public static Clip TrimTailTo(Clip clip, long newEnd) =>
            newEnd == clip.End ? clip : SanitizeClip(clip with { Duration = newEnd - clip.Start });

        /// <summary>
        /// Splits the clip at <paramref name="index"/> at <paramref name="frame"/> (strictly inside it). The left piece
        /// keeps the id and link group; the right piece gets a new id and the edit's right-hand link group. Fades at
        /// the new cut are dropped, keyframes are split with a boundary key on each side, and the clip's outgoing
        /// transition moves to the right piece.
        /// </summary>
        public (Clip Left, Clip Right) SplitAt(TrackWork track, int index, long frame)
        {
            var clip = track.Clips[index];
            var rightId = NewId(IIdGenerator.ClipPrefix);
            var left = WithFades(TrimTailTo(clip, frame), null, 0);
            var right = WithFades(TrimHeadTo(clip, frame), 0, null) with { Id = rightId, LinkId = RightLink(clip.LinkId) };
            if (clip is MediaClip m && !m.Audio.Keys.IsDefaultOrEmpty)
            {
                var offset = frame - clip.Start;
                var lm = (MediaClip)left;
                var rm = (MediaClip)right;
                var lastLeft = offset - 1;
                if (!lm.Audio.Keys.Any(k => k.Frame == lastLeft))
                {
                    var db = EnvelopeAt(m.Audio.Keys, lastLeft);
                    left = lm with { Audio = lm.Audio with { Keys = [.. lm.Audio.Keys, new VolumeKey(lastLeft, db)] } };
                }

                if (!rm.Audio.Keys.Any(k => k.Frame == 0))
                {
                    var db = EnvelopeAt(m.Audio.Keys, offset);
                    right = rm with { Audio = rm.Audio with { Keys = [new VolumeKey(0, db), .. rm.Audio.Keys] } };
                }
            }

            left = SanitizeClip(left);
            right = SanitizeClip(right);
            track.Clips[index] = left;
            track.Clips.Insert(index + 1, right);
            track.RenameTransitionLeft(clip.Id, rightId);
            return (left, right);
        }

        /// <summary>Empties <c>[start, end)</c> on a track: covered clips are removed, trimmed or split (overwrite).</summary>
        public void ClearRange(TrackWork track, long start, long end)
        {
            for (var i = track.Clips.Count - 1; i >= 0; i--)
            {
                var c = track.Clips[i];
                if (c.End <= start || c.Start >= end)
                {
                    continue;
                }

                if (c.Start >= start && c.End <= end)
                {
                    track.Clips.RemoveAt(i);
                }
                else if (c.Start < start && c.End > end)
                {
                    var (_, right) = SplitAt(track, i, start);
                    track.Clips[i + 1] = TrimHeadTo(right, end);
                }
                else if (c.Start < start)
                {
                    track.Clips[i] = TrimTailTo(c, start);
                }
                else
                {
                    track.Clips[i] = TrimHeadTo(c, end);
                }
            }
        }

        /// <summary>
        /// Opens <paramref name="amount"/> frames at <paramref name="at"/> on every unlocked track: a clip spanning the
        /// point is split, later clips move right, and transitions whose region contains the point are removed.
        /// </summary>
        public void InsertShift(long at, long amount)
        {
            foreach (var track in Tracks)
            {
                if (track.Locked)
                {
                    continue;
                }

                track.Transitions.RemoveAll(tr =>
                {
                    var cut = track.CutOf(tr);
                    if (cut is null)
                    {
                        return false;
                    }

                    var (a, b) = TimelineQuery.TransitionHalves(tr.Duration);
                    return cut.Value - a <= at && at < cut.Value + b;
                });
                var index = track.Clips.FindIndex(c => c.Start < at && at < c.End);
                if (index >= 0)
                {
                    SplitAt(track, index, at);
                }

                track.ShiftFrom(at, amount);
            }
        }

        /// <summary>
        /// Ripples the tracks not in <paramref name="edited"/>: clips starting at or after <paramref name="editPoint"/>
        /// move by <paramref name="delta"/>. A track without room to move left is left alone, with a warning.
        /// </summary>
        public void RippleOthers(IReadOnlyCollection<TrackWork> edited, long editPoint, long delta)
        {
            if (delta == 0)
            {
                return;
            }

            foreach (var track in Tracks)
            {
                if (track.Locked || edited.Contains(track))
                {
                    continue;
                }

                if (!track.ShiftFrom(editPoint, delta))
                {
                    Warn($"{track.Name} has no room to close up and was left alone.");
                }
            }
        }

        /// <summary>Trims or removes the clips of the given media so they lie inside their (possibly changed) source.</summary>
        public void ClampToSource(ISet<string>? mediaIds)
        {
            foreach (var track in Tracks)
            {
                for (var i = track.Clips.Count - 1; i >= 0; i--)
                {
                    if (track.Clips[i] is not MediaClip m || (mediaIds is not null && !mediaIds.Contains(m.MediaId)))
                    {
                        continue;
                    }

                    var media = MediaOf(m);
                    if (media is null
                        || (m.Stream == StreamKind.Video && media.Info.Video is null)
                        || (m.Stream == StreamKind.Audio && media.Info.Audio is null))
                    {
                        track.Clips.RemoveAt(i);
                        Warn($"Removed a clip of {Name(m)}: the media no longer has that stream.");
                        continue;
                    }

                    Clip clip = m;
                    var head = Head(clip);
                    if (head < 0)
                    {
                        if (-head >= clip.Duration)
                        {
                            track.Clips.RemoveAt(i);
                            Warn($"Removed a clip of {Name(m)}: it lies outside the media.");
                            continue;
                        }

                        clip = TrimHeadTo(clip, clip.Start - head);
                    }

                    var tail = Tail(clip);
                    if (tail < 0)
                    {
                        if (clip.Duration + tail < 1)
                        {
                            track.Clips.RemoveAt(i);
                            Warn($"Removed a clip of {Name(m)}: it lies outside the media.");
                            continue;
                        }

                        clip = TrimTailTo(clip, clip.End + tail);
                    }

                    if (!ReferenceEquals(clip, m))
                    {
                        track.Clips[i] = clip;
                        Warn($"Shortened a clip of {Name(m)} to fit its media.");
                    }
                }
            }
        }

        /// <summary>Drops transitions that no longer join butting clips and re-clamps the rest, earlier cuts first.</summary>
        private void NormalizeTransitions(TrackWork track)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < track.Clips.Count; i++)
            {
                index[track.Clips[i].Id] = i;
            }

            var candidates = new List<(Transition Transition, int Left)>();
            var usedLeft = new HashSet<int>();
            foreach (var tr in track.Transitions)
            {
                if (!index.TryGetValue(tr.LeftClipId, out var li) || !index.TryGetValue(tr.RightClipId, out var ri)
                    || ri != li + 1 || track.Clips[li].End != track.Clips[ri].Start || tr.Duration < MinTransitionFrames
                    || !usedLeft.Add(li))
                {
                    continue;
                }

                var kind = tr.Kind;
                if (track.Kind == TrackKind.Audio && kind != TransitionKind.AudioCrossfade)
                {
                    kind = TransitionKind.AudioCrossfade;
                }
                else if (track.Kind == TrackKind.Video && kind == TransitionKind.AudioCrossfade)
                {
                    kind = TransitionKind.Crossfade;
                }

                candidates.Add((kind == tr.Kind ? tr : tr with { Kind = kind }, li));
            }

            candidates.Sort((x, y) => x.Left.CompareTo(y.Left));
            var result = new List<Transition>(candidates.Count);
            var incomingB = new Dictionary<int, long>();
            foreach (var (tr, li) in candidates)
            {
                var left = track.Clips[li];
                var right = track.Clips[li + 1];
                var max = TimelineQuery.Budget(
                    left.Duration - incomingB.GetValueOrDefault(li),
                    Head(right),
                    right.Duration,
                    Tail(left),
                    out var limit);
                var duration = tr.Duration;
                if (duration > max)
                {
                    var reason = TimelineQuery.BudgetReason(limit, Name(left), Name(right));
                    if (max < MinTransitionFrames)
                    {
                        Warn($"Removed a transition: {reason}.");
                        continue;
                    }

                    Warn($"Shortened to {max} frames: {reason}.");
                    duration = max;
                }

                incomingB[li + 1] = TimelineQuery.TransitionHalves(duration).B;
                result.Add(duration == tr.Duration ? tr : tr with { Duration = duration });
            }

            track.Transitions = result;
        }

        /// <summary>Normalizes the working state and builds the result (the input reference when nothing changed).</summary>
        public EditResult Finish(string label, IEnumerable<string>? select = null)
        {
            foreach (var track in Tracks)
            {
                track.Sort();

                // Clips the edit did not touch are the original instances and need no second look.
                var untouched = new HashSet<Clip>(track.Original.Clips, ReferenceEqualityComparer.Instance);
                for (var i = 0; i < track.Clips.Count; i++)
                {
                    if (!untouched.Contains(track.Clips[i]))
                    {
                        track.Clips[i] = SanitizeClip(track.Clips[i]);
                    }
                }

                NormalizeTransitions(track);
                if (track.Clips.Count > 0)
                {
                    HasHadClips = true;
                }
            }

            var markers = Markers.OrderBy(m => m.Frame).ToList();
            var tracks = Tracks.Select(t => t.Build()).ToImmutableArray();
            var old = Original.Sequence;
            var tracksSame = tracks.Length == old.Tracks.Length && tracks.Zip(old.Tracks).All(p => ReferenceEquals(p.First, p.Second));
            var markersSame = markers.SequenceEqual(old.Markers);
            var mediaSame = Media.SequenceEqual(Original.Media);
            var seqSame = tracksSame && markersSame && Settings == old.Settings && InFrame == old.InFrame
                && OutFrame == old.OutFrame && HasHadClips == old.HasHadClips;
            var warnings = Warnings.ToImmutableArray();
            if (seqSame && mediaSame)
            {
                return new EditResult(Original, label, warnings, ImmutableArray<string>.Empty);
            }

            var sequence = new Sequence(
                Settings,
                tracksSame ? old.Tracks : tracks,
                markersSame ? old.Markers : [.. markers],
                InFrame,
                OutFrame,
                HasHadClips);
            var project = Original with { Sequence = sequence, Media = mediaSame ? Original.Media : Media };
            DebugCheck(Original, project, label);
            var selection = select is null ? ImmutableArray<string>.Empty : TimelineQuery.ExistingIds(project, select.Distinct());
            return new EditResult(project, label, warnings, selection);
        }
    }
}
