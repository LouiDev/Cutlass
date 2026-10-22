using Cutlass.Core.Model;

namespace Cutlass.App.Services;

/// <summary>
/// The pure transport rules of DESIGN 6 and 7: which range Space and Shift+Space play, the L shuttle speeds, and which
/// project changes need a playback restart (TECH_BRIEF 8.2: mixer changes apply live, timeline edits restart).
/// </summary>
public static class PlaybackPlanner
{
    /// <summary>The fastest shuttle speed (TECH_BRIEF 8.2: 1x, 2x and 4x).</summary>
    public const int MaxSpeed = 4;

    /// <summary>The next L speed: 1x when stopped, then 2x and 4x, then 4x stays.</summary>
    /// <param name="playing">True while playing.</param>
    /// <param name="speed">The current speed.</param>
    /// <returns>The new speed.</returns>
    public static int NextShuttleSpeed(bool playing, int speed) =>
        !playing || speed < 1 ? 1 : Math.Min(MaxSpeed, speed * 2);

    /// <summary>
    /// The range Space plays from the playhead. Past the end it starts over at frame 0. With loop on and an In or Out
    /// point it plays and repeats In to Out (starting at In when the playhead is outside); with loop on and no range it
    /// repeats the whole sequence.
    /// </summary>
    /// <param name="playhead">The playhead.</param>
    /// <param name="duration">The sequence length in frames.</param>
    /// <param name="inPoint">The In point, or null.</param>
    /// <param name="outPoint">The Out point (exclusive), or null.</param>
    /// <param name="loop">True when loop is on.</param>
    /// <returns>The range, or null when there is nothing to play.</returns>
    public static PlayRange? ForPlayhead(long playhead, long duration, long? inPoint, long? outPoint, bool loop)
    {
        if (duration <= 0)
        {
            return null;
        }

        if (loop && (inPoint is not null || outPoint is not null))
        {
            var lo = Math.Clamp(inPoint ?? 0, 0, duration);
            var hi = Math.Clamp(outPoint ?? duration, 0, duration);
            if (hi > lo)
            {
                var start = playhead >= lo && playhead < hi ? playhead : lo;
                return new PlayRange(start, hi, true, lo);
            }
        }

        var from = playhead >= 0 && playhead < duration ? playhead : 0;
        return new PlayRange(from, duration, loop, loop ? 0 : null);
    }

    /// <summary>The range Shift+Space plays: In to Out (the sequence start and end stand in for missing points).</summary>
    /// <param name="duration">The sequence length in frames.</param>
    /// <param name="inPoint">The In point, or null.</param>
    /// <param name="outPoint">The Out point (exclusive), or null.</param>
    /// <param name="loop">True when loop is on.</param>
    /// <returns>The range, or null when it is empty.</returns>
    public static PlayRange? ForInToOut(long duration, long? inPoint, long? outPoint, bool loop)
    {
        if (duration <= 0)
        {
            return null;
        }

        var lo = Math.Clamp(inPoint ?? 0, 0, duration);
        var hi = Math.Clamp(outPoint ?? duration, 0, duration);
        return hi > lo ? new PlayRange(lo, hi, loop, loop ? lo : null) : null;
    }

    /// <summary>
    /// True when nothing that playback renders changed between two projects: only mute, solo, gain (applied live by
    /// the mixer) or fields the render ignores (track name, lock, target, markers, In/Out) differ.
    /// </summary>
    /// <param name="old">The project playback runs on.</param>
    /// <param name="current">The new project.</param>
    /// <returns>True when no restart is needed.</returns>
    public static bool IsRenderUnchanged(Project old, Project current)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(current);
        if (ReferenceEquals(old, current))
        {
            return true;
        }

        // Edits rebuild the immutable arrays even when their items are unchanged, so compare item by item.
        if (!old.Media.SequenceEqual(current.Media) || old.Sequence.Settings != current.Sequence.Settings)
        {
            return false;
        }

        var a = old.Sequence.Tracks;
        var b = current.Sequence.Tracks;
        if (a == b)
        {
            return true;
        }

        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            var x = a[i];
            var y = b[i];
            if (!ReferenceEquals(x, y) && (x.Id != y.Id || x.Kind != y.Kind || x.Hidden != y.Hidden
                || !x.Clips.SequenceEqual(y.Clips) || !x.Transitions.SequenceEqual(y.Transitions)))
            {
                return false;
            }
        }

        return true;
    }
}
