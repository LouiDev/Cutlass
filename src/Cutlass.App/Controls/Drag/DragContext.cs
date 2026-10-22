using Cutlass.App.ViewModels;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// The pointer state of a drag. The press position is kept as a frame and a content y, so scrolling during the drag
/// never changes the delta. Coordinates are control pixels; no Avalonia types, so drags are unit tested.
/// </summary>
public sealed class DragContext
{
    /// <summary>Creates the context at the press.</summary>
    /// <param name="viewModel">The timeline view model.</param>
    /// <param name="pressHit">What the press hit.</param>
    /// <param name="pressX">The press x in control pixels.</param>
    /// <param name="pressY">The press y in control pixels.</param>
    /// <param name="ctrl">Ctrl held at the press.</param>
    /// <param name="shift">Shift held at the press.</param>
    /// <param name="alt">Alt held at the press.</param>
    public DragContext(TimelineViewModel viewModel, HitResult pressHit, double pressX, double pressY, bool ctrl, bool shift, bool alt)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(pressHit);
        ViewModel = viewModel;
        PressHit = pressHit;
        PressX = pressX;
        PressY = pressY;
        PressFrame = View.XToFrame(pressX - TimelineViewState.HeaderWidth);
        PressContentY = View.ToContentY(pressY);
        PressCtrl = ctrl;
        PressShift = shift;
        PressAlt = alt;
        X = pressX;
        Y = pressY;
        Ctrl = ctrl;
        Shift = shift;
        Alt = alt;
    }

    /// <summary>The timeline view model.</summary>
    public TimelineViewModel ViewModel { get; }

    /// <summary>The view state.</summary>
    public TimelineViewState View => ViewModel.ViewState;

    /// <summary>What the press hit.</summary>
    public HitResult PressHit { get; }

    /// <summary>The press x in control pixels.</summary>
    public double PressX { get; }

    /// <summary>The press y in control pixels.</summary>
    public double PressY { get; }

    /// <summary>The exact frame under the press.</summary>
    public double PressFrame { get; }

    /// <summary>The content y of the press.</summary>
    public double PressContentY { get; }

    /// <summary>Ctrl held at the press.</summary>
    public bool PressCtrl { get; }

    /// <summary>Shift held at the press.</summary>
    public bool PressShift { get; }

    /// <summary>Alt held at the press (one side of a linked pair).</summary>
    public bool PressAlt { get; }

    /// <summary>The current x in control pixels.</summary>
    public double X { get; set; }

    /// <summary>The current y in control pixels.</summary>
    public double Y { get; set; }

    /// <summary>Ctrl held now (insert at drop).</summary>
    public bool Ctrl { get; set; }

    /// <summary>Shift held now (suspends snapping).</summary>
    public bool Shift { get; set; }

    /// <summary>Alt held now.</summary>
    public bool Alt { get; set; }

    /// <summary>The exact frame under the pointer.</summary>
    public double Frame => View.XToFrame(X - TimelineViewState.HeaderWidth);

    /// <summary>The content y of the pointer.</summary>
    public double ContentY => View.ToContentY(Y);

    /// <summary>The whole-frame horizontal delta since the press.</summary>
    public long FrameDelta => TimelineViewState.RoundDelta(Frame - PressFrame);

    /// <summary>True when snapping applies now (on, and Shift not held).</summary>
    public bool SnapActive => ViewModel.SnapEnabled && !Shift;

    /// <summary>Moves the pointer.</summary>
    /// <param name="x">The x in control pixels.</param>
    /// <param name="y">The y in control pixels.</param>
    public void MoveTo(double x, double y)
    {
        X = x;
        Y = y;
    }
}
