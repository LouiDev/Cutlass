using System.Collections.Immutable;

namespace Cutlass.Core.Model;

/// <summary>
/// The timeline: format settings, tracks, markers and the In/Out range. Positions are integer frames.
/// </summary>
/// <param name="Settings">Canvas size and frame rate.</param>
/// <param name="Tracks">Video and audio tracks, video tracks first (V1 upward), then audio tracks (A1 downward).</param>
/// <param name="Markers">Sequence markers, sorted by frame.</param>
/// <param name="InFrame">The In point, or null when unset.</param>
/// <param name="OutFrame">The Out point (exclusive), or null when unset.</param>
/// <param name="HasHadClips">
/// True once a media clip was placed (titles and generators do not count); sequence settings then change only through
/// the dialog, and the next media clip no longer sets them.
/// </param>
public sealed record Sequence(
    SequenceSettings Settings,
    ImmutableArray<Track> Tracks,
    ImmutableArray<Marker> Markers,
    long? InFrame,
    long? OutFrame,
    bool HasHadClips);
