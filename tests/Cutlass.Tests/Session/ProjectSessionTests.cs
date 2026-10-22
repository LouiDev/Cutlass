using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Session;

public sealed class ProjectSessionTests
{
    private readonly List<ProjectChangedEventArgs> _events = [];
    private long _now = 10_000;

    private ProjectSession NewSession(Project? initial = null, int capacity = UndoHistory.DefaultCapacity)
    {
        var session = new ProjectSession(initial ?? Initial(), () => _now, capacity);
        session.Changed += (_, e) => _events.Add(e);
        return session;
    }

    private static Project Initial() => new ProjectBuilder().With(V1, V("c1", 0, 50), V("c2", 50, 50, sourceFrame: 100)).Build();

    private static Func<Project, EditResult> Opacity(double value) =>
        p => TimelineEdits.UpdateClips(p, ["c1"], c => ((MediaClip)c) with { Video = ((MediaClip)c).Video with { Opacity = value } }, "Opacity");

    private static double OpacityOf(Project p) => ModelAssert.Media(p, "c1").Video.Opacity;

    [Fact]
    public void ExecuteRecordsAndUndoRedoRestoreReferences()
    {
        var session = NewSession();
        var initial = session.Current;
        var result = session.Execute(p => TimelineEdits.Lift(p, ["c2"]));
        var after = session.Current;

        Assert.Same(result.Project, after);
        Assert.True(session.IsDirty);
        Assert.Equal("Lift", session.UndoLabel);
        Assert.Null(session.RedoLabel);
        Assert.True(session.Undo());
        Assert.Same(initial, session.Current);
        Assert.Equal("Lift", session.RedoLabel);
        Assert.True(session.Redo());
        Assert.Same(after, session.Current);
        Assert.False(session.Redo());
        Assert.Equal([ChangeKind.Edit, ChangeKind.Undo, ChangeKind.Redo], _events.Select(e => e.Kind));
        Assert.Same(initial, _events[0].Old);
        Assert.Same(after, _events[0].New);
    }

    [Fact]
    public void UnchangedResultsAreNotRecorded()
    {
        var session = NewSession();
        var result = session.Execute(p => TimelineEdits.Lift(p, ["nope"]));
        Assert.Same(session.Current, result.Project);
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);
        Assert.Empty(_events);
        Assert.False(session.Undo());
    }

    [Fact]
    public void ANewEditClearsTheRedoStack()
    {
        var session = NewSession();
        session.Execute(Opacity(0.5));
        session.Undo();
        Assert.True(session.CanRedo);
        session.Execute(Opacity(0.25));
        Assert.False(session.CanRedo);
        Assert.Equal(1, session.UndoCount);
    }

    [Fact]
    public void SameKeyWithinTheWindowCoalesces()
    {
        var session = NewSession();
        var initial = session.Current;
        session.Execute(Opacity(0.9), "inspector:c1:opacity");
        _now += 1000;
        session.Execute(Opacity(0.8), "inspector:c1:opacity");
        _now += 1400;
        session.Execute(Opacity(0.7), "inspector:c1:opacity");

        Assert.Equal(1, session.UndoCount);
        Assert.Equal(0.7, OpacityOf(session.Current));
        session.Undo();
        Assert.Same(initial, session.Current);
    }

    [Fact]
    public void CoalescingNeedsTheSameKeyAndTheWindow()
    {
        var session = NewSession();
        session.Execute(Opacity(0.9), "inspector:c1:opacity");
        _now += 1501;
        session.Execute(Opacity(0.8), "inspector:c1:opacity");
        _now += 100;
        session.Execute(Opacity(0.7), "inspector:c1:saturation");
        _now += 100;
        session.Execute(Opacity(0.6));
        _now += 100;
        session.Execute(Opacity(0.5));
        Assert.Equal(5, session.UndoCount);
    }

    [Fact]
    public void CoalescingBackToTheStartDropsTheEntry()
    {
        var session = NewSession();
        var initial = session.Current;
        session.Execute(p => TimelineEdits.Nudge(new SequentialIdGenerator(), p, ["c2"], 1), "nudge:c2");
        _now += 200;
        session.Execute(_ => new EditResult(initial, "Nudge", [], []), "nudge:c2");
        Assert.Same(initial, session.Current);
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void GestureUpdatesLiveAndCommitsOneEntry()
    {
        var session = NewSession();
        var initial = session.Current;
        session.BeginGesture();
        for (var d = 1; d <= 10; d++)
        {
            var delta = d;
            session.UpdateGesture(start => TimelineEdits.Trim(start, "c1", ClipEdge.End, -delta, false, false));
            Assert.Equal(50 - d, ModelAssert.Clip(session.Current, "c1").Duration);
        }

        Assert.False(session.CanUndo);
        session.CommitGesture("Trim");
        Assert.Equal(1, session.UndoCount);
        Assert.Equal("Trim", session.UndoLabel);
        Assert.Equal(10, _events.Count(e => e.Kind == ChangeKind.GestureUpdate));
        Assert.Equal(ChangeKind.GestureCommit, _events[^1].Kind);
        Assert.Same(initial, _events[^1].Old);
        session.Undo();
        Assert.Same(initial, session.Current);
    }

    [Fact]
    public void CancelRestoresTheGestureStart()
    {
        var session = NewSession();
        var initial = session.Current;
        session.BeginGesture();
        session.UpdateGesture(start => TimelineEdits.Trim(start, "c1", ClipEdge.End, -5, false, false));
        session.CancelGesture();
        Assert.Same(initial, session.Current);
        Assert.False(session.CanUndo);
        Assert.Equal(ChangeKind.GestureCancel, _events[^1].Kind);
        Assert.False(session.InGesture);
    }

    [Fact]
    public void AnUnchangedGestureRecordsNothing()
    {
        var session = NewSession();
        session.BeginGesture();
        session.UpdateGesture(start => TimelineEdits.Trim(start, "c1", ClipEdge.End, -5, false, false));
        session.UpdateGesture(start => TimelineEdits.Trim(start, "c1", ClipEdge.End, 0, false, false));
        session.CommitGesture("Trim");
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void ExecuteAndUndoDuringAGestureCancelItFirst()
    {
        var session = NewSession();
        var initial = session.Current;
        session.Execute(Opacity(0.5));
        var afterEdit = session.Current;
        session.BeginGesture();
        session.UpdateGesture(start => TimelineEdits.Trim(start, "c1", ClipEdge.End, -5, false, false));
        Assert.True(session.Undo());
        Assert.Same(initial, session.Current);
        Assert.False(session.InGesture);

        session.Redo();
        Assert.Same(afterEdit, session.Current);
        session.UpdateGesture(start => TimelineEdits.Trim(start, "c1", ClipEdge.End, -5, false, false));
        session.Execute(Opacity(0.25));
        Assert.Equal(50, ModelAssert.Clip(session.Current, "c1").Duration);
        Assert.Equal(2, session.UndoCount);
    }

    [Fact]
    public void HistoryKeepsTheNewest500Entries()
    {
        var session = NewSession();
        for (var i = 1; i <= 520; i++)
        {
            var value = i / 1000.0;
            _now += 2000;
            session.Execute(Opacity(value));
        }

        Assert.Equal(500, session.UndoCount);
        while (session.Undo())
        {
        }

        Assert.Equal(0.020, OpacityOf(session.Current), 9);
    }

    [Fact]
    public void DirtyStateFollowsTheSavedReference()
    {
        var session = NewSession();
        Assert.False(session.IsDirty);
        session.Execute(Opacity(0.5));
        Assert.True(session.IsDirty);
        session.MarkSaved();
        Assert.False(session.IsDirty);
        Assert.Equal(ChangeKind.Saved, _events[^1].Kind);
        session.Undo();
        Assert.True(session.IsDirty);
        session.Redo();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void ReplaceClearsHistory()
    {
        var session = NewSession();
        session.Execute(Opacity(0.5));
        var loaded = new ProjectBuilder().Build();
        session.Replace(loaded);
        Assert.Same(loaded, session.Current);
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);
        Assert.Equal(ChangeKind.Replace, _events[^1].Kind);

        session.Replace(Initial(), markSaved: false);
        Assert.True(session.IsDirty);
        session.MarkSaved();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void UndoHistoryCoalescesOnlyOntoTheMatchingTop()
    {
        var history = new UndoHistory(3);
        var a = new ProjectBuilder().Build();
        var b = a with { };
        var c = a with { };
        history.Push(new HistoryEntry(a, b, "One", "k", 0));
        Assert.False(history.TryCoalesce(new HistoryEntry(a, c, "Two", "k", 100)));    // Before is not the top's After
        Assert.True(history.TryCoalesce(new HistoryEntry(b, c, "Two", "k", 100)));
        Assert.Same(a, history.NextUndo!.Before);
        Assert.Same(c, history.NextUndo.After);
        Assert.Equal("Two", history.NextUndo.Label);
        Assert.False(history.TryCoalesce(new HistoryEntry(c, a, "Three", null, 200)));
        history.Push(new HistoryEntry(c, a, "x", null, 0));
        history.Push(new HistoryEntry(a, b, "y", null, 0));
        history.Push(new HistoryEntry(b, c, "z", null, 0));
        Assert.Equal(3, history.UndoCount);
        Assert.Equal("z", history.Undo()!.Label);
        Assert.Equal(1, history.RedoCount);
        history.Clear();
        Assert.Null(history.Undo());
        Assert.Null(history.Redo());
    }
}
