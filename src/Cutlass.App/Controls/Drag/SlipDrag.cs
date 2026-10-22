using System.Collections.Immutable;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// Slip tool (DESIGN 5.4, 5.5): slips the clip's source in and out while the clip keeps its position and length.
/// Linked partners slip by the same amount unless Alt was held at the press. Dragging right shows earlier source (the
/// content follows the pointer). The viewer shows a two-up of the new first and last frame (DESIGN 4.3).
/// </summary>
public sealed class SlipDrag : IDragOperation
{
    private readonly string _clipId;
    private TimelineDragState? _state;
    private bool _includeLinked;
    private long _sourceIn;
    private ImmutableArray<string> _outline = ImmutableArray<string>.Empty;

    /// <summary>Creates the drag.</summary>
    /// <param name="clipId">The slipped clip.</param>
    public SlipDrag(string clipId)
    {
        ArgumentNullException.ThrowIfNull(clipId);
        _clipId = clipId;
    }

    /// <inheritdoc />
    public string? Tooltip { get; private set; }

    /// <inheritdoc />
    public bool TooltipIsLimit { get; private set; }

    /// <inheritdoc />
    public ViewerHint? Hint { get; private set; }

    /// <inheritdoc />
    public long? SnapFrame => null;

    /// <inheritdoc />
    public Project? OriginProject => _state?.Origin;

    /// <inheritdoc />
    public IReadOnlyCollection<string> OutlineClipIds => _outline;

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
        _sourceIn = found.Clip is MediaClip m ? m.SourceInFlicks : 0;
        _outline = [_clipId];
        _state = new TimelineDragState(vm, origin);
        Hint = new ViewerHint(found.Clip.Start, Math.Max(found.Clip.Start, found.Clip.End - 1));
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

        // Positive Core deltas show later source; the pointer moving right drags the content right (earlier source).
        var delta = -c.FrameDelta;
        var (clipId, linked) = (_clipId, _includeLinked);
        var result = c.ViewModel.Session.UpdateGesture(p => TimelineEdits.Slip(p, clipId, delta, linked));
        _state.Last = result;
        var rate = result.Project.Sequence.Settings.Rate;
        var clip = TimelineQuery.FindClip(result.Project, _clipId)?.Clip;
        long actual = 0;
        if (clip is MediaClip m)
        {
            var perFrame = (double)rate.FlicksPerFrame * m.SpeedPercent / 100;
            actual = (long)Math.Round((m.SourceInFlicks - _sourceIn) / perFrame, MidpointRounding.AwayFromZero);
        }

        TooltipIsLimit = actual != delta;
        Tooltip = "Slip " + DragText.Delta(actual, rate);
        Hint = clip is null ? null : new ViewerHint(clip.Start, Math.Max(clip.Start, clip.End - 1));
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (_state is null)
        {
            return;
        }

        _state.ViewModel.Session.CommitGesture("Slip");
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
