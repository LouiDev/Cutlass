using Cutlass.App.ViewModels;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// Ruler scrub (DESIGN 5.2): a click or drag in the ruler puts the playhead on the frame under the pointer, snapped to
/// edit points, markers and In/Out while snapping is on (Shift suspends it). A view drag: nothing is recorded, and Esc
/// restores the playhead.
/// </summary>
public sealed class ScrubDrag : IDragOperation
{
    private TimelineViewModel? _viewModel;
    private long _original;

    /// <inheritdoc />
    public string? Tooltip { get; private set; }

    /// <inheritdoc />
    public bool TooltipIsLimit => false;

    /// <inheritdoc />
    public ViewerHint? Hint => null;

    /// <inheritdoc />
    public long? SnapFrame { get; private set; }

    /// <inheritdoc />
    public Project? OriginProject => null;

    /// <inheritdoc />
    public IReadOnlyCollection<string> OutlineClipIds => [];

    /// <summary>
    /// The playhead frame for a pointer frame: the frame cell under the pointer, clamped to 0 and the sequence end, and
    /// snapped when snapping is active.
    /// </summary>
    /// <param name="c">The drag context.</param>
    /// <param name="frame">The exact pointer frame.</param>
    /// <returns>The frame and the snap target, if any.</returns>
    public static (long Frame, long? Snap) PlayheadFrame(DragContext c, double frame)
    {
        ArgumentNullException.ThrowIfNull(c);
        var vm = c.ViewModel;
        var target = Math.Clamp((long)Math.Floor(frame), 0, Math.Max(0, vm.SequenceEnd));
        if (!c.SnapActive)
        {
            return (target, null);
        }

        // Frame 0 stands in for the playhead target: the playhead itself is what moves.
        var targets = SnapEngine.Collect(vm.Session.Current, 0, []);
        var snap = SnapEngine.Resolve([target], 0, targets, c.View.PixelsPerFrame);
        return snap.SnapFrame is { } s ? (Math.Clamp(s, 0, Math.Max(0, vm.SequenceEnd)), s) : (target, null);
    }

    /// <inheritdoc />
    public void Begin(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        _viewModel = c.ViewModel;
        _original = c.ViewModel.Playhead;
    }

    /// <inheritdoc />
    public void Update(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (_viewModel is null)
        {
            return;
        }

        var (frame, snap) = PlayheadFrame(c, c.Frame);
        SnapFrame = snap;
        _viewModel.Playhead = frame;
        Tooltip = null;
    }

    /// <inheritdoc />
    public void Commit() => _viewModel = null;

    /// <inheritdoc />
    public void Cancel()
    {
        if (_viewModel is not null)
        {
            _viewModel.Playhead = _original;
        }

        _viewModel = null;
    }
}
