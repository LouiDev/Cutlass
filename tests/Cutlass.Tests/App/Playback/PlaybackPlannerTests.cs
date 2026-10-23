using Cutlass.App.Services.Playback;
using Cutlass.Core.Editing;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App.Playback;

public sealed class PlaybackPlannerTests
{
    [Theory]
    [InlineData(false, 4, 1)]
    [InlineData(true, 1, 2)]
    [InlineData(true, 2, 4)]
    [InlineData(true, 4, 4)]
    public void ShuttleSpeeds(bool playing, int speed, int expected) =>
        Assert.Equal(expected, PlaybackPlanner.NextShuttleSpeed(playing, speed));

    [Fact]
    public void SpaceRanges()
    {
        Assert.Null(PlaybackPlanner.ForPlayhead(0, 0, null, null, loop: false));
        Assert.Equal(new PlayRange(10, 100, false, null), PlaybackPlanner.ForPlayhead(10, 100, null, null, loop: false));
        Assert.Equal(new PlayRange(0, 100, false, null), PlaybackPlanner.ForPlayhead(100, 100, null, null, loop: false));
        Assert.Equal(new PlayRange(10, 100, true, 0), PlaybackPlanner.ForPlayhead(10, 100, null, null, loop: true));

        // Loop with a range: inside keeps the playhead, outside starts at In.
        Assert.Equal(new PlayRange(30, 50, true, 20), PlaybackPlanner.ForPlayhead(30, 100, 20, 50, loop: true));
        Assert.Equal(new PlayRange(20, 50, true, 20), PlaybackPlanner.ForPlayhead(70, 100, 20, 50, loop: true));
        Assert.Equal(new PlayRange(0, 40, true, 0), PlaybackPlanner.ForPlayhead(45, 100, null, 40, loop: true));

        // Without loop In/Out do not limit Space.
        Assert.Equal(new PlayRange(70, 100, false, null), PlaybackPlanner.ForPlayhead(70, 100, 20, 50, loop: false));
    }

    [Fact]
    public void InToOutRanges()
    {
        Assert.Equal(new PlayRange(20, 50, false, null), PlaybackPlanner.ForInToOut(100, 20, 50, loop: false));
        Assert.Equal(new PlayRange(20, 100, true, 20), PlaybackPlanner.ForInToOut(100, 20, null, loop: true));
        Assert.Equal(new PlayRange(0, 100, false, null), PlaybackPlanner.ForInToOut(100, null, 500, loop: false));
        Assert.Null(PlaybackPlanner.ForInToOut(100, 60, 60, loop: false));
    }

    [Fact]
    public void MixerAndUiOnlyChangesNeedNoRestart()
    {
        var project = new ProjectBuilder().With(V1, V("v1", 0, 100)).With(A1, A("a1", 0, 100)).Build();
        var muted = TimelineEdits.SetTrack(project, A1, t => t with { Muted = true, Solo = true, GainDb = -6 }, "Mix").Project;
        Assert.NotSame(project, muted);
        Assert.True(PlaybackPlanner.IsRenderUnchanged(project, muted));

        var locked = TimelineEdits.SetTrack(muted, V1, t => t with { Locked = true, Targeted = false, Name = "Pictures" }, "UI").Project;
        Assert.True(PlaybackPlanner.IsRenderUnchanged(project, locked));

        var marked = TimelineEdits.SetInOut(locked, 10, 20).Project;
        Assert.True(PlaybackPlanner.IsRenderUnchanged(project, marked));
    }

    [Fact]
    public void RenderChangesNeedARestart()
    {
        var project = new ProjectBuilder().With(V1, V("v1", 0, 100)).With(A1, A("a1", 0, 100)).Build();
        var hidden = TimelineEdits.SetTrack(project, V1, t => t with { Hidden = true }, "Hide").Project;
        Assert.False(PlaybackPlanner.IsRenderUnchanged(project, hidden));

        var moved = TimelineEdits.Nudge(new SequentialIdGenerator(1), project, ["v1"], 5).Project;
        Assert.False(PlaybackPlanner.IsRenderUnchanged(project, moved));
    }
}
