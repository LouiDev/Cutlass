using System.Collections.Immutable;
using System.Globalization;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Core.Editing;

/// <summary>New projects and track edits: properties, add and remove.</summary>
public static partial class TimelineEdits
{
    /// <summary>The settings of a new sequence until the first clip sets them: 1920x1080 at 30 fps.</summary>
    public static SequenceSettings DefaultSettings { get; } = new(1920, 1080, FrameRate.Fps30);

    /// <summary>
    /// Creates an empty project with tracks V1, V2, A1 and A2 (DESIGN 2.1). V1 and A1 are targeted.
    /// </summary>
    /// <param name="ids">The id generator.</param>
    /// <param name="settings">The sequence settings, or null for <see cref="DefaultSettings"/>.</param>
    /// <returns>The new project.</returns>
    public static Project NewProject(IIdGenerator ids, SequenceSettings? settings = null)
    {
        ImmutableArray<Track> tracks =
        [
            NewTrack(ids, TrackKind.Video, "V1", targeted: true),
            NewTrack(ids, TrackKind.Video, "V2", targeted: false),
            NewTrack(ids, TrackKind.Audio, "A1", targeted: true),
            NewTrack(ids, TrackKind.Audio, "A2", targeted: false),
        ];
        var sequence = new Sequence(settings ?? DefaultSettings, tracks, ImmutableArray<Marker>.Empty, null, null, false);
        return new Project(Project.CurrentSchemaVersion, ImmutableArray<MediaAsset>.Empty, sequence);
    }

    /// <summary>
    /// Changes track properties (name, hidden, lock, target, mute, solo, gain). Id, kind, clips and transitions are
    /// kept; an empty name keeps the old one; gain clamps to -60..+12 dB; video tracks have no mute, solo or gain and
    /// audio tracks cannot be hidden. Works on locked tracks, so they can be unlocked.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="trackId">The track.</param>
    /// <param name="update">Returns the changed track.</param>
    /// <param name="label">The undo label, for example "Lock Track".</param>
    /// <returns>The result.</returns>
    public static EditResult SetTrack(Project project, string trackId, Func<Track, Track> update, string label)
    {
        var w = new Work(project);
        var track = w.Track(trackId);
        if (track is null)
        {
            return EditResult.Unchanged(project, label, "The track no longer exists.");
        }

        var old = track.Props;
        var updated = update(old);
        var name = string.IsNullOrWhiteSpace(updated.Name) ? old.Name : updated.Name.Trim();
        var video = old.Kind == TrackKind.Video;
        var props = old with
        {
            Name = name,
            Hidden = video && updated.Hidden,
            Locked = updated.Locked,
            Targeted = updated.Targeted,
            Muted = !video && updated.Muted,
            Solo = !video && updated.Solo,
            GainDb = video ? 0 : Clamp(updated.GainDb, MinGainDb, MaxGainDb, 0),
        };
        if (props.Name == old.Name && props.Hidden == old.Hidden && props.Locked == old.Locked && props.Targeted == old.Targeted
            && props.Muted == old.Muted && props.Solo == old.Solo && props.GainDb.Equals(old.GainDb))
        {
            return EditResult.Unchanged(project, label);
        }

        track.Props = props;
        return w.Finish(label);
    }

    /// <summary>
    /// Adds a track of a kind at a kind index (0 is V1 or A1; null appends), at most 8 per kind. Tracks that still have
    /// their automatic name (<c>V2</c>, <c>A3</c>, ...) are renumbered to their position.
    /// </summary>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="kindIndex">The position among tracks of that kind, or null for the end.</param>
    /// <returns>The result; <see cref="EditResult.Select"/> holds the track id.</returns>
    public static EditResult AddTrack(IIdGenerator ids, Project project, TrackKind kind, int? kindIndex = null)
    {
        const string label = "Add Track";
        var w = new Work(project, ids);
        var ofKind = w.OfKind(kind);
        if (ofKind.Count >= TimelineValidator.MaxTracksPerKind)
        {
            return EditResult.Unchanged(
                project, label, $"A sequence has at most {TimelineValidator.MaxTracksPerKind} {(kind == TrackKind.Video ? "video" : "audio")} tracks.");
        }

        var position = Math.Clamp(kindIndex ?? ofKind.Count, 0, ofKind.Count);
        var firstOfKind = kind == TrackKind.Video ? 0 : w.Tracks.Count(t => t.Kind == TrackKind.Video);
        var track = NewTrack(ids, kind, AutoName(kind, position), targeted: false);
        w.Tracks.Insert(firstOfKind + position, new TrackWork(track) { Props = track });
        RenumberAutoNames(w);
        return w.Finish(label, [track.Id]);
    }

    /// <summary>
    /// Removes a track with its clips (the UI confirms when it is not empty). The last track of a kind and locked
    /// tracks stay.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="trackId">The track.</param>
    /// <returns>The result.</returns>
    public static EditResult RemoveTrack(Project project, string trackId)
    {
        const string label = "Delete Track";
        var w = new Work(project);
        var track = w.Track(trackId);
        if (track is null)
        {
            return EditResult.Unchanged(project, label, "The track no longer exists.");
        }

        if (track.Locked)
        {
            return EditResult.Unchanged(project, label, $"{track.Name} is locked.");
        }

        if (w.OfKind(track.Kind).Count <= 1)
        {
            return EditResult.Unchanged(
                project, label, $"A sequence needs at least one {(track.Kind == TrackKind.Video ? "video" : "audio")} track.");
        }

        w.Tracks.Remove(track);
        RenumberAutoNames(w);
        return w.Finish(label);
    }

    private static Track NewTrack(IIdGenerator ids, TrackKind kind, string name, bool targeted) => new(
        ids.NewId(IIdGenerator.TrackPrefix),
        kind,
        name,
        Hidden: false,
        Locked: false,
        Targeted: targeted,
        Muted: false,
        Solo: false,
        GainDb: 0,
        ImmutableArray<Clip>.Empty,
        ImmutableArray<Transition>.Empty);

    private static string AutoName(TrackKind kind, int kindIndex) =>
        string.Create(CultureInfo.InvariantCulture, $"{(kind == TrackKind.Video ? 'V' : 'A')}{kindIndex + 1}");

    private static bool IsAutoName(TrackKind kind, string name) =>
        name.Length >= 2 && name[0] == (kind == TrackKind.Video ? 'V' : 'A') && name.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;

    private static void RenumberAutoNames(Work w)
    {
        foreach (var kind in new[] { TrackKind.Video, TrackKind.Audio })
        {
            var tracks = w.OfKind(kind);
            for (var i = 0; i < tracks.Count; i++)
            {
                var name = AutoName(kind, i);
                if (IsAutoName(kind, tracks[i].Name) && tracks[i].Name != name)
                {
                    tracks[i].Props = tracks[i].Props with { Name = name };
                }
            }
        }
    }
}
