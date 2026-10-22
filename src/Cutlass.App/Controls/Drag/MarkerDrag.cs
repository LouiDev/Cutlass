using System.Collections.Immutable;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// Drags a marker head in the ruler (DESIGN 5.5: markers drag). Whole frames from 0, snapping to edit points, the
/// playhead, other markers and In/Out. One undo step.
/// </summary>
public sealed class MarkerDrag : IDragOperation
{
    private readonly string _markerId;
    private TimelineDragState? _state;
    private long _frame;
    private ImmutableArray<long> _targets = ImmutableArray<long>.Empty;

    /// <summary>Creates the drag.</summary>
    /// <param name="markerId">The marker.</param>
    public MarkerDrag(string markerId)
    {
        ArgumentNullException.ThrowIfNull(markerId);
        _markerId = markerId;
    }

    /// <inheritdoc />
    public string? Tooltip { get; private set; }

    /// <inheritdoc />
    public bool TooltipIsLimit { get; private set; }

    /// <inheritdoc />
    public ViewerHint? Hint => null;

    /// <inheritdoc />
    public long? SnapFrame { get; private set; }

    /// <inheritdoc />
    public Project? OriginProject => null;

    /// <inheritdoc />
    public IReadOnlyCollection<string> OutlineClipIds => [];

    /// <inheritdoc />
    public void Begin(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var vm = c.ViewModel;
        var origin = vm.Session.Current;
        if (origin.Sequence.Markers.FirstOrDefault(m => m.Id == _markerId) is not { } marker)
        {
            return;
        }

        _frame = marker.Frame;

        // The dragged marker is no target of its own.
        var others = origin with { Sequence = origin.Sequence with { Markers = origin.Sequence.Markers.RemoveAll(m => m.Id == _markerId) } };
        _targets = SnapEngine.Collect(others, vm.Playhead);
        _state = new TimelineDragState(vm, origin);
        vm.Session.BeginGesture();
    }

    /// <inheritdoc />
    public void Update(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (_state is null)
        {
            return;
        }

        var delta = c.FrameDelta;
        SnapFrame = null;
        if (c.SnapActive)
        {
            var snap = SnapEngine.Resolve([_frame], delta, _targets, c.View.PixelsPerFrame);
            delta = snap.Delta;
            SnapFrame = snap.SnapFrame;
        }

        var wanted = _frame + delta;
        var frame = Math.Max(0, wanted);
        TooltipIsLimit = frame != wanted;
        var id = _markerId;
        var result = c.ViewModel.Session.UpdateGesture(p => TimelineEdits.MoveMarker(p, id, frame));
        _state.Last = result;
        Tooltip = Core.Time.Timecode.Format(frame, result.Project.Sequence.Settings.Rate);
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (_state is null)
        {
            return;
        }

        _state.ViewModel.Session.CommitGesture("Move Marker");
        if (_state.Last is { } last)
        {
            _state.ViewModel.ReportWarnings(last);
        }

        _state = null;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        _state?.ViewModel.Session.CancelGesture();
        _state = null;
    }
}
