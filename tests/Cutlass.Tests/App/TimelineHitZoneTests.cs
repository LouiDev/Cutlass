using Cutlass.App.Controls;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App;

/// <summary>The M6 hit zones (DESIGN 5.4): marker heads, keyframe dots, the volume line and transition ends.</summary>
public sealed class TimelineHitZoneTests
{
    private readonly Project _project;
    private readonly TimelineViewState _view;
    private readonly TrackLayout _layout;

    public TimelineHitZoneTests()
    {
        var keyed = A("a1", 0, 50) with { Audio = AudioProps.Default with { Keys = [new VolumeKey(10, 0)] } };
        _project = new ProjectBuilder()
            .With(V1, V("c1", 0, 50), V("c2", 50, 50, sourceFrame: 100))
            .With(A1, keyed)
            .Transition(V1, "x1", "c1", "c2", 20)
            .Marker("k1", 70)
            .Build();
        _view = new TimelineViewState();
        _view.SetViewport(1000, 400);
        _view.SetContent(100, 214);
        _layout = _view.Layout(_project.Sequence);
    }

    private double X(double frame) => TimelineViewState.HeaderWidth + _view.FrameToX(frame);

    private double Y(string trackId, double yInRow) => _view.ToControlY(_layout.Row(trackId)!.Top) + yInRow;

    private HitResult Hit(double x, double y, IReadOnlySet<string>? selected = null) =>
        HitTester.Hit(_project, _view, _layout, x, y, 1000, selected);

    [Fact]
    public void MarkerHeadsWinOverScrubbingInTheLowerRuler()
    {
        var hit = Hit(X(70) + 3, 20);
        Assert.Equal(HitZone.Marker, hit.Zone);
        Assert.Equal("k1", hit.MarkerId);
        Assert.Equal(HitZone.Ruler, Hit(X(70), 5).Zone);
        Assert.Equal(HitZone.Ruler, Hit(X(80), 20).Zone);
    }

    [Fact]
    public void TransitionBandsHaveResizableEnds()
    {
        var y = Y(V1, 28);
        Assert.Equal(HitZone.TransitionStart, Hit(X(40) + 1, y).Zone);
        Assert.Equal(HitZone.TransitionEnd, Hit(X(60) - 1, y).Zone);
        var middle = Hit(X(50), y);
        Assert.Equal(HitZone.Transition, middle.Zone);
        Assert.Equal("x1", middle.TransitionId);
        Assert.True(middle.IsTransition);
    }

    [Fact]
    public void KeyframeDotsAndTheVolumeLineNeedASelectedAudioClip()
    {
        var lineY = Y(A1, 1 + VolumeScale.DbToY(0, 0, 46));
        var key = Hit(X(10.5), lineY, new HashSet<string> { "a1" });
        Assert.Equal(HitZone.Keyframe, key.Zone);
        Assert.Equal(0, key.KeyIndex);
        Assert.Equal("a1", key.ClipId);
        Assert.True(key.IsClip);

        Assert.Equal(HitZone.VolumeLine, Hit(X(30), lineY + 3, new HashSet<string> { "a1" }).Zone);
        Assert.Equal(HitZone.ClipBody, Hit(X(30), Y(A1, 30), new HashSet<string> { "a1" }).Zone);
        Assert.Equal(HitZone.ClipBody, Hit(X(30), lineY).Zone);
        Assert.Equal(HitZone.ClipBody, Hit(X(10.5), lineY + 10).Zone);
    }

    [Fact]
    public void VolumeScalePutsZeroDecibelsAt83Percent()
    {
        Assert.Equal(100 * (1 - (60.0 / 72)), VolumeScale.DbToY(0, 0, 100), 9);
        Assert.Equal(0, VolumeScale.DbToY(12, 0, 100), 9);
        Assert.Equal(100, VolumeScale.DbToY(-80, 0, 100), 9);
        Assert.Equal(-6, VolumeScale.DbPerPixels(10, 120), 9);
        Assert.Equal("+3.5 dB", VolumeScale.Format(3.5));
        Assert.Equal("-6.0 dB", VolumeScale.Format(-6));
        Assert.Equal("0.0 dB", VolumeScale.Format(0));
    }

    [Fact]
    public void VolumeLevelsInterpolateBetweenKeyCentres()
    {
        var clip = A("a", 0, 100) with
        {
            Audio = AudioProps.Default with { GainDb = 2, Keys = [new VolumeKey(10, 0), new VolumeKey(20, -10)] },
        };
        Assert.Equal(2, VolumeScale.LevelAt(clip, 0), 9);
        Assert.Equal(-3, VolumeScale.LevelAt(clip, 15.5), 9);
        Assert.Equal(-8, VolumeScale.LevelAt(clip, 90), 9);
        Assert.Equal(new[] { 0 }, VolumeScale.SegmentKeys(clip, 5));
        Assert.Equal(new[] { 0, 1 }, VolumeScale.SegmentKeys(clip, 15));
        Assert.Equal(new[] { 1 }, VolumeScale.SegmentKeys(clip, 50));
        Assert.Empty(VolumeScale.SegmentKeys(A("b", 0, 10), 5));
        Assert.Equal(-1, VolumeScale.LevelAt(A("b", 0, 10) with { Audio = AudioProps.Default with { GainDb = -1 } }, 5), 9);
    }
}
