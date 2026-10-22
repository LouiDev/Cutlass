using Cutlass.App.ViewModels;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// Drags the playhead line where it crosses empty lane space. Unlike a ruler scrub, a press does not jump: the playhead
/// keeps its offset to the pointer and moves by whole frames, with the same snapping. Esc restores it.
/// </summary>
public sealed class PlayheadDrag : IDragOperation
{
    private TimelineViewModel? _viewModel;
    private long _original;

    /// <inheritdoc />
    public string? Tooltip => null;

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

        // The playhead cell's centre plus the pointer delta, so the floor in PlayheadFrame lands on whole frames.
        var (frame, snap) = ScrubDrag.PlayheadFrame(c, _original + 0.5 + c.FrameDelta);
        SnapFrame = snap;
        _viewModel.Playhead = frame;
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
