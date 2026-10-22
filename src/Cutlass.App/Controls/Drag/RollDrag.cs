using System.Collections.Immutable;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// Roll tool on a shared cut (DESIGN 5.4, 5.5): moves the cut between two butting clips, the left clip's end and the
/// right clip's start together, limited by the handles on both sides. Linked cuts on other tracks roll too unless Alt
/// was held at the press. The cut snaps; the viewer shows a two-up of the outgoing and incoming frame (DESIGN 4.3).
/// </summary>
public sealed class RollDrag : IDragOperation
{
    private readonly string _leftId;
    private readonly string _rightId;
    private TimelineDragState? _state;
    private bool _includeLinked;
    private long _cut;
    private ImmutableArray<long> _targets = ImmutableArray<long>.Empty;
    private ImmutableArray<string> _outline = ImmutableArray<string>.Empty;

    /// <summary>Creates the drag.</summary>
    /// <param name="leftId">The outgoing clip (ends at the cut).</param>
    /// <param name="rightId">The incoming clip (starts at the cut).</param>
    public RollDrag(string leftId, string rightId)
    {
        ArgumentNullException.ThrowIfNull(leftId);
        ArgumentNullException.ThrowIfNull(rightId);
        _leftId = leftId;
        _rightId = rightId;
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

    /// <summary>
    /// The two butting clips of the cut at a clip edge, or null when the edge has no butting neighbour on its track.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="clipId">The pressed clip.</param>
    /// <param name="edge">The pressed edge.</param>
    /// <returns>The outgoing and incoming clip ids, or null.</returns>
    public static (string Left, string Right)? CutAt(Project project, string clipId, ClipEdge edge)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (TimelineQuery.FindClip(project, clipId) is not { } found)
        {
            return null;
        }

        var clips = found.Track.Clips;
        var index = clips.IndexOf(found.Clip);
        if (edge == ClipEdge.End && index + 1 < clips.Length && clips[index + 1].Start == found.Clip.End)
        {
            return (found.Clip.Id, clips[index + 1].Id);
        }

        if (edge == ClipEdge.Start && index > 0 && clips[index - 1].End == found.Clip.Start)
        {
            return (clips[index - 1].Id, found.Clip.Id);
        }

        return null;
    }

    /// <inheritdoc />
    public void Begin(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var vm = c.ViewModel;
        var origin = vm.Session.Current;
        if (TimelineQuery.FindClip(origin, _leftId) is not { } left || left.Track.Locked || TimelineQuery.FindClip(origin, _rightId) is null)
        {
            return;
        }

        _includeLinked = vm.LinkedSelection && !c.PressAlt;
        _cut = left.Clip.End;
        var group = TimelineQuery.WithLinkedPartners(origin, [_leftId, _rightId]);
        _targets = [.. SnapEngine.Collect(origin, vm.Playhead, group).Where(t => t != _cut)];
        _outline = _includeLinked ? group : [_leftId, _rightId];
        _state = new TimelineDragState(vm, origin);
        Hint = new ViewerHint(Math.Max(0, _cut - 1), _cut);
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
            var snap = SnapEngine.Resolve([_cut], delta, _targets, c.View.PixelsPerFrame);
            delta = snap.Delta;
            SnapFrame = snap.SnapFrame;
        }

        var (left, right, linked) = (_leftId, _rightId, _includeLinked);
        var result = c.ViewModel.Session.UpdateGesture(p => TimelineEdits.Roll(p, left, right, delta, linked));
        _state.Last = result;
        var cut = TimelineQuery.FindClip(result.Project, _rightId)?.Clip.Start ?? _cut;
        var actual = cut - _cut;
        TooltipIsLimit = actual != delta;
        if (TooltipIsLimit)
        {
            SnapFrame = null;
        }

        Tooltip = DragText.Delta(actual, result.Project.Sequence.Settings.Rate);

        // Two-up: the last frame of the outgoing clip and the first frame of the incoming clip.
        Hint = new ViewerHint(Math.Max(0, cut - 1), cut);
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (_state is null)
        {
            return;
        }

        _state.ViewModel.Session.CommitGesture("Roll");
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
