using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The single path from inspector fields to the session (TECH_BRIEF 3.4, 10.1). A typed or clicked change runs
/// <see cref="ProjectSession.Execute"/> with a coalesce key such as <c>inspector:&lt;clipId&gt;:color.saturation</c>, so
/// repeated changes of one field merge into one undo step; a scrubby drag runs between <see cref="BeginGesture"/> and
/// <see cref="EndGesture"/>, where every change is a <see cref="ProjectSession.UpdateGesture"/> from the gesture start
/// and the release records exactly one step.
/// </summary>
public sealed class InspectorEditor
{
    private EditResult? _gestureResult;

    /// <summary>Creates the editor.</summary>
    /// <param name="timeline">The timeline, which owns the session, the id generator and the warning toasts.</param>
    public InspectorEditor(TimelineViewModel timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        Timeline = timeline;
    }

    /// <summary>Raised when an edit left the project unchanged (refused or clamped to the old value); fields reload.</summary>
    public event EventHandler? Unchanged;

    /// <summary>The timeline view model.</summary>
    public TimelineViewModel Timeline { get; }

    /// <summary>The project session.</summary>
    public ProjectSession Session => Timeline.Session;

    /// <summary>True while a scrubby drag runs.</summary>
    public bool InGesture { get; private set; }

    /// <summary>The coalesce key of an inspector field for a set of targets (TECH_BRIEF 3.4).</summary>
    /// <param name="targetIds">The clip or transition ids (sorted ordinally and joined with commas).</param>
    /// <param name="field">The field, for example <c>color.saturation</c>.</param>
    /// <returns>For example <c>inspector:c_1:color.saturation</c>.</returns>
    public static string CoalesceKey(IEnumerable<string> targetIds, string field)
    {
        ArgumentNullException.ThrowIfNull(targetIds);
        return "inspector:" + string.Join(",", targetIds.Order(StringComparer.Ordinal)) + ":" + field;
    }

    /// <summary>
    /// Applies an edit. During a gesture it replaces the gesture's live result; otherwise it executes it with the
    /// coalesce key and reports its warnings.
    /// </summary>
    /// <param name="edit">The edit; during a gesture it receives the project at gesture start.</param>
    /// <param name="coalesceKey">The coalesce key, or null.</param>
    /// <returns>The result.</returns>
    public EditResult Apply(Func<Project, EditResult> edit, string? coalesceKey)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (InGesture && Session.InGesture)
        {
            var live = Session.UpdateGesture(edit);
            _gestureResult = live;
            return live;
        }

        InGesture = false;
        var before = Session.Current;
        var result = Timeline.Run(edit, coalesceKey);
        if (ReferenceEquals(before, Session.Current))
        {
            Unchanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    /// <summary>Starts a drag gesture (a scrubby field was dragged).</summary>
    public void BeginGesture()
    {
        Session.BeginGesture();
        _gestureResult = null;
        InGesture = true;
    }

    /// <summary>Ends a drag gesture: one undo step on commit, the start state restored on cancel (Esc).</summary>
    /// <param name="commit">True to record the drag, false to undo it.</param>
    /// <param name="label">The undo label.</param>
    public void EndGesture(bool commit, string label)
    {
        if (!InGesture)
        {
            return;
        }

        InGesture = false;
        if (commit)
        {
            Session.CommitGesture(label);
            if (_gestureResult is { } result)
            {
                Timeline.ReportWarnings(result);
            }
        }
        else
        {
            Session.CancelGesture();
        }

        _gestureResult = null;
    }
}
