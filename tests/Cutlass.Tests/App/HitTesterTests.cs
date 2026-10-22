using Cutlass.App.Controls;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App;

public sealed class HitTesterTests
{
    private const double Ppf = 2;
    private readonly Project _project;
    private readonly TimelineViewState _view;
    private readonly TrackLayout _layout;

    public HitTesterTests()
    {
        _project = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, link: "l1"), V("c2", 50, 50, sourceFrame: 100), V("c3", 200, 5))
            .With(A1, A("a1", 0, 50, link: "l1"))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Build();
        _view = new TimelineViewState();
        _view.SetViewport(1000, 400);
        _view.SetContent(205, 214);
        _layout = _view.Layout(_project.Sequence);
    }

    private double X(double frame) => TimelineViewState.HeaderWidth + _view.FrameToX(frame);

    private double Y(string trackId, double yInRow) => _view.ToControlY(_layout.Row(trackId)!.Top) + yInRow;

    private HitResult Hit(double x, double y, long playhead = 1000) => HitTester.Hit(_project, _view, _layout, x, y, playhead);

    [Fact]
    public void TheDefaultZoomIsTwoPixelsPerFrame() => Assert.Equal(Ppf, _view.PixelsPerFrame);

    [Fact]
    public void RulerAndCorner()
    {
        Assert.Equal(HitZone.Ruler, Hit(X(10), 10).Zone);
        Assert.Equal(10, Hit(X(10), 10).Frame, 9);
        Assert.Equal(HitZone.None, Hit(50, 10).Zone);
    }

    [Fact]
    public void HeaderButtonsAndResizeEdge()
    {
        Assert.Equal(HitZone.HeaderTarget, Hit(10, Y(V1, 10)).Zone);
        Assert.Equal(HitZone.HeaderVisibility, Hit(110, Y(V1, 10)).Zone);
        Assert.Equal(HitZone.HeaderLock, Hit(130, Y(V1, 10)).Zone);
        Assert.Equal(HitZone.HeaderMute, Hit(85, Y(A1, 10)).Zone);
        Assert.Equal(HitZone.HeaderSolo, Hit(110, Y(A1, 10)).Zone);
        Assert.Equal(HitZone.HeaderResize, Hit(60, Y(V1, 54)).Zone);
        var header = Hit(60, Y(A2, 20));
        Assert.Equal(HitZone.Header, header.Zone);
        Assert.Equal(A2, header.TrackId);
    }

    [Fact]
    public void BodyAndEdges()
    {
        var body = Hit(X(25), Y(V1, 28));
        Assert.Equal(HitZone.ClipBody, body.Zone);
        Assert.Equal("c1", body.ClipId);
        Assert.Equal(V1, body.TrackId);
        Assert.Equal(HitZone.ClipStart, Hit(X(1), Y(V1, 28)).Zone);
        var end = Hit(X(99.5), Y(V1, 28));
        Assert.Equal(HitZone.ClipEnd, end.Zone);
        Assert.Equal("c2", end.ClipId);
    }

    [Fact]
    public void AtAJunctionEachClipKeepsItsOwnEdge()
    {
        var left = Hit(X(49.5), Y(V1, 12));
        var right = Hit(X(50.5), Y(V1, 12));
        Assert.Equal((HitZone.ClipEnd, "c1"), (left.Zone, left.ClipId));
        Assert.Equal((HitZone.ClipStart, "c2"), (right.Zone, right.ClipId));
    }

    [Fact]
    public void TransitionBandBeatsEdgesAndBody()
    {
        var hit = Hit(X(49.5), Y(V1, 28));
        Assert.Equal(HitZone.Transition, hit.Zone);
        Assert.Equal("x1", hit.TransitionId);
        Assert.Equal(HitZone.Transition, Hit(X(54), Y(V1, 28)).Zone);
        Assert.Equal(HitZone.ClipBody, Hit(X(56), Y(V1, 28)).Zone);
    }

    [Fact]
    public void FadeHandlesBeatEverything()
    {
        Assert.Equal(HitZone.FadeInHandle, Hit(X(0.5), Y(V1, 4)).Zone);
        var outHandle = Hit(X(48), Y(V1, 4));
        Assert.Equal((HitZone.FadeOutHandle, "c1"), (outHandle.Zone, outHandle.ClipId));
        Assert.Equal(HitZone.ClipStart, Hit(X(0.5), Y(V1, 12)).Zone);
    }

    [Fact]
    public void FadeHandlesNeedRoom()
    {
        _view.SetTrackHeight(V1, 32);
        var layout = _view.Layout(_project.Sequence);
        var y = _view.ToControlY(layout.Row(V1)!.Top) + 4;
        Assert.Equal(HitZone.ClipStart, HitTester.Hit(_project, _view, layout, X(0.5), y, 1000).Zone);
        Assert.Null(HitTester.FadeHandles(0, 30, 56, 0, 0));
        Assert.NotNull(HitTester.FadeHandles(0, 40, 40, 0, 0));
    }

    [Fact]
    public void NarrowClipsSplitIntoThirds()
    {
        Assert.Equal(HitZone.ClipStart, Hit(X(200.5), Y(V1, 4)).Zone);
        Assert.Equal(HitZone.ClipBody, Hit(X(202.5), Y(V1, 28)).Zone);
        Assert.Equal(HitZone.ClipEnd, Hit(X(204.5), Y(V1, 28)).Zone);
    }

    [Fact]
    public void GapsLanesAndThePlayhead()
    {
        var gap = Hit(X(150), Y(V1, 28));
        Assert.Equal(HitZone.Gap, gap.Zone);
        Assert.Equal((100L, 200L), (gap.GapStart, gap.GapEnd));
        Assert.Equal(HitZone.Playhead, Hit(X(150) + 2, Y(V1, 28), playhead: 150).Zone);
        Assert.Equal(HitZone.Lane, Hit(X(300), Y(V1, 28)).Zone);
        Assert.Equal(HitZone.Lane, Hit(X(10), Y(A2, 20)).Zone);
        var below = Hit(X(10), Y(A2, 200));
        Assert.Equal(HitZone.Lane, below.Zone);
        Assert.Null(below.TrackId);
    }

    [Fact]
    public void ClipsKeepPrecedenceOverThePlayhead() =>
        Assert.Equal(HitZone.ClipBody, Hit(X(25), Y(V1, 28), playhead: 25).Zone);
}
