using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// Drags an end of a transition band (DESIGN 5.5): transitions stay centred on their cut, so moving either end by one
/// frame changes the duration by two. The duration clamps to 2 frames and to the clips and their handles; the tooltip
/// shows the new duration, in red when clamped.
/// </summary>
public sealed class TransitionDrag : IDragOperation
{
    private readonly string _transitionId;
    private readonly bool _rightEnd;
    private TimelineDragState? _state;
    private long _duration;

    /// <summary>Creates the drag.</summary>
    /// <param name="transitionId">The transition.</param>
    /// <param name="rightEnd">True for the right end, false for the left end.</param>
    public TransitionDrag(string transitionId, bool rightEnd)
    {
        ArgumentNullException.ThrowIfNull(transitionId);
        _transitionId = transitionId;
        _rightEnd = rightEnd;
    }

    /// <inheritdoc />
    public string? Tooltip { get; private set; }

    /// <inheritdoc />
    public bool TooltipIsLimit { get; private set; }

    /// <inheritdoc />
    public ViewerHint? Hint => null;

    /// <inheritdoc />
    public long? SnapFrame => null;

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
        if (TimelineQuery.FindTransition(origin, _transitionId) is not { } found || found.Track.Locked)
        {
            return;
        }

        _duration = found.Transition.Duration;
        _state = new TimelineDragState(vm, origin);
        vm.SelectTransition(_transitionId);
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
        var wanted = _duration + (2 * (_rightEnd ? delta : -delta));
        var request = Math.Max(TimelineEdits.MinTransitionFrames, wanted);
        var id = _transitionId;
        var result = c.ViewModel.Session.UpdateGesture(p => TimelineEdits.SetTransitionDuration(p, id, request));
        _state.Last = result;
        var actual = TimelineQuery.FindTransition(result.Project, _transitionId)?.Transition.Duration ?? _duration;
        TooltipIsLimit = actual != wanted;
        Tooltip = DragText.Duration(actual, result.Project.Sequence.Settings.Rate);
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (_state is null)
        {
            return;
        }

        _state.ViewModel.Session.CommitGesture("Transition Duration");
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
