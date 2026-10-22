using System.Globalization;
using Cutlass.App.Controls;
using Cutlass.App.Controls.Drag;
using Cutlass.App.Services;
using Cutlass.App.ViewModels;
using Cutlass.Core.Editing;
using Cutlass.Core.Session;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App.Playback;

public sealed class MeterAndGainTests
{
    [Fact]
    public void MeterJumpsUpFallsAndHoldsItsPeak()
    {
        var meter = new MeterBallistics();
        Assert.Equal(MeterBallistics.FloorDb, meter.LevelDb);

        meter.Update(0.5f, 0.016);
        Assert.Equal(-6.02, meter.LevelDb, 2);
        Assert.Equal(-6.02, meter.PeakDb, 2);

        // Silence: the bar falls at 24 dB/s, the hold stays for 1.5 s.
        meter.Update(0f, 0.5);
        Assert.Equal(-18.02, meter.LevelDb, 2);
        Assert.Equal(-6.02, meter.PeakDb, 2);
        meter.Update(0f, 0.6);
        Assert.Equal(-6.02, meter.PeakDb, 2);
        meter.Update(0f, 0.6);
        Assert.Equal(-6.02 - (24 * 0.6), meter.PeakDb, 2);
        Assert.Equal(-18.02 - (24 * 1.2), meter.LevelDb, 2);

        meter.Reset();
        Assert.Equal(MeterBallistics.FloorDb, meter.LevelDb);
        Assert.Equal(MeterBallistics.FloorDb, meter.PeakDb);
    }

    [Fact]
    public void MeterScaleFractions()
    {
        Assert.Equal(1, AudioMeter.Fraction(0));
        Assert.Equal(0, AudioMeter.Fraction(-96));
        Assert.Equal(0.9, AudioMeter.Fraction(-6), 9);
        Assert.Equal(0, AudioMeter.Fraction(double.NaN));
    }

    [Fact]
    [UseCulture("de-DE")]
    public void GainFieldFormatsAndDrags()
    {
        Assert.Equal("0.0 dB", TrackGain.Format(0));
        Assert.Equal("-3.5 dB", TrackGain.Format(-3.5));
        Assert.Equal("+2.0 dB", TrackGain.Format(2));
        Assert.Equal("+12.0 dB", TrackGain.Format(40));

        Assert.Equal(1.0, TrackGain.FromDrag(0, 10, shift: false, ctrl: false));
        Assert.Equal(10.0, TrackGain.FromDrag(0, 10, shift: true, ctrl: false));
        Assert.Equal(0.1, TrackGain.FromDrag(0, 10, shift: false, ctrl: true));
        Assert.Equal(TrackGain.MinDb, TrackGain.FromDrag(-50, -1000, shift: true, ctrl: false));
        Assert.Equal("-0.5", TrackGain.FromDrag(0, -5, false, false).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AudioHeadersHitTheGainField()
    {
        var project = new ProjectBuilder().With(V1, V("v1", 0, 100)).With(A1, A("a1", 0, 100)).Build();
        var view = new TimelineViewState();
        view.SetViewport(1000, 600);
        var layout = view.Layout(project.Sequence);
        var a1 = layout.Row(A1)!;
        var v1 = layout.Row(V1)!;

        var hit = HitTester.Hit(project, view, layout, 60, view.ToControlY(a1.Top + 34), 0);
        Assert.Equal(HitZone.HeaderGain, hit.Zone);
        Assert.Equal(A1, hit.TrackId);
        Assert.True(hit.IsHeader);

        // Video tracks have no gain field, and short audio tracks hide it.
        Assert.Equal(HitZone.Header, HitTester.Hit(project, view, layout, 60, view.ToControlY(v1.Top + 34), 0).Zone);
        view.SetTrackHeight(A1, 40);
        layout = view.Layout(project.Sequence);
        a1 = layout.Row(A1)!;
        Assert.NotEqual(HitZone.HeaderGain, HitTester.Hit(project, view, layout, 60, view.ToControlY(a1.Top + 30), 0).Zone);
    }

    [Fact]
    public void GainDragIsOneUndoStep()
    {
        var project = new ProjectBuilder().With(V1, V("v1", 0, 100)).With(A1, A("a1", 0, 100)).Build();
        var session = new ProjectSession(project, () => 0);
        var vm = new TimelineViewModel(session, new SequentialIdGenerator(1000));
        vm.ViewState.SetViewport(1000, 600);
        var row = vm.GetLayout().Row(A1)!;
        var y = vm.ViewState.ToControlY(row.Top + 34);
        var hit = HitTester.Hit(session.Current, vm.ViewState, vm.GetLayout(), 60, y, 0);
        var context = new DragContext(vm, hit, 60, y, ctrl: false, shift: false, alt: false);
        var drag = new TrackGainDrag(A1);

        drag.Begin(context);
        context.MoveTo(20, y);
        drag.Update(context);
        Assert.Equal("-4.0 dB", drag.Tooltip);
        context.MoveTo(90, y);
        drag.Update(context);
        drag.Commit();

        Assert.Equal(3.0, TimelineQuery.FindTrack(session.Current, A1)!.GainDb);
        Assert.Equal(1, session.UndoCount);
        session.Undo();
        Assert.Equal(0, TimelineQuery.FindTrack(session.Current, A1)!.GainDb);

        vm.SetTrackGain(A1, -99);
        Assert.Equal(TrackGain.MinDb, TimelineQuery.FindTrack(session.Current, A1)!.GainDb);
    }
}
