using System.Collections.Immutable;
using Cutlass.Core.Model;

namespace Cutlass.Media.Preview;

/// <summary>
/// An immutable snapshot of the live mixer (TECH_BRIEF 8.2): mute, solo and gain per audio track plus a linear master
/// factor. Playback renders every audio track as its own stereo pair (PerTrack) and <see cref="AudioMixer"/> applies
/// this state while mixing, so a change takes effect within the queued audio (about 128 ms) without a restart.
/// </summary>
/// <param name="Tracks">The audio tracks, in sequence order (A1 first). Tracks missing here play unmuted at 0 dB.</param>
/// <param name="Master">The linear master factor applied to the sum (1 is unity).</param>
public sealed record MixerState(ImmutableArray<MixerTrack> Tracks, double Master)
{
    /// <summary>No tracks, unity master: every track plays at 0 dB.</summary>
    public static MixerState Neutral { get; } = new([], 1.0);

    /// <summary>True when any track is soloed.</summary>
    public bool AnySolo => !Tracks.IsDefaultOrEmpty && Tracks.Any(t => t.Solo);

    /// <summary>The mixer state of a sequence's audio tracks (mute, solo and gain as the model holds them).</summary>
    /// <param name="sequence">The sequence.</param>
    /// <param name="master">The linear master factor.</param>
    /// <returns>The state.</returns>
    public static MixerState FromSequence(Sequence sequence, double master = 1.0)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        var tracks = sequence.Tracks
            .Where(t => t.Kind == TrackKind.Audio)
            .Select(t => new MixerTrack(t.Id, t.Muted, t.Solo, t.GainDb))
            .ToImmutableArray();
        return new MixerState(tracks, master);
    }

    /// <summary>
    /// A track is active when it is not muted and either nothing is soloed or it is soloed (TECH_BRIEF 8.2). A track
    /// this state does not know is active unless something is soloed.
    /// </summary>
    /// <param name="trackId">The track id.</param>
    /// <returns>True when the track is heard.</returns>
    public bool IsActive(string? trackId)
    {
        var track = Find(trackId);
        if (track is null)
        {
            return !AnySolo;
        }

        return !track.Muted && (!AnySolo || track.Solo);
    }

    /// <summary>The settings of a track, or null when the state does not list it.</summary>
    /// <param name="trackId">The track id.</param>
    /// <returns>The settings.</returns>
    public MixerTrack? Find(string? trackId)
    {
        if (trackId is null || Tracks.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var track in Tracks)
        {
            if (string.Equals(track.TrackId, trackId, StringComparison.Ordinal))
            {
                return track;
            }
        }

        return null;
    }

    /// <summary>Returns a state with one track's settings replaced (or added at the end).</summary>
    /// <param name="track">The new settings.</param>
    /// <returns>The new state.</returns>
    public MixerState WithTrack(MixerTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var tracks = Tracks.IsDefault ? ImmutableArray<MixerTrack>.Empty : Tracks;
        for (var i = 0; i < tracks.Length; i++)
        {
            if (string.Equals(tracks[i].TrackId, track.TrackId, StringComparison.Ordinal))
            {
                return this with { Tracks = tracks.SetItem(i, track) };
            }
        }

        return this with { Tracks = tracks.Add(track) };
    }

    /// <summary>
    /// Writes mute, solo and gain into the snapshot's audio tracks. Used only by the Mixed fallback (TECH_BRIEF 7.6),
    /// where the graph applies them and a mixer change restarts playback.
    /// </summary>
    /// <param name="project">The snapshot.</param>
    /// <returns>The snapshot with this state applied.</returns>
    public Project ApplyTo(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var tracks = project.Sequence.Tracks.Select(t =>
        {
            if (t.Kind != TrackKind.Audio || Find(t.Id) is not { } m)
            {
                return t;
            }

            return t with { Muted = m.Muted, Solo = m.Solo, GainDb = m.GainDb };
        }).ToImmutableArray();
        return project with { Sequence = project.Sequence with { Tracks = tracks } };
    }
}
