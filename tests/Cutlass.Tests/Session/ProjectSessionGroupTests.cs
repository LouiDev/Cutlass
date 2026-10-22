using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Session;

/// <summary>Groups and the write gate of <see cref="ProjectSession"/> (AGENT_INTEGRATION 2.6 and 7.1).</summary>
public sealed class ProjectSessionGroupTests
{
    private readonly List<ProjectChangedEventArgs> _events = [];
    private readonly List<string> _refused = [];
    private long _now = 10_000;

    private ProjectSession NewSession(Project? initial = null)
    {
        var session = new ProjectSession(initial ?? Initial(), () => _now);
        session.Changed += (_, e) => _events.Add(e);
        session.WriteRefused += (_, label) => _refused.Add(label);
        return session;
    }

    private static Project Initial() => new ProjectBuilder()
        .With(V1, V("c1", 0, 50), V("c2", 50, 50, sourceFrame: 100), V("c3", 100, 50, sourceFrame: 200))
        .Build();

    private static Func<Project, EditResult> Opacity(string id, double value) =>
        p => TimelineEdits.UpdateClips(p, [id], c => ((MediaClip)c) with { Video = ((MediaClip)c).Video with { Opacity = value } }, "Opacity");

    [Fact]
    public void AGroupRecordsOneEntryFromItsStartToTheEnd()
    {
        var session = NewSession();
        var start = session.Current;
        session.BeginGroup("Agent: Tidy");
        Assert.True(session.InGroup);
        Assert.Equal("Agent: Tidy", session.GroupLabel);
        session.Execute(p => TimelineEdits.Lift(p, ["c2"]));
        session.Execute(Opacity("c1", 0.5));
        Assert.Equal(0, session.UndoCount);
        Assert.Equal([ChangeKind.Edit, ChangeKind.Edit], _events.Select(e => e.Kind));
        var end = session.Current;

        Assert.True(session.EndGroup());
        Assert.False(session.InGroup);
        Assert.Equal(1, session.UndoCount);
        Assert.Equal("Agent: Tidy", session.UndoLabel);
        Assert.Equal(ChangeKind.GroupCommit, _events[^1].Kind);
        Assert.Same(start, _events[^1].Old);
        Assert.Same(end, _events[^1].New);

        Assert.True(session.Undo());
        Assert.Same(start, session.Current);
        Assert.True(session.Redo());
        Assert.Same(end, session.Current);
    }

    [Fact]
    public void CancelRestoresTheStartAndRecordsNothing()
    {
        var session = NewSession();
        session.Execute(Opacity("c3", 0.2));
        var start = session.Current;
        session.BeginGroup("Agent: Try");
        session.Execute(p => TimelineEdits.Lift(p, ["c1"]));
        session.CancelGroup();
        Assert.Same(start, session.Current);
        Assert.Equal(1, session.UndoCount);
        Assert.Equal("Opacity", session.UndoLabel);
        Assert.Equal(ChangeKind.GroupCancel, _events[^1].Kind);
    }

    [Fact]
    public void AnEmptyGroupRecordsAndRaisesNothing()
    {
        var session = NewSession();
        session.BeginGroup("Agent: Nothing");
        Assert.False(session.EndGroup());
        session.BeginGroup("Agent: Nothing");
        session.CancelGroup();
        Assert.Equal(0, session.UndoCount);
        Assert.Empty(_events);
    }

    [Fact]
    public void GroupsDoNotNestAndEndWithoutBeginIsANoOp()
    {
        var session = NewSession();
        Assert.False(session.EndGroup());
        session.CancelGroup();
        session.BeginGroup("Agent: A");
        Assert.Throws<InvalidOperationException>(() => session.BeginGroup("Agent: B"));
        Assert.Equal("Agent: A", session.GroupLabel);
    }

    [Fact]
    public void NothingCoalescesInsideAGroup()
    {
        var session = NewSession();
        session.Execute(Opacity("c1", 0.9), "inspector:c1:opacity");
        session.BeginGroup("Agent: Fade");
        session.Execute(Opacity("c1", 0.8), "inspector:c1:opacity");
        session.Execute(Opacity("c1", 0.7), "inspector:c1:opacity");
        session.EndGroup();
        Assert.Equal(2, session.UndoCount);
        session.Execute(Opacity("c1", 0.6), "inspector:c1:opacity");
        Assert.Equal(3, session.UndoCount);
    }

    [Fact]
    public void UndoAndRedoAreRefusedInsideAGroup()
    {
        var session = NewSession();
        session.Execute(p => TimelineEdits.Lift(p, ["c3"]));
        session.BeginGroup("Agent: X");
        session.Execute(p => TimelineEdits.Lift(p, ["c1"]));
        var inside = session.Current;
        Assert.False(session.Undo());
        Assert.False(session.Redo());
        Assert.Same(inside, session.Current);
        Assert.Empty(_refused);
    }

    [Fact]
    public void BeginGroupCancelsARunningGesture()
    {
        var session = NewSession();
        var start = session.Current;
        session.BeginGesture();
        session.UpdateGesture(p => TimelineEdits.Lift(p, ["c1"]));
        session.BeginGroup("Agent: G");
        Assert.False(session.InGesture);
        Assert.Same(start, session.Current);
    }

    [Fact]
    public void ReplaceClosesAnOpenGroup()
    {
        var session = NewSession();
        session.BeginGroup("Agent: G");
        session.Execute(p => TimelineEdits.Lift(p, ["c1"]));
        session.Replace(Initial());
        Assert.False(session.InGroup);
        Assert.Equal(0, session.UndoCount);
    }

    [Fact]
    public void TheGateRefusesUserEditsGesturesUndoAndReplace()
    {
        var session = NewSession();
        session.Execute(p => TimelineEdits.Lift(p, ["c3"]));
        var before = session.Current;
        var held = true;
        session.WriteGate = w => w == SessionWriter.Agent || !held;
        _events.Clear();

        var result = session.Execute(p => TimelineEdits.Lift(p, ["c1"]));
        Assert.Same(before, result.Project);
        Assert.Equal("Lift", result.Label);
        Assert.Contains(ProjectSession.RefusedMessage, result.Warnings);

        session.BeginGesture();
        Assert.False(session.InGesture);
        var update = session.UpdateGesture(p => TimelineEdits.Lift(p, ["c2"]));
        Assert.Same(before, update.Project);
        session.CommitGesture("Move");
        Assert.False(session.Undo());
        Assert.False(session.Redo());
        session.Replace(Initial());
        Assert.Same(before, session.Current);
        Assert.Equal(1, session.UndoCount);
        Assert.Empty(_events);
        Assert.Equal(["Lift", "Gesture", "Undo", "Redo", "Replace"], _refused);

        // MarkSaved is always allowed.
        session.MarkSaved();
        Assert.False(session.IsDirty);

        // The agent passes, and the user again once the gate opens.
        using (session.WriteAs(SessionWriter.Agent))
        {
            Assert.Equal(SessionWriter.Agent, session.Writer);
            Assert.NotSame(before, session.Execute(p => TimelineEdits.Lift(p, ["c1"])).Project);
        }

        Assert.Equal(SessionWriter.User, session.Writer);
        held = false;
        Assert.True(session.Undo());
    }

    [Fact]
    public void ARefusedBeginGroupOpensNoGroup()
    {
        var session = NewSession();
        session.WriteGate = w => w == SessionWriter.Agent;
        session.BeginGroup("Agent: X");
        Assert.False(session.InGroup);
        Assert.Equal(["Agent: X"], _refused);
    }

    [Theory]
    [InlineData(1, 300)]
    [InlineData(2, 300)]
    [InlineData(3, 300)]
    public void RandomGroupsKeepTheModelValidAndUndoRestoresEveryCommittedState(int seed, int steps)
    {
        var rng = new Random(seed);
        var ids = new SequentialIdGenerator();
        var session = NewSession(new ProjectBuilder()
            .With(V1, V("v1", 0, 40, link: "l_1"), V("v2", 40, 60, sourceFrame: 300, link: "l_2"), V("v3", 120, 30, sourceFrame: 900))
            .With(A1, A("a1", 0, 40, link: "l_1"), A("a2", 40, 60, sourceFrame: 300, link: "l_2"))
            .Build());
        var committed = new List<Project> { session.Current };
        for (var step = 0; step < steps; step++)
        {
            var roll = rng.Next(10);
            if (roll < 2 && !session.InGroup)
            {
                session.BeginGroup("Agent: G" + step);
            }
            else if (roll == 2 && session.InGroup)
            {
                if (session.EndGroup())
                {
                    committed.Add(session.Current);
                }
            }
            else if (roll == 3 && session.InGroup)
            {
                session.CancelGroup();
                Assert.Same(committed[^1], session.Current);
            }
            else
            {
                var before = session.Current;
                var result = session.Execute(RandomEdit(rng, ids, session.Current));
                Assert.True(TimelineValidator.IsValid(session.Current), string.Join("; ", TimelineValidator.Check(session.Current)));
                if (!session.InGroup && result.Changed(before))
                {
                    committed.Add(session.Current);
                }
            }
        }

        if (session.InGroup && session.EndGroup())
        {
            committed.Add(session.Current);
        }

        Assert.Equal(committed.Count - 1, session.UndoCount);
        for (var i = committed.Count - 1; i > 0; i--)
        {
            Assert.Same(committed[i], session.Current);
            Assert.True(session.Undo());
        }

        Assert.Same(committed[0], session.Current);
    }

    private static Func<Project, EditResult> RandomEdit(Random rng, IIdGenerator ids, Project project)
    {
        var clips = project.Sequence.Tracks.SelectMany(t => t.Clips).Select(c => c.Id).ToList();
        var pick = clips.Count == 0 ? "none" : clips[rng.Next(clips.Count)];
        var frame = rng.Next(0, 200);
        return rng.Next(6) switch
        {
            0 => p => TimelineEdits.Split(ids, p, [V1, A1], frame),
            1 => p => TimelineEdits.Move(ids, p, [pick], rng.Next(-20, 20), 0, EditMode.Overwrite),
            2 => p => TimelineEdits.Lift(p, [pick]),
            3 => p => TimelineEdits.RemoveRanges(ids, p, [(frame, frame + rng.Next(1, 30))], null, ripple: rng.Next(2) == 0),
            4 => p => TimelineEdits.Trim(p, pick, ClipEdge.End, rng.Next(-10, 10), ripple: false, includeLinked: true),
            _ => p => TimelineEdits.AddMarker(ids, p, frame),
        };
    }
}
