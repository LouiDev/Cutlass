using System.Collections.Immutable;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// Body drag (DESIGN 5.4, 5.5): moves the selected clips. Overwrite by default, insert while Ctrl is held (the live
/// result is the ghost preview), vertical moves within the pressed clip's track kind, Alt moves only the pressed clip
/// and not its partners, and the clip edges snap.
/// </summary>
public sealed class MoveClipsDrag : IDragOperation
{
    private readonly string _pressedClipId;
    private TimelineDragState? _state;
    private ImmutableArray<string> _ids = ImmutableArray<string>.Empty;
    private ImmutableArray<long> _edges = ImmutableArray<long>.Empty;
    private ImmutableArray<long> _targets = ImmutableArray<long>.Empty;
    private TrackKind _kind;
    private int _kindIndex;
    private long _pressedStart;
    private string _label = "Move";

    /// <summary>Creates the drag for a pressed clip (already selected by the control).</summary>
    /// <param name="pressedClipId">The clip under the press.</param>
    public MoveClipsDrag(string pressedClipId)
    {
        ArgumentNullException.ThrowIfNull(pressedClipId);
        _pressedClipId = pressedClipId;
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
    public Project? OriginProject => _state?.Origin;

    /// <inheritdoc />
    public IReadOnlyCollection<string> OutlineClipIds => _ids;

    /// <summary>The clips being moved.</summary>
    public IReadOnlyList<string> MovingIds => _ids;

    /// <summary>True when the current preview inserts instead of overwriting.</summary>
    public bool IsInsert { get; private set; }

    /// <inheritdoc />
    public void Begin(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var vm = c.ViewModel;
        var origin = vm.Session.Current;
        if (TimelineQuery.FindClip(origin, _pressedClipId) is not { } pressed)
        {
            return;
        }

        var ids = vm.SelectedClipIds.Contains(_pressedClipId) ? vm.SelectedClipIds.ToList() : [_pressedClipId];
        if (c.PressAlt)
        {
            var partners = TimelineQuery.LinkGroup(origin, _pressedClipId).Where(id => id != _pressedClipId).ToHashSet(StringComparer.Ordinal);
            ids.RemoveAll(partners.Contains);
        }

        _ids = [.. ids.Where(id => TimelineQuery.FindClip(origin, id) is { Track.Locked: false })];
        var edges = new List<long>();
        foreach (var id in _ids)
        {
            var clip = TimelineQuery.FindClip(origin, id)!.Value.Clip;
            edges.Add(clip.Start);
            edges.Add(clip.End);
        }

        _edges = [.. edges.Distinct().Order()];
        _targets = SnapEngine.Collect(origin, vm.Playhead, _ids);
        _kind = pressed.Track.Kind;
        _kindIndex = TimelineQuery.KindIndex(origin, pressed.Track.Id);
        _pressedStart = pressed.Clip.Start;
        _state = new TimelineDragState(vm, origin);
        vm.Session.BeginGesture();
    }

    /// <inheritdoc />
    public void Update(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (_state is null || _ids.IsEmpty)
        {
            return;
        }

        var delta = c.FrameDelta;
        SnapFrame = null;
        if (c.SnapActive)
        {
            var snap = SnapEngine.Resolve(_edges, delta, _targets, c.View.PixelsPerFrame);
            delta = snap.Delta;
            SnapFrame = snap.SnapFrame;
        }

        var row = c.ViewModel.GetLayout().NearestRowOfKind(c.ContentY, _kind);
        var deltaTracks = row is null ? 0 : row.KindIndex - _kindIndex;
        IsInsert = c.Ctrl;
        var mode = IsInsert ? EditMode.Insert : EditMode.Overwrite;
        var ids = _ids;
        var vm = c.ViewModel;
        var result = vm.Session.UpdateGesture(p => TimelineEdits.Move(vm.Ids, p, ids, delta, deltaTracks, mode));
        _state.Last = result;
        _label = mode == EditMode.Insert ? "Move (Insert)" : "Move";
        var moved = TimelineQuery.FindClip(result.Project, _pressedClipId)?.Clip.Start ?? _pressedStart;
        var actual = ReferenceEquals(result.Project, _state.Origin) ? 0 : moved - _pressedStart;
        TooltipIsLimit = actual != delta;
        if (TooltipIsLimit)
        {
            SnapFrame = null;
        }

        Tooltip = DragText.Delta(actual, result.Project.Sequence.Settings.Rate);
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (_state is null)
        {
            return;
        }

        _state.ViewModel.Session.CommitGesture(_label);
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
