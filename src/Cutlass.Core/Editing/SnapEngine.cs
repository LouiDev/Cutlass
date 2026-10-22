using System.Collections.Immutable;
using Cutlass.Core.Model;

namespace Cutlass.Core.Editing;

/// <summary>
/// Snapping (DESIGN 5.5): targets are the playhead, every clip edge, markers, In/Out and frame 0. The 8 px threshold is
/// converted to frames at the current zoom, and the nearest candidate over all moving edges wins.
/// </summary>
public static class SnapEngine
{
    /// <summary>The default snap distance in pixels.</summary>
    public const double DefaultThresholdPx = 8;

    /// <summary>Collects the snap targets of a project, sorted and distinct.</summary>
    /// <param name="project">The project.</param>
    /// <param name="playhead">The playhead frame.</param>
    /// <param name="excludeIds">Clips whose edges are not targets (the ones being dragged), or null.</param>
    /// <returns>The target frames in ascending order.</returns>
    public static ImmutableArray<long> Collect(Project project, long playhead, IReadOnlyCollection<string>? excludeIds = null)
    {
        var targets = new SortedSet<long> { 0, playhead };
        foreach (var track in project.Sequence.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (excludeIds is not null && excludeIds.Contains(clip.Id))
                {
                    continue;
                }

                targets.Add(clip.Start);
                targets.Add(clip.End);
            }
        }

        foreach (var marker in project.Sequence.Markers)
        {
            targets.Add(marker.Frame);
        }

        if (project.Sequence.InFrame is { } inFrame)
        {
            targets.Add(inFrame);
        }

        if (project.Sequence.OutFrame is { } outFrame)
        {
            targets.Add(outFrame);
        }

        return [.. targets];
    }

    /// <summary>
    /// Snaps a drag. Each moving edge lands at <c>edge + delta</c>; the closest target within the threshold over all
    /// edges engages, and the returned delta puts that edge exactly on it. Ties go to the earlier edge, then the
    /// earlier target.
    /// </summary>
    /// <param name="movingEdges">The frames of the edges being dragged, before the delta.</param>
    /// <param name="delta">The unsnapped delta in frames.</param>
    /// <param name="targets">Sorted targets from <see cref="Collect"/>.</param>
    /// <param name="pixelsPerFrame">The zoom (0.01 to 40).</param>
    /// <param name="thresholdPx">The snap distance in pixels.</param>
    /// <returns>The snapped delta and the target frame, or the original delta when nothing is in range.</returns>
    public static SnapResult Resolve(
        IReadOnlyList<long> movingEdges, long delta, ImmutableArray<long> targets, double pixelsPerFrame, double thresholdPx = DefaultThresholdPx)
    {
        if (targets.IsDefaultOrEmpty || movingEdges.Count == 0 || pixelsPerFrame <= 0 || double.IsNaN(pixelsPerFrame))
        {
            return new SnapResult(delta, null);
        }

        var thresholdFrames = thresholdPx / pixelsPerFrame;
        long? bestTarget = null;
        long bestEdge = 0;
        var bestDistance = double.MaxValue;
        foreach (var edge in movingEdges)
        {
            var position = edge + delta;
            var index = targets.BinarySearch(position);
            if (index < 0)
            {
                index = ~index;
            }

            for (var i = Math.Max(0, index - 1); i <= Math.Min(targets.Length - 1, index); i++)
            {
                var distance = Math.Abs((double)targets[i] - position);
                if (distance <= thresholdFrames && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTarget = targets[i];
                    bestEdge = edge;
                }
            }
        }

        return bestTarget is { } target ? new SnapResult(target - bestEdge, target) : new SnapResult(delta, null);
    }
}
