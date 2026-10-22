using System.Collections.Immutable;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// Edge drag (DESIGN 5.4, 5.5): a plain trim (Select tool) that stops at the neighbour and the source, or a ripple trim
/// (Ripple tool, or Ctrl at the press with the Select tool). Linked partners whose edge sits on the same frame follow
/// unless Alt was held at the press. The moving edge snaps; the tooltip shows the delta and the new duration, in red
/// when the trim is clamped.
/// </summary>
public sealed class TrimDrag : IDragOperation
{
    private readonly string _clipId;
    private readonly ClipEdge _edge;
    private readonly bool _ripple;
    private TimelineDragState? _state;
    private bool _includeLinked;
    private long _edgeFrame;
    private long _duration;
    private ImmutableArray<long> _targets = ImmutableArray<long>.Empty;
    private ImmutableArray<string> _outline = ImmutableArray<string>.Empty;

    /// <summary>Creates the drag.</summary>
    /// <param name="clipId">The trimmed clip.</param>
    /// <param name="edge">The dragged edge.</param>
    /// <param name="ripple">True for a ripple trim.</param>
    public TrimDrag(string clipId, ClipEdge edge, bool ripple)
    {
        ArgumentNullException.ThrowIfNull(clipId);
        _clipId = clipId;
        _edge = edge;
        _ripple = ripple;
    }

    /// <inheritdoc />
    public string? Tooltip { get; private set; }

    /// <inheritdoc />
    public bool TooltipIsLimit { get; private set; }

    /// <inheritdoc />
    public ViewerHint? Hint { get; private set; }

    /// <inheritdoc />
    public long? SnapFrame { get; private set; }

    /// <inheritdoc />
    public Project? OriginProject => _state?.Origin;

    /// <inheritdoc />
    public IReadOnlyCollection<string> OutlineClipIds => _outline;

    /// <summary>True for a ripple trim.</summary>
    public bool IsRipple => _ripple;

    /// <inheritdoc />
    public void Begin(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var vm = c.ViewModel;
        var origin = vm.Session.Current;
        if (TimelineQuery.FindClip(origin, _clipId) is not { } found || found.Track.Locked)
        {
            return;
        }

        _includeLinked = vm.LinkedSelection && !c.PressAlt;
        _edgeFrame = _edge == ClipEdge.Start ? found.Clip.Start : found.Clip.End;
        _duration = found.Clip.Duration;
        var group = TimelineQuery.LinkGroup(origin, _clipId);
        _targets = SnapEngine.Collect(origin, vm.Playhead, group);
        _outline = _includeLinked ? group : [_clipId];
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
            var snap = SnapEngine.Resolve([_edgeFrame], delta, _targets, c.View.PixelsPerFrame);
            delta = snap.Delta;
            SnapFrame = snap.SnapFrame;
        }

        var (clipId, edge, ripple, linked) = (_clipId, _edge, _ripple, _includeLinked);
        var result = c.ViewModel.Session.UpdateGesture(p => TimelineEdits.Trim(p, clipId, edge, delta, ripple, linked));
        _state.Last = result;
        var clip = TimelineQuery.FindClip(result.Project, _clipId)?.Clip;
        var newDuration = clip?.Duration ?? _duration;

        // Measured by the duration change, because a ripple start trim keeps the clip's start in place.
        var actual = _edge == ClipEdge.Start ? _duration - newDuration : newDuration - _duration;
        TooltipIsLimit = actual != delta;
        if (TooltipIsLimit)
        {
            SnapFrame = null;
        }

        var rate = result.Project.Sequence.Settings.Rate;
        Tooltip = DragText.Delta(actual, rate) + "  " + DragText.Duration(newDuration, rate);
        Hint = clip is null ? null : new ViewerHint(_edge == ClipEdge.Start ? clip.Start : Math.Max(clip.Start, clip.End - 1));
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (_state is null)
        {
            return;
        }

        _state.ViewModel.Session.CommitGesture(_ripple ? "Ripple Trim" : "Trim");
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
