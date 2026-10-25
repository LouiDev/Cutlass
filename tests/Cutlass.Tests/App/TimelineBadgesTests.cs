using Cutlass.App.Controls;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App;

/// <summary>The clip badge texts of the timeline (DESIGN 5.1): OFFLINE, speed, hold (effects batch 4), fx and sync.</summary>
public sealed class TimelineBadgesTests
{
    [Fact]
    public void FrozenClipsShowTheHoldBadgeNextToSpeedAndFx()
    {
        var fx = VideoProps.Default with { Opacity = 0.5 };
        var p = new ProjectBuilder()
            .With(V1, V("f1", 0, 50, sourceFrame: 100, freeze: true), V("c2", 50, 50, sourceFrame: 300, speed: 200) with { Video = fx })
            .With(A1, A("a1", 0, 50) with { Audio = AudioProps.Default with { Effects = AudioEffects.None with { Mono = true } } })
            .Build();

        Assert.Equal([("hold", false)], TimelineControl.Badges(p, ModelAssert.Clip(p, "f1"), offline: false));
        Assert.Equal([("OFFLINE", true), ("hold", false)], TimelineControl.Badges(p, ModelAssert.Clip(p, "f1"), offline: true));
        Assert.Equal([("200%", false), ("fx", false)], TimelineControl.Badges(p, ModelAssert.Clip(p, "c2"), offline: false));
        Assert.Equal([("hold", false), ("fx", false)], TimelineControl.Badges(p, ModelAssert.Media(p, "f1") with { Video = fx }, offline: false));
        Assert.Equal([("fx", false)], TimelineControl.Badges(p, ModelAssert.Clip(p, "a1"), offline: false));
        Assert.Empty(TimelineControl.Badges(p, Title("t1", 0, 10), offline: false));
    }

    [Fact]
    public void AFrozenStillShowsNoHoldBadge()
    {
        var p = new ProjectBuilder().With(V1, V("s1", 0, 50, media: Still, freeze: true)).Build();
        Assert.Empty(TimelineControl.Badges(p, ModelAssert.Clip(p, "s1"), offline: false));
    }

    [Fact]
    public void FrozenLinkedClipsShowNoSyncBadge()
    {
        // The audio leads the hold by 20 frames of source, which would be a red sync badge on moving clips.
        var p = new ProjectBuilder()
            .With(V1, V("f1", 0, 50, sourceFrame: 100, link: "l1", freeze: true), V("c2", 50, 50, sourceFrame: 300, link: "l2"))
            .With(A1, A("a1", 0, 50, sourceFrame: 120, link: "l1"), A("a2", 50, 50, sourceFrame: 320, link: "l2"))
            .Build();

        Assert.Equal([("hold", false)], TimelineControl.Badges(p, ModelAssert.Clip(p, "f1"), offline: false));
        Assert.Empty(TimelineControl.Badges(p, ModelAssert.Clip(p, "a1"), offline: false));
        Assert.Null(TimelineQuery.SyncOffset(p, "f1"));
        Assert.Null(TimelineQuery.SyncOffset(p, "a1"));
        Assert.Equal([("+20", true)], TimelineControl.Badges(p, ModelAssert.Clip(p, "a2"), offline: false));
    }
}
