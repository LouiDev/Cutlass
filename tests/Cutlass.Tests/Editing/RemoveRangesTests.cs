using System.Collections.Immutable;
using System.Diagnostics;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

/// <summary><see cref="TimelineEdits.RemoveRanges"/> (AGENT_INTEGRATION 2.6 and 7.1).</summary>
public sealed class RemoveRangesTests
{
    private static Project Sample() => new ProjectBuilder()
        .With(V1, V("v1", 0, 100, link: "l_1"), V("v2", 100, 100, sourceFrame: 500, link: "l_2"))
        .With(A1, A("a1", 0, 100, link: "l_1"), A("a2", 100, 100, sourceFrame: 500, link: "l_2"))
        .With(V2, Title("t1", 150, 40))
        .Marker("k_1", 120)
        .InOut(10, 20)
        .Build();

    /// <summary>The view model's In/Out ripple delete as it was before it moved into Core (the reference).</summary>
    private static EditResult OldRippleDeleteRange(IIdGenerator ids, Project project, long inFrame, long outFrame)
    {
        const string label = "Ripple Delete Range";
        var warnings = new List<string>();
        var unlocked = project.Sequence.Tracks.Where(t => !t.Locked).Select(t => t.Id).ToList();
        var current = project;

        void Step(EditResult r)
        {
            warnings.AddRange(r.Warnings);
            current = r.Project;
        }

        Step(TimelineEdits.Split(ids, current, unlocked, inFrame));
        Step(TimelineEdits.Split(ids, current, unlocked, outFrame));
        var inside = current.Sequence.Tracks.Where(t => !t.Locked).SelectMany(t => t.Clips)
            .Where(c => c.Start >= inFrame && c.End <= outFrame).Select(c => c.Id).ToList();
        if (inside.Count > 0)
        {
            Step(TimelineEdits.Lift(current, inside));
        }

        var after = current.Sequence.Tracks.Where(t => !t.Locked).SelectMany(t => t.Clips)
            .Where(c => c.Start >= outFrame).Select(c => c.Id).ToList();
        if (after.Count > 0)
        {
            Step(TimelineEdits.Move(ids, current, after, inFrame - outFrame, 0, EditMode.Overwrite));
        }

        Step(TimelineEdits.SetInOut(current, null, null));
        return ReferenceEquals(current, project)
            ? EditResult.Unchanged(project, label, [.. warnings])
            : new EditResult(current, label, [.. warnings.Distinct(StringComparer.Ordinal)], ImmutableArray<string>.Empty);
    }

    [Theory]
    [InlineData(30, 60)]
    [InlineData(90, 130)]
    [InlineData(0, 100)]
    [InlineData(150, 250)]
    public void OneRangeEqualsTheOldRippleDeleteRange(long start, long end)
    {
        var p = new ProjectBuilder()
            .With(V1, V("v1", 0, 100, link: "l_1"), V("v2", 100, 100, sourceFrame: 500, link: "l_2"))
            .With(A1, A("a1", 0, 100, link: "l_1"), A("a2", 100, 100, sourceFrame: 500, link: "l_2"))
            .With(V2, Title("t1", 150, 40))
            .Transition(V1, "x_1", "v1", "v2", 20)
            .Lock(A2)
            .Build();
        var expected = OldRippleDeleteRange(new SequentialIdGenerator(500), p, start, end);
        var actual = TimelineEdits.RemoveRanges(new SequentialIdGenerator(500), p, [(start, end)], null, ripple: true);
        Assert.Equal("Remove Ranges", actual.Label);
        ModelAssert.Valid(actual.Project);
        ModelAssert.Equivalent(expected.Project, actual.Project);
        Assert.Equal(expected.Warnings, actual.Warnings);
    }

    [Fact]
    public void RippleShortensTheTimelineByTheMergedRanges()
    {
        var p = Sample();
        var r = TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, [(50, 60), (10, 20), (15, 30), (190, 300)], null, ripple: true);
        var q = ModelAssert.Changed(p, r);
        // Merged: [10, 30) and [50, 60), plus [190, 200) at the end (the timeline ends at 200).
        Assert.Equal(200 - 20 - 10 - 10, TimelineQuery.Duration(q));
        Assert.Null(q.Sequence.InFrame);
        Assert.Null(q.Sequence.OutFrame);
        Assert.Equal(0, ModelAssert.Layout(q, V1)[0].Start);
        Assert.Equal(10, ModelAssert.Layout(q, V1)[0].Duration);
    }

    [Fact]
    public void UnsortedAndOverlappingRangesGiveTheSameResultAsSortedOnes()
    {
        var p = Sample();
        var a = TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, [(120, 140), (10, 40), (30, 50)], null, ripple: true);
        var b = TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, [(10, 50), (120, 140)], null, ripple: true);
        ModelAssert.Equivalent(a.Project, b.Project);
    }

    [Fact]
    public void MergeRangesClampsSortsAndJoinsTouchingRanges()
    {
        var merged = TimelineEdits.MergeRanges([(40, 50), (-5, 3), (3, 10), (20, 20), (45, 60), (70, 65)]);
        Assert.Equal([(0L, 10L), (40L, 60L)], merged);
    }

    [Fact]
    public void LiftLeavesGapsAndMovesNothing()
    {
        var p = Sample();
        var q = ModelAssert.Changed(p, TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, [(20, 40)], null, ripple: false));
        Assert.Equal(200, TimelineQuery.Duration(q));
        Assert.Equal([(0L, 20L), (40L, 60L), (100L, 100L)], ModelAssert.Spans(q, V1));
        Assert.Equal([(0L, 20L), (40L, 60L), (100L, 100L)], ModelAssert.Spans(q, A1));
    }

    [Fact]
    public void LockedTracksAreNeverCutOrShifted()
    {
        var p = new ProjectBuilder()
            .With(V1, V("v1", 0, 100))
            .With(A1, A("a1", 0, 100))
            .Lock(A1)
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, [(10, 30)], null, ripple: true));
        Assert.Equal([(0L, 10L), (10L, 70L)], ModelAssert.Spans(q, V1));
        Assert.Equal([(0L, 100L)], ModelAssert.Spans(q, A1));
    }

    [Fact]
    public void TheTrackFilterLimitsTheCut()
    {
        var p = Sample();
        var q = ModelAssert.Changed(p, TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, [(10, 30)], [A1], ripple: true));
        Assert.Equal([(0L, 100L), (100L, 100L)], ModelAssert.Spans(q, V1));
        Assert.Equal([(0L, 10L), (10L, 70L), (80L, 100L)], ModelAssert.Spans(q, A1));
    }

    [Fact]
    public void LinkedClipsAreCutOnBothTracksAndTransitionsSurviveOrGo()
    {
        var p = new ProjectBuilder()
            .With(V1, V("v1", 0, 100, link: "l_1"), V("v2", 100, 100, sourceFrame: 500, link: "l_2"))
            .With(A1, A("a1", 0, 100, link: "l_1"), A("a2", 100, 100, sourceFrame: 500, link: "l_2"))
            .Transition(V1, "x_1", "v1", "v2", 10)
            .Build();
        // A range far from the cut keeps the transition; one over the cut removes it.
        var keep = ModelAssert.Changed(p, TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, [(20, 40)], null, ripple: true));
        Assert.Single(ModelAssert.Track(keep, V1).Transitions);
        Assert.Equal(ModelAssert.Spans(keep, V1), ModelAssert.Spans(keep, A1));
        var gone = ModelAssert.Changed(p, TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, [(90, 110)], null, ripple: true));
        Assert.Empty(ModelAssert.Track(gone, V1).Transitions);
        Assert.Equal(180, TimelineQuery.Duration(gone));
    }

    [Fact]
    public void NoRangesOrEmptyRangesChangeNothing()
    {
        var p = Sample();
        ModelAssert.Unchanged(p, TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, [], null, ripple: true));
        ModelAssert.Unchanged(p, TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, [(30, 30), (40, 20)], null, ripple: true));
    }

    [Fact]
    public void ARangeOverEmptyTimeOnlyClearsInOut()
    {
        var p = Sample();
        var q = ModelAssert.Changed(p, TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, [(500, 600)], null, ripple: true));
        Assert.Null(q.Sequence.InFrame);
        Assert.Equal(TimelineQuery.Duration(p), TimelineQuery.Duration(q));
    }

    [Fact]
    public void ManyRangesOverAThousandClipsStayFast()
    {
        // AGENT_INTEGRATION 2.3: RemoveRanges over 1000 clips must stay linear. The old per-range ripple shift was
        // quadratic and took seconds here; the linear version takes about 20 ms on a desktop and about 60 ms on a shared
        // CI runner. The bound only has to separate those two worlds, so it is deliberately loose (Debug builds also
        // validate after every inner edit).
        var builder = new ProjectBuilder();
        var clips = Enumerable.Range(0, 500).Select(i => (Clip)V("v" + i, i * 20L, 20, sourceFrame: 0)).ToArray();
        var audio = Enumerable.Range(0, 500).Select(i => (Clip)A("a" + i, i * 20L, 20, sourceFrame: 0)).ToArray();
        var p = builder.With(V1, clips).With(A1, audio).Build();
        var ranges = Enumerable.Range(0, 50).Select(i => ((long)(i * 200) + 5, (long)(i * 200) + 15)).ToList();
        // Warm up the JIT on the same shapes first; the bound is about the steady state.
        TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, ranges, null, ripple: true);
        var watch = Stopwatch.StartNew();
        var r = TimelineEdits.RemoveRanges(new SequentialIdGenerator(), p, ranges, null, ripple: true);
        watch.Stop();
        Assert.Equal(10_000 - 500, TimelineQuery.Duration(r.Project));
#if DEBUG
        Assert.True(watch.ElapsedMilliseconds < 10_000, $"RemoveRanges took {watch.ElapsedMilliseconds} ms.");
#else
        Assert.True(watch.ElapsedMilliseconds < 1_000, $"RemoveRanges took {watch.ElapsedMilliseconds} ms.");
#endif
    }
}
