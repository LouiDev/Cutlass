using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.Core.Session;

/// <summary>
/// Owns the current project and its undo history (TECH_BRIEF 3.4). UI thread only. Edits run through
/// <see cref="Execute"/>; drags run as gestures whose live updates are recorded as a single step on commit.
/// Selection and view state are not part of the session and are never undoable. Agent groups and the write gate live
/// in <c>ProjectSession.Group.cs</c> (AGENT_INTEGRATION 2.6).
/// </summary>
public sealed partial class ProjectSession
{
    private readonly Func<long> _clockMs;
    private readonly UndoHistory _history;
    private Project _saved;
    private Project? _gestureStart;
    private bool _deadGesture;

    /// <summary>Creates a session. The initial project counts as saved.</summary>
    /// <param name="initial">The initial project.</param>
    /// <param name="clockMs">A monotonic millisecond clock (for coalescing), for example <c>Environment.TickCount64</c>.</param>
    public ProjectSession(Project initial, Func<long> clockMs)
        : this(initial, clockMs, UndoHistory.DefaultCapacity)
    {
    }

    /// <summary>Creates a session with a custom history capacity.</summary>
    /// <param name="initial">The initial project.</param>
    /// <param name="clockMs">A monotonic millisecond clock.</param>
    /// <param name="historyCapacity">The maximum number of undo steps.</param>
    public ProjectSession(Project initial, Func<long> clockMs, int historyCapacity)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(clockMs);
        Current = initial;
        _saved = initial;
        _clockMs = clockMs;
        _history = new UndoHistory(historyCapacity);
    }

    /// <summary>Raised after every change of <see cref="Current"/> or of the saved state.</summary>
    public event EventHandler<ProjectChangedEventArgs>? Changed;

    /// <summary>The current project (live during a gesture).</summary>
    public Project Current { get; private set; }

    /// <summary>True when the current project is not the one last saved (reference comparison).</summary>
    public bool IsDirty => !ReferenceEquals(Current, _saved);

    /// <summary>True while a gesture is active (a dead gesture of a refused writer does not count).</summary>
    public bool InGesture => _gestureStart is not null;

    /// <summary>The label of the step Undo would revert, or null.</summary>
    public string? UndoLabel => _history.NextUndo?.Label;

    /// <summary>The label of the step Redo would re-apply, or null.</summary>
    public string? RedoLabel => _history.NextRedo?.Label;

    /// <summary>True when there is something to undo.</summary>
    public bool CanUndo => _history.UndoCount > 0;

    /// <summary>True when there is something to redo.</summary>
    public bool CanRedo => _history.RedoCount > 0;

    /// <summary>The number of undo steps held.</summary>
    public int UndoCount => _history.UndoCount;

    /// <summary>The number of redo steps held.</summary>
    public int RedoCount => _history.RedoCount;

    /// <summary>
    /// Runs an edit on <see cref="Current"/> and records it. An unchanged result records nothing. With a coalesce key
    /// equal to the top entry's within 1.5 s, the edit merges into that entry (keeping its older Before). A new edit
    /// clears the redo stack. An active gesture is cancelled first. Inside a group the edit is applied and raised, but
    /// recorded only by <see cref="EndGroup"/>. When the write gate refuses the current writer, nothing changes, the
    /// result is unchanged with the warning "An agent holds the project." and <see cref="WriteRefused"/> is raised.
    /// </summary>
    /// <param name="edit">The edit, for example <c>p =&gt; TimelineEdits.Lift(p, ids)</c>.</param>
    /// <param name="coalesceKey">For example <c>inspector:&lt;clipId&gt;:color.saturation</c>, or null.</param>
    /// <returns>The edit's result.</returns>
    public EditResult Execute(Func<Project, EditResult> edit, string? coalesceKey = null)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!IsWriteAllowed())
        {
            // The edit is pure, so running it only to learn its label changes nothing.
            var refused = edit(Current);
            WriteRefused?.Invoke(this, refused.Label);
            return EditResult.Unchanged(Current, refused.Label, RefusedMessage);
        }

        if (InGesture)
        {
            CancelGesture();
        }

        var before = Current;
        var result = edit(before);
        if (ReferenceEquals(result.Project, before))
        {
            return result;
        }

        // Inside a group EndGroup records one entry for everything, and nothing coalesces.
        if (!InGroup)
        {
            var entry = new HistoryEntry(before, result.Project, result.Label, coalesceKey, _clockMs());
            if (!_history.TryCoalesce(entry))
            {
                _history.Push(entry);
            }
        }

        Current = result.Project;
        Raise(before, ChangeKind.Edit);
        return result;
    }

    /// <summary>
    /// Starts a gesture (a drag). An active gesture is cancelled first. When the write gate refuses the current writer
    /// the gesture is dead: its updates and its commit do nothing, and <see cref="WriteRefused"/> is raised once.
    /// </summary>
    public void BeginGesture()
    {
        if (InGesture)
        {
            CancelGesture();
        }

        _deadGesture = false;
        if (!IsWriteAllowed())
        {
            _deadGesture = true;
            WriteRefused?.Invoke(this, "Gesture");
            return;
        }

        _gestureStart = Current;
    }

    /// <summary>
    /// Applies an edit to the gesture start and makes the result current without recording it. Each update replaces
    /// the previous one, so the drag delta is always relative to the gesture start. Without an active gesture this
    /// begins one. A dead gesture returns an unchanged result.
    /// </summary>
    /// <param name="fromGestureStart">The edit, applied to the project at gesture start.</param>
    /// <returns>The edit's result.</returns>
    public EditResult UpdateGesture(Func<Project, EditResult> fromGestureStart)
    {
        ArgumentNullException.ThrowIfNull(fromGestureStart);
        if (_gestureStart is null && !_deadGesture)
        {
            BeginGesture();
        }

        if (_gestureStart is null)
        {
            return EditResult.Unchanged(Current, "Gesture", RefusedMessage);
        }

        var result = fromGestureStart(_gestureStart);
        if (!ReferenceEquals(result.Project, Current))
        {
            var old = Current;
            Current = result.Project;
            Raise(old, ChangeKind.GestureUpdate);
        }

        return result;
    }

    /// <summary>
    /// Ends the gesture, recording one step from the gesture start to <see cref="Current"/> (none when unchanged).
    /// Inside a group the gesture becomes part of the group's entry.
    /// </summary>
    /// <param name="label">The undo label.</param>
    public void CommitGesture(string label)
    {
        _deadGesture = false;
        if (_gestureStart is not { } start)
        {
            return;
        }

        _gestureStart = null;
        if (ReferenceEquals(start, Current))
        {
            return;
        }

        if (!InGroup)
        {
            _history.Push(new HistoryEntry(start, Current, label, null, _clockMs()));
        }

        Raise(start, ChangeKind.GestureCommit);
    }

    /// <summary>Ends the gesture and restores the gesture start (Esc).</summary>
    public void CancelGesture()
    {
        _deadGesture = false;
        if (_gestureStart is not { } start)
        {
            return;
        }

        _gestureStart = null;
        if (ReferenceEquals(start, Current))
        {
            return;
        }

        var old = Current;
        Current = start;
        Raise(old, ChangeKind.GestureCancel);
    }

    /// <summary>
    /// Reverts the last step. An active gesture is cancelled first. Refused (false) inside a group, and when the write
    /// gate refuses the current writer (which raises <see cref="WriteRefused"/>).
    /// </summary>
    /// <returns>True when something was undone.</returns>
    public bool Undo()
    {
        if (!IsWriteAllowed())
        {
            WriteRefused?.Invoke(this, "Undo");
            return false;
        }

        if (InGroup)
        {
            return false;
        }

        CancelGesture();
        if (_history.Undo() is not { } entry)
        {
            return false;
        }

        var old = Current;
        Current = entry.Before;
        Raise(old, ChangeKind.Undo);
        return true;
    }

    /// <summary>
    /// Re-applies the last undone step. An active gesture is cancelled first. Refused (false) inside a group, and when
    /// the write gate refuses the current writer (which raises <see cref="WriteRefused"/>).
    /// </summary>
    /// <returns>True when something was redone.</returns>
    public bool Redo()
    {
        if (!IsWriteAllowed())
        {
            WriteRefused?.Invoke(this, "Redo");
            return false;
        }

        if (InGroup)
        {
            return false;
        }

        CancelGesture();
        if (_history.Redo() is not { } entry)
        {
            return false;
        }

        var old = Current;
        Current = entry.After;
        Raise(old, ChangeKind.Redo);
        return true;
    }

    /// <summary>Marks the current project as saved, so <see cref="IsDirty"/> becomes false. The write gate never refuses it.</summary>
    public void MarkSaved()
    {
        if (ReferenceEquals(_saved, Current))
        {
            return;
        }

        _saved = Current;
        Raise(Current, ChangeKind.Saved);
    }

    /// <summary>
    /// Replaces the project (open, new, recover) and clears the history and any open group. The project counts as
    /// saved unless <paramref name="markSaved"/> is false (a recovered autosave starts dirty). Does nothing when the
    /// write gate refuses the current writer (which raises <see cref="WriteRefused"/>).
    /// </summary>
    /// <param name="loaded">The new project.</param>
    /// <param name="markSaved">False to leave the project dirty.</param>
    public void Replace(Project loaded, bool markSaved = true)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        if (!IsWriteAllowed())
        {
            WriteRefused?.Invoke(this, "Replace");
            return;
        }

        _gestureStart = null;
        _deadGesture = false;
        _groupStart = null;
        _groupLabel = null;
        _history.Clear();
        var old = Current;
        Current = loaded;
        // A copy is a reference that is never current, so the session stays dirty until the next save.
        _saved = markSaved ? loaded : loaded with { };
        Raise(old, ChangeKind.Replace);
    }

    private void Raise(Project old, ChangeKind kind) => Changed?.Invoke(this, new ProjectChangedEventArgs(old, Current, kind));
}
