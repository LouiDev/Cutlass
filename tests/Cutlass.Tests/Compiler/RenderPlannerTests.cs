using Cutlass.Compiler.Planning;
using Cutlass.Core.Model;
using static Cutlass.Tests.Compiler.CompilerKit;

namespace Cutlass.Tests.Compiler;

/// <summary>RenderPlanner: runs, extents, widening and trimming back (TECH_BRIEF 7.4 and 7.5).</summary>
public sealed class RenderPlannerTests
{
    // A [0,150) and B [150,270) with a 30-frame crossfade: region [135,165). C [270,300) after a plain cut.
    private static readonly Track V1 = VideoTrack(
        "tr_v1",
        [V("c_a", 0, 150, "m_a", 2 * F), V("c_b", 150, 120, "m_b", 10 * F), V("c_c", 270, 30, "m_c", F)],
        [new Transition("x_1", TransitionKind.Crossfade, "c_a", "c_b", 30)]);

    [Fact]
    public void FullWindowHasEveryExtent()
    {
        var run = Assert.Single(RenderPlanner.PlanVideoTrack(V1, 0, 300));

        Assert.Equal(
            [("c_a", 0L, 165L), ("c_b", 135L, 270L), ("c_c", 270L, 300L)],
            run.Segments.Select(s => (s.Clip.Id, s.Start, s.End)));
        Assert.Equal(["x_1", null], run.Joins.Select(j => j?.Id));
        Assert.False(run.IsWidened);
        Assert.True(run.HasXfade);
        Assert.Equal(3, run.RunClipCount);
        Assert.True(run.RunHasTransition);
    }

    [Theory]
    [InlineData(150, 151, 135, 165, 15, 16)]
    [InlineData(140, 200, 135, 200, 5, 65)]
    [InlineData(100, 140, 100, 165, 0, 40)]
    [InlineData(160, 170, 135, 170, 25, 35)]
    [InlineData(135, 165, 135, 165, 0, 30)]
    public void WindowsInsideATransitionWidenAndTrimBack(long f0, long f1, long w0, long w1, long trimStart, long trimEnd)
    {
        var run = Assert.Single(RenderPlanner.PlanVideoTrack(V1, f0, f1));

        Assert.Equal((w0, w1), (run.RenderStart, run.RenderEnd));
        Assert.Equal((f0, f1), (run.VisibleStart, run.VisibleEnd));
        Assert.Equal((trimStart, trimEnd), (run.TrimStart, run.TrimEnd));
        Assert.Equal(f0 != w0 || f1 != w1, run.IsWidened);
        Assert.True(run.HasXfade);

        // The joined length equals the widened interval.
        var joined = run.Segments.Sum(s => s.Length) - run.Joins.Sum(j => j?.Duration ?? 0);
        Assert.Equal(w1 - w0, joined);
    }

    [Fact]
    public void WindowOutsideTheTransitionKeepsTheRunFacts()
    {
        var run = Assert.Single(RenderPlanner.PlanVideoTrack(V1, 200, 201));

        var segment = Assert.Single(run.Segments);
        Assert.Equal(("c_b", 135L, 270L, 200L, 201L), (segment.Clip.Id, segment.ExtentStart, segment.ExtentEnd, segment.Start, segment.End));
        Assert.Equal(65, segment.Offset);
        Assert.NotNull(segment.Head);
        Assert.False(run.HasXfade);
        Assert.Equal(3, run.RunClipCount);
        Assert.True(run.RunHasTransition);
    }

    [Fact]
    public void RunsOutsideTheWindowAreSkipped()
    {
        var track = VideoTrack("tr_v1", [V("c_1", 0, 30, "m_a"), V("c_2", 60, 30, "m_a"), V("c_3", 120, 30, "m_a")]);

        var runs = RenderPlanner.PlanVideoTrack(track, 40, 100);

        var run = Assert.Single(runs);
        Assert.Equal("c_2", run.Segments[0].Clip.Id);
        Assert.Equal(1, run.RunClipCount);
        Assert.Empty(RenderPlanner.PlanVideoTrack(track, 30, 60));
    }

    [Fact]
    public void DisabledClipsBreakRunsAndDropTheirTransitions()
    {
        var track = VideoTrack(
            "tr_v1",
            [V("c_1", 0, 30, "m_a", F), V("c_2", 30, 30, "m_b", F, enabled: false), V("c_3", 60, 30, "m_c", F)],
            [new Transition("x_1", TransitionKind.Crossfade, "c_1", "c_2", 10), new Transition("x_2", TransitionKind.Crossfade, "c_2", "c_3", 10)]);

        var runs = RenderPlanner.PlanVideoTrack(track, 0, 90);

        Assert.Equal(2, runs.Length);
        Assert.All(runs, r => Assert.Null(Assert.Single(r.Segments).Tail));
        Assert.Equal((0L, 30L), (runs[0].Segments[0].ExtentStart, runs[0].Segments[0].ExtentEnd));
        Assert.Equal((60L, 90L), (runs[1].Segments[0].ExtentStart, runs[1].Segments[0].ExtentEnd));
    }

    [Fact]
    public void AudioSegmentsAreCutToTheWindowWithoutWidening()
    {
        var track = AudioTrack(
            "tr_a1",
            [A("c_1", 0, 150, "m_a", 2 * F), A("c_2", 150, 120, "m_b", 10 * F)],
            [new Transition("x_1", TransitionKind.AudioCrossfade, "c_1", "c_2", 30)]);

        var segments = RenderPlanner.PlanAudioTrack(track, 150, 151);

        Assert.Equal([("c_1", 150L, 151L), ("c_2", 150L, 151L)], segments.Select(s => (s.Clip.Id, s.Start, s.End)));
        Assert.Equal((0L, 165L), (segments[0].ExtentStart, segments[0].ExtentEnd));
        Assert.Equal((135L, 270L), (segments[1].ExtentStart, segments[1].ExtentEnd));
        Assert.Equal(150, segments[0].Offset);
    }

    [Theory]
    [InlineData(30, 135, 165)]
    [InlineData(31, 135, 166)]
    [InlineData(2, 149, 151)]
    public void TransitionRegionSplitsTheDuration(long duration, long start, long end)
    {
        var transition = new Transition("x", TransitionKind.Crossfade, "a", "b", duration);

        Assert.Equal((start, end), RenderPlanner.TransitionRegion(transition, 150));
    }

    [Fact]
    public void InvalidTransitionsAreTreatedAsCuts()
    {
        var track = VideoTrack(
            "tr_v1",
            [V("c_1", 0, 4, "m_a", F), V("c_2", 4, 30, "m_b", F)],
            [new Transition("x_1", TransitionKind.Crossfade, "c_1", "c_2", 20)]);

        var run = Assert.Single(RenderPlanner.PlanVideoTrack(track, 0, 34));

        Assert.Equal(new Transition?[] { null }, run.Joins.ToArray());
        Assert.False(run.RunHasTransition);
    }
}
