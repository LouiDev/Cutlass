using System.Collections.Immutable;
using System.Globalization;
using Cutlass.Core.Model;

namespace Cutlass.Core.Editing;

/// <summary>
/// Checks the model invariants of TECH_BRIEF 3.3. Every edit result must pass; it runs after every edit in DEBUG builds
/// and in tests. Linked clips have no equality constraint: a link is only a group id.
/// </summary>
public static class TimelineValidator
{
    /// <summary>The maximum number of tracks of each kind.</summary>
    public const int MaxTracksPerKind = 8;

    /// <summary>Returns every violated invariant as a readable message. Empty means valid.</summary>
    /// <param name="project">The project to check.</param>
    /// <returns>The violations.</returns>
    public static ImmutableArray<string> Check(Project project)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        void Fail(FormattableString message) => errors.Add(message.ToString(CultureInfo.InvariantCulture));

        var seq = project.Sequence;
        var settings = seq.Settings;
        if (settings.Width <= 0 || settings.Height <= 0 || settings.Width % 2 != 0 || settings.Height % 2 != 0)
        {
            Fail($"Sequence size {settings.Width}x{settings.Height} must be positive and even.");
        }

        if (!settings.Rate.IsSupported)
        {
            Fail($"Sequence rate {settings.Rate} is not a supported rate.");
        }

        var media = new Dictionary<string, MediaAsset>(StringComparer.Ordinal);
        foreach (var asset in project.Media)
        {
            if (!media.TryAdd(asset.Id, asset))
            {
                Fail($"Duplicate media id {asset.Id}.");
            }
        }

        // Rule 6 and track order: video tracks first, then audio tracks.
        int videoCount = 0, audioCount = 0;
        var seenAudio = false;
        var trackIds = new HashSet<string>(StringComparer.Ordinal);
        var clipIds = new HashSet<string>(StringComparer.Ordinal);
        var transitionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in seq.Tracks)
        {
            if (!trackIds.Add(track.Id))
            {
                Fail($"Duplicate track id {track.Id}.");
            }

            if (track.Kind == TrackKind.Video)
            {
                videoCount++;
                if (seenAudio)
                {
                    Fail($"Video track {track.Id} follows an audio track.");
                }
            }
            else
            {
                audioCount++;
                seenAudio = true;
            }

            CheckTrack(project, track, media, clipIds, transitionIds, Fail);
        }

        if (videoCount > MaxTracksPerKind || audioCount > MaxTracksPerKind)
        {
            Fail($"Too many tracks: {videoCount} video and {audioCount} audio (at most {MaxTracksPerKind} each).");
        }

        long previousMarker = long.MinValue;
        var markerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var marker in seq.Markers)
        {
            if (!markerIds.Add(marker.Id))
            {
                Fail($"Duplicate marker id {marker.Id}.");
            }

            if (marker.Frame < 0)
            {
                Fail($"Marker {marker.Id} is at negative frame {marker.Frame}.");
            }

            if (marker.Frame < previousMarker)
            {
                Fail($"Marker {marker.Id} is out of order.");
            }

            previousMarker = marker.Frame;
        }

        if (seq.InFrame is < 0)
        {
            Fail($"In point {seq.InFrame} is negative.");
        }

        if (seq.OutFrame is < 1)
        {
            Fail($"Out point {seq.OutFrame} must be at least 1.");
        }

        if (seq.InFrame is { } inFrame && seq.OutFrame is { } outFrame && outFrame <= inFrame)
        {
            Fail($"Out point {outFrame} is not after In point {inFrame}.");
        }

        return errors.ToImmutable();
    }

    /// <summary>True when <see cref="Check"/> finds nothing.</summary>
    /// <param name="project">The project to check.</param>
    /// <returns>True when valid.</returns>
    public static bool IsValid(Project project) => Check(project).IsEmpty;

    private static void CheckTrack(
        Project project,
        Track track,
        Dictionary<string, MediaAsset> media,
        HashSet<string> clipIds,
        HashSet<string> transitionIds,
        Action<FormattableString> fail)
    {
        var rate = project.Sequence.Settings.Rate;
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        Clip? previous = null;
        for (var i = 0; i < track.Clips.Length; i++)
        {
            var clip = track.Clips[i];
            index[clip.Id] = i;
            if (!clipIds.Add(clip.Id))
            {
                fail($"Duplicate clip id {clip.Id}.");
            }

            // Rule 1.
            if (clip.Duration < 1)
            {
                fail($"Clip {clip.Id} has duration {clip.Duration}.");
            }

            if (clip.Start < 0)
            {
                fail($"Clip {clip.Id} starts at negative frame {clip.Start}.");
            }

            if (previous is not null && previous.End > clip.Start)
            {
                fail($"Clips {previous.Id} and {clip.Id} on {track.Name} overlap or are out of order.");
            }

            previous = clip;

            // Rules 2 and 5.
            switch (clip)
            {
                case MediaClip m:
                    var expected = track.Kind == TrackKind.Video ? StreamKind.Video : StreamKind.Audio;
                    if (m.Stream != expected)
                    {
                        fail($"Clip {m.Id} plays the {m.Stream} stream on {track.Kind} track {track.Name}.");
                    }

                    if (!media.TryGetValue(m.MediaId, out var asset))
                    {
                        fail($"Clip {m.Id} refers to missing media {m.MediaId}.");
                    }
                    else if ((m.Stream == StreamKind.Video && asset.Info.Video is null)
                        || (m.Stream == StreamKind.Audio && asset.Info.Audio is null))
                    {
                        fail($"Clip {m.Id} uses the {m.Stream} stream, which media {m.MediaId} does not have.");
                    }

                    if (m.SpeedPercent is < TimelineEdits.MinSpeedPercent or > TimelineEdits.MaxSpeedPercent)
                    {
                        fail($"Clip {m.Id} has speed {m.SpeedPercent}%.");
                    }

                    CheckFades(m.Id, "video", m.Video.FadeInFrames, m.Video.FadeOutFrames, m.Duration, fail);
                    CheckFades(m.Id, "audio", m.Audio.FadeInFrames, m.Audio.FadeOutFrames, m.Duration, fail);
                    long lastKey = -1;
                    foreach (var key in m.Audio.Keys)
                    {
                        if (key.Frame < 0 || key.Frame >= m.Duration)
                        {
                            fail($"Clip {m.Id} has a volume key at {key.Frame}, outside [0, {m.Duration}).");
                        }

                        if (key.Frame <= lastKey)
                        {
                            fail($"Clip {m.Id} has volume keys out of order at {key.Frame}.");
                        }

                        lastKey = key.Frame;
                    }

                    break;
                case TitleClip t:
                    if (track.Kind != TrackKind.Video)
                    {
                        fail($"Title {t.Id} is on audio track {track.Name}.");
                    }

                    if (t.LinkId is not null)
                    {
                        fail($"Title {t.Id} is linked.");
                    }

                    CheckFades(t.Id, "title", t.FadeInFrames, t.FadeOutFrames, t.Duration, fail);
                    break;
            }
        }

        // Rule 4.
        var asLeft = new HashSet<string>(StringComparer.Ordinal);
        var asRight = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transition in track.Transitions)
        {
            if (!transitionIds.Add(transition.Id))
            {
                fail($"Duplicate transition id {transition.Id}.");
            }

            var audioKind = transition.Kind == TransitionKind.AudioCrossfade;
            if (audioKind != (track.Kind == TrackKind.Audio))
            {
                fail($"Transition {transition.Id} of kind {transition.Kind} is on {track.Kind} track {track.Name}.");
            }

            if (transition.Duration < TimelineEdits.MinTransitionFrames)
            {
                fail($"Transition {transition.Id} has duration {transition.Duration}.");
            }

            if (!index.TryGetValue(transition.LeftClipId, out var li) || !index.TryGetValue(transition.RightClipId, out var ri))
            {
                fail($"Transition {transition.Id} refers to a clip that is not on track {track.Name}.");
                continue;
            }

            var left = track.Clips[li];
            var right = track.Clips[ri];
            if (ri != li + 1 || left.End != right.Start)
            {
                fail($"Transition {transition.Id} joins clips {left.Id} and {right.Id}, which do not butt.");
            }

            if (!asLeft.Add(left.Id) || !asRight.Add(right.Id))
            {
                fail($"Transition {transition.Id} shares a cut with another transition.");
            }

            var (a, b) = TimelineQuery.TransitionHalves(transition.Duration);
            if (a > left.Duration || b > right.Duration)
            {
                fail($"Transition {transition.Id} ({transition.Duration} frames) is longer than its clips allow.");
            }
        }

        // Rule 4 (no overlap on one clip) and rule 3 (extents inside the source).
        foreach (var clip in track.Clips)
        {
            var (head, tail) = TimelineQuery.Extent(track, clip);
            var b = 0L;
            var a = 0L;
            foreach (var transition in track.Transitions)
            {
                if (transition.RightClipId == clip.Id)
                {
                    b = TimelineQuery.TransitionHalves(transition.Duration).B;
                }

                if (transition.LeftClipId == clip.Id)
                {
                    a = TimelineQuery.TransitionHalves(transition.Duration).A;
                }
            }

            if (a + b > clip.Duration)
            {
                fail($"The transitions on clip {clip.Id} overlap.");
            }

            if (clip is MediaClip mc && media.TryGetValue(mc.MediaId, out var asset) && TimelineQuery.IsBounded(clip, asset))
            {
                if (TimelineQuery.HeadHandle(clip, asset, rate) < head)
                {
                    fail($"Clip {clip.Id} starts before its source (extent head {head}).");
                }

                if (TimelineQuery.TailHandle(clip, asset, rate) < tail)
                {
                    fail($"Clip {clip.Id} runs past the end of its source (extent tail {tail}).");
                }
            }
        }
    }

    private static void CheckFades(string id, string what, long fadeIn, long fadeOut, long duration, Action<FormattableString> fail)
    {
        if (fadeIn < 0 || fadeOut < 0 || fadeIn + fadeOut > duration)
        {
            fail($"Clip {id} has {what} fades {fadeIn}+{fadeOut} for duration {duration}.");
        }
    }
}
