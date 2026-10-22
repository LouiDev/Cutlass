using Cutlass.Core.Model;

namespace Cutlass.Core.Editing;

/// <summary>Adding, resizing, changing and removing transitions.</summary>
public static partial class TimelineEdits
{
    /// <summary>
    /// Adds a transition centred on the cut at <paramref name="cutFrame"/> of a track (Ctrl+D, Ctrl+Shift+D), or
    /// updates the kind and duration of the one already there. The duration clamps to both clip lengths and both source
    /// handles, with a warning such as "Shortened to 12 frames: not enough source after intro.mov". The kind follows the
    /// track: audio tracks always get <see cref="TransitionKind.AudioCrossfade"/>, video tracks never do.
    /// </summary>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="trackId">The track.</param>
    /// <param name="cutFrame">The cut frame (left clip end, right clip start).</param>
    /// <param name="kind">The transition kind.</param>
    /// <param name="duration">The requested duration in frames.</param>
    /// <returns>The result; <see cref="EditResult.Select"/> holds the transition id.</returns>
    public static EditResult AddTransition(
        IIdGenerator ids, Project project, string trackId, long cutFrame, TransitionKind kind, long duration) =>
        AddTransitions(ids, project, [(trackId, cutFrame)], kind, duration);

    /// <summary>Adds or updates transitions on several cuts as one edit (see <see cref="AddTransition"/>).</summary>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="cuts">The track and cut frame pairs.</param>
    /// <param name="kind">The transition kind.</param>
    /// <param name="duration">The requested duration in frames.</param>
    /// <returns>The result; <see cref="EditResult.Select"/> holds the transition ids.</returns>
    public static EditResult AddTransitions(
        IIdGenerator ids, Project project, IReadOnlyList<(string TrackId, long CutFrame)> cuts, TransitionKind kind, long duration)
    {
        const string label = "Add Transition";
        var w = new Work(project, ids);
        var selected = new List<string>();
        foreach (var (trackId, cutFrame) in cuts)
        {
            var track = w.Track(trackId);
            if (track is null)
            {
                continue;
            }

            if (track.Locked)
            {
                w.Warn($"{track.Name} is locked.");
                continue;
            }

            var li = track.Clips.FindIndex(c => c.End == cutFrame);
            if (li < 0 || li + 1 >= track.Clips.Count || track.Clips[li + 1].Start != cutFrame)
            {
                w.Warn("There is no cut at that frame.");
                continue;
            }

            var left = track.Clips[li];
            var right = track.Clips[li + 1];
            var k = CoerceKind(track.Kind, kind);
            var d = Math.Max(MinTransitionFrames, duration);
            var max = Budget(w, track, left, right, null, out var reason);
            if (max < MinTransitionFrames)
            {
                w.Warn($"No room for a transition: {reason}.");
                continue;
            }

            if (d > max)
            {
                w.Warn($"Shortened to {max} frames: {reason}.");
                d = max;
            }

            var existing = track.Transitions.FindIndex(t => t.LeftClipId == left.Id && t.RightClipId == right.Id);
            if (existing >= 0)
            {
                track.Transitions[existing] = track.Transitions[existing] with { Kind = k, Duration = d };
                selected.Add(track.Transitions[existing].Id);
            }
            else
            {
                var transition = new Transition(ids.NewId(IIdGenerator.TransitionPrefix), k, left.Id, right.Id, d);
                track.Transitions.Add(transition);
                selected.Add(transition.Id);
            }
        }

        return selected.Count == 0 ? EditResult.Unchanged(project, label, [.. w.Warnings]) : w.Finish(label, selected);
    }

    /// <summary>Changes a transition's duration, clamped like <see cref="AddTransition"/> (minimum 2 frames).</summary>
    /// <param name="project">The project.</param>
    /// <param name="transitionId">The transition.</param>
    /// <param name="duration">The requested duration in frames.</param>
    /// <returns>The result.</returns>
    public static EditResult SetTransitionDuration(Project project, string transitionId, long duration)
    {
        const string label = "Transition Duration";
        var w = new Work(project);
        var track = w.Tracks.Find(t => t.Transitions.Exists(x => x.Id == transitionId));
        if (track is null)
        {
            return EditResult.Unchanged(project, label, "The transition no longer exists.");
        }

        if (track.Locked)
        {
            return EditResult.Unchanged(project, label, $"{track.Name} is locked.");
        }

        var index = track.Transitions.FindIndex(x => x.Id == transitionId);
        var transition = track.Transitions[index];
        var left = track.Clips.Find(c => c.Id == transition.LeftClipId);
        var right = track.Clips.Find(c => c.Id == transition.RightClipId);
        if (left is null || right is null)
        {
            return EditResult.Unchanged(project, label, "The transition no longer joins two clips.");
        }

        var d = duration;
        if (d < MinTransitionFrames)
        {
            w.Warn($"A transition is at least {MinTransitionFrames} frames long.");
            d = MinTransitionFrames;
        }

        var max = Budget(w, track, left, right, transitionId, out var reason);
        if (d > max)
        {
            w.Warn($"Shortened to {max} frames: {reason}.");
            d = Math.Max(MinTransitionFrames, max);
        }

        if (d == transition.Duration)
        {
            return EditResult.Unchanged(project, label, [.. w.Warnings]);
        }

        track.Transitions[index] = transition with { Duration = d };
        return w.Finish(label);
    }

    /// <summary>Changes the kind of transitions (coerced to fit each track's kind).</summary>
    /// <param name="project">The project.</param>
    /// <param name="transitionIds">The transitions.</param>
    /// <param name="kind">The new kind.</param>
    /// <returns>The result.</returns>
    public static EditResult SetTransitionKind(Project project, IReadOnlyCollection<string> transitionIds, TransitionKind kind)
    {
        const string label = "Transition Kind";
        var w = new Work(project);
        foreach (var track in w.Tracks)
        {
            for (var i = 0; i < track.Transitions.Count; i++)
            {
                if (!transitionIds.Contains(track.Transitions[i].Id))
                {
                    continue;
                }

                if (track.Locked)
                {
                    w.Warn($"{track.Name} is locked.");
                    continue;
                }

                track.Transitions[i] = track.Transitions[i] with { Kind = CoerceKind(track.Kind, kind) };
            }
        }

        return w.Finish(label);
    }

    /// <summary>Removes transitions.</summary>
    /// <param name="project">The project.</param>
    /// <param name="transitionIds">The transitions.</param>
    /// <returns>The result.</returns>
    public static EditResult RemoveTransitions(Project project, IReadOnlyCollection<string> transitionIds)
    {
        const string label = "Remove Transition";
        var w = new Work(project);
        foreach (var track in w.Tracks)
        {
            if (track.Locked && track.Transitions.Exists(t => transitionIds.Contains(t.Id)))
            {
                w.Warn($"{track.Name} is locked.");
                continue;
            }

            track.Transitions.RemoveAll(t => transitionIds.Contains(t.Id));
        }

        return w.Finish(label);
    }

    private static TransitionKind CoerceKind(TrackKind trackKind, TransitionKind kind) => trackKind switch
    {
        TrackKind.Audio => TransitionKind.AudioCrossfade,
        _ when kind == TransitionKind.AudioCrossfade => TransitionKind.Crossfade,
        _ => kind,
    };

    /// <summary>The transition budget on the working state, ignoring <paramref name="ignoreId"/> and this cut's own transition.</summary>
    private static long Budget(Work w, TrackWork track, Clip left, Clip right, string? ignoreId, out string reason)
    {
        long incomingB = 0, outgoingA = 0;
        foreach (var t in track.Transitions)
        {
            if (t.Id == ignoreId)
            {
                continue;
            }

            if (t.RightClipId == left.Id)
            {
                incomingB = TimelineQuery.TransitionHalves(t.Duration).B;
            }

            if (t.LeftClipId == right.Id)
            {
                outgoingA = TimelineQuery.TransitionHalves(t.Duration).A;
            }
        }

        var max = TimelineQuery.Budget(left.Duration - incomingB, w.Head(right), right.Duration - outgoingA, w.Tail(left), out var limit);
        reason = TimelineQuery.BudgetReason(limit, w.Name(left), w.Name(right));
        return max;
    }
}
