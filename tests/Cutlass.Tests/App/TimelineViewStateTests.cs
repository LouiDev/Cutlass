using Cutlass.App.Controls.Timeline;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;

namespace Cutlass.Tests.App;

public sealed class TimelineViewStateTests
{
    private static TimelineViewState View(double width = 1000, double height = 400, long frames = 5000)
    {
        var view = new TimelineViewState();
        view.SetViewport(width, height);
        view.SetContent(frames, 300);
        return view;
    }

    [Fact]
    public void FrameAndPixelMathFollowTheDesignFormula()
    {
        var view = View();
        view.SetZoom(4, 0);
        view.ScrollToFrame(100);
        Assert.Equal(0, view.FrameToX(100));
        Assert.Equal(40, view.FrameToX(110));
        Assert.Equal(110, view.XToFrame(40));
        Assert.Equal(110, view.FrameAt(41.9));
        Assert.Equal(109, view.FrameAt(39.9));
        Assert.Equal(110, view.BoundaryAt(38.5));
        Assert.Equal(110, view.BoundaryAt(41.9));
    }

    [Fact]
    public void ZoomKeepsTheFrameUnderTheAnchor()
    {
        var view = View();
        view.ScrollToFrame(200);
        var anchorFrame = view.XToFrame(300);
        view.ZoomBy(2.5, 300);
        Assert.Equal(5, view.PixelsPerFrame, 9);
        Assert.Equal(anchorFrame, view.XToFrame(300), 9);
    }

    [Fact]
    public void ZoomClampsToTheDesignRange()
    {
        var view = View();
        view.SetZoom(1000, 0);
        Assert.Equal(TimelineViewState.MaxPixelsPerFrame, view.PixelsPerFrame);
        view.SetZoom(0.000001, 0);
        Assert.Equal(TimelineViewState.MinPixelsPerFrame, view.PixelsPerFrame);
    }

    [Fact]
    public void ZoomToFitShowsTheWholeSequenceFromFrameZero()
    {
        var view = View(1000);
        view.ScrollToFrame(300);
        view.ZoomToFit(500);
        Assert.Equal(0, view.ScrollFrame);
        Assert.Equal(1.9, view.PixelsPerFrame, 9);
        Assert.True(view.FrameToX(500) <= view.ViewportWidth);
    }

    [Fact]
    public void ScrollStaysWithinTheExtent()
    {
        var view = View(1000, 400, 1000);
        view.SetZoom(2, 0);
        view.ScrollToFrame(-50);
        Assert.Equal(0, view.ScrollFrame);
        view.ScrollToFrame(1_000_000);
        // Extent = 1000 frames + half a screen (250 frames); the view is 500 frames wide.
        Assert.Equal(750, view.ScrollFrame, 9);
        view.ScrollByPixels(-100);
        Assert.Equal(700, view.ScrollFrame, 9);
    }

    [Fact]
    public void VerticalScrollClampsToTheContent()
    {
        var view = new TimelineViewState();
        view.SetViewport(1000, 100);
        view.SetContent(10, 300);
        view.SetScrollY(500);
        Assert.Equal(200, view.ScrollY);
        view.SetScrollY(-10);
        Assert.Equal(0, view.ScrollY);
    }

    [Fact]
    public void EnsureVisiblePagesWhenThePlayheadLeavesTheView()
    {
        var view = View(1000);
        view.SetZoom(2, 0);
        Assert.False(view.EnsureVisible(100));
        Assert.True(view.EnsureVisible(600));
        Assert.Equal(600, view.ScrollFrame);
        Assert.True(view.EnsureVisible(550));
        Assert.Equal(51, view.ScrollFrame, 9);
        Assert.InRange(view.FrameToX(550), 0, view.ViewportWidth);
    }

    [Fact]
    public void ChangedIsRaisedForZoomScrollAndTrackHeights()
    {
        var view = View();
        var count = 0;
        view.Changed += (_, _) => count++;
        view.ZoomBy(2, 0);
        view.ScrollByPixels(50);
        view.SetTrackHeight("tr_v1", 80);
        view.SetTrackHeight("tr_v1", 80);
        Assert.Equal(3, count);
    }

    [Fact]
    public void TrackHeightsDefaultAndClamp()
    {
        var view = new TimelineViewState();
        Assert.Equal(56, view.TrackHeight(V1, TrackKind.Video));
        Assert.Equal(48, view.TrackHeight(A1, TrackKind.Audio));
        view.SetTrackHeight(V1, 10);
        Assert.Equal(TimelineViewState.MinTrackHeight, view.TrackHeight(V1, TrackKind.Video));
        view.SetTrackHeight(A1, 999);
        Assert.Equal(TimelineViewState.MaxTrackHeight, view.TrackHeight(A1, TrackKind.Audio));
    }

    [Fact]
    public void LayoutStacksVideoUpwardAndAudioDownwardFromTheDivider()
    {
        var view = new TimelineViewState();
        view.SetViewport(1000, 100);
        var layout = view.Layout(new ProjectBuilder().Build().Sequence);
        Assert.Equal([V2, V1, A1, A2], layout.Rows.Select(r => r.TrackId).ToArray());
        Assert.Equal(0, layout.Rows[0].Top);
        Assert.Equal(56, layout.Rows[1].Top);
        Assert.Equal(112, layout.DividerTop);
        Assert.Equal(112 + TimelineViewState.DividerHeight, layout.Rows[2].Top);
        Assert.Equal(0, layout.Row(V1)!.KindIndex);
        Assert.Equal(1, layout.Row(A2)!.KindIndex);
        Assert.Equal((2 * 56) + (2 * 48) + TimelineViewState.DividerHeight, layout.ContentHeight);
    }

    [Fact]
    public void LayoutCentersTheDividerWhenEverythingFits()
    {
        var view = new TimelineViewState();
        view.SetViewport(1000, 600);
        var layout = view.Layout(new ProjectBuilder().Build().Sequence);
        Assert.Equal(300, layout.DividerTop + (TimelineViewState.DividerHeight / 2), 9);
    }

    [Fact]
    public void NearestRowOfKindStaysWithinTheKind()
    {
        var view = new TimelineViewState();
        view.SetViewport(1000, 100);
        var layout = view.Layout(new ProjectBuilder().Build().Sequence);
        Assert.Equal(V1, layout.NearestRowOfKind(layout.Row(A2)!.Top + 5, TrackKind.Video)!.TrackId);
        Assert.Equal(A1, layout.NearestRowOfKind(-50, TrackKind.Audio)!.TrackId);
        Assert.Equal(V2, layout.NearestRowOfKind(-50, TrackKind.Video)!.TrackId);
    }

    [Theory]
    [InlineData(40, 5, 1)]
    [InlineData(8, 25, 5)]
    [InlineData(2, 50, 5)]
    [InlineData(0.5, 250, 25)]
    [InlineData(0.01, 15000, 1500)]
    public void RulerStepsKeepLabelsApart(double ppf, long major, long minor)
    {
        var view = new TimelineViewState();
        view.SetZoom(ppf, 0);
        var steps = view.ChooseRulerSteps(25);
        Assert.Equal(major, steps.Major);
        Assert.Equal(minor, steps.Minor);
        Assert.True(steps.Major * ppf >= TimelineViewState.MinMajorTickPixels);
        Assert.Equal(0, steps.Major % steps.Minor);
    }

    [Fact]
    public void SubSecondRulerStepsDivideTheSecond()
    {
        var view = new TimelineViewState();
        view.SetZoom(10, 0);
        Assert.Equal(12, view.ChooseRulerSteps(24).Major);
        Assert.Equal(25, view.ChooseRulerSteps(25).Major);
    }

    [Fact]
    public void RulerStepsUseWholeSecondsAboveOneSecondAtNtscRates()
    {
        var view = new TimelineViewState();
        view.SetZoom(1, 0);
        var steps = view.ChooseRulerSteps(30);
        Assert.Equal(150, steps.Major);
        Assert.Equal(0, steps.Major % 30);
    }
}
