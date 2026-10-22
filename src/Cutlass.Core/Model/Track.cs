using System.Collections.Immutable;

namespace Cutlass.Core.Model;

/// <summary>
/// One video or audio track. Clips are sorted by start and never overlap; transitions join butting clips on this track.
/// </summary>
/// <param name="Id">Track id (prefix <c>tr_</c>).</param>
/// <param name="Kind">Video or audio.</param>
/// <param name="Name">Display name, for example <c>V1</c>.</param>
/// <param name="Hidden">Video only: the track is not rendered.</param>
/// <param name="Locked">The track ignores edits.</param>
/// <param name="Targeted">The track receives source edits and Ctrl+K splits when nothing is selected.</param>
/// <param name="Muted">Audio only: the track is silent.</param>
/// <param name="Solo">Audio only: only soloed tracks are heard while any track is soloed.</param>
/// <param name="GainDb">Audio only: track gain in dB.</param>
/// <param name="Clips">The clips, sorted by start.</param>
/// <param name="Transitions">The transitions between butting clips.</param>
public sealed record Track(
    string Id,
    TrackKind Kind,
    string Name,
    bool Hidden,
    bool Locked,
    bool Targeted,
    bool Muted,
    bool Solo,
    double GainDb,
    ImmutableArray<Clip> Clips,
    ImmutableArray<Transition> Transitions);
