using System.Collections.Immutable;
using Avalonia;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Cutlass.App.Controls.Drag;
using Cutlass.App.Services;
using Cutlass.App.ViewModels;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Timeline;

/// <summary>
/// Pointer, wheel, keyboard and drop input (DESIGN 5.2 to 5.6). A press becomes a drag after 4 px of movement; a press
/// that never moves is a click. While <see cref="TimelineViewModel.IsReadOnly"/> (an agent holds the project,
/// AGENT_INTEGRATION 3.7) only view drags start (scrub, marquee, playhead, track height), clicks select, and edit
/// drags, razor clicks, header buttons, edit double-clicks, Delete and drops are refused.
/// </summary>
public sealed partial class TimelineControl
{
    private const double DragThreshold = 4;
    private const double WheelScrollPixels = 60;
    private const double WheelTrackPixels = 40;
    private const double WheelZoomFactor = 1.2;
    private const double AutoScrollMargin = 24;

    private ActiveDrag? _drag;
    private PendingPress? _pending;
    private DropPreview? _drop;
    private string? _hoverClipId;
    private long? _razorFrame;

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (ViewModel is not { } vm)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        var readOnly = vm.IsReadOnly;
        if (point.Properties.IsRightButtonPressed && _drag is null)
        {
            var at = HitTester.Hit(vm.Session.Current, vm.ViewState, vm.GetLayout(), point.Position.X, point.Position.Y, vm.Playhead, vm.SelectedClipIds);
            if (at.Zone == HitZone.HeaderGain && at.TrackId is { } gainTrack && !readOnly)
            {
                // Right-click resets the header gain field to 0 dB (the ScrubbyNumberBox rule, DESIGN 4.4).
                vm.SetTrackGain(gainTrack, 0);
            }
            else
            {
                Focus();
                ShowContextMenu(vm, at);
            }

            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed || _drag is not null)
        {
            return;
        }

        Focus();
        var (ctrl, shift, alt) = Modifiers(e.KeyModifiers);
        var pos = point.Position;
        var hit = HitTester.Hit(vm.Session.Current, vm.ViewState, vm.GetLayout(), pos.X, pos.Y, vm.Playhead, vm.SelectedClipIds);
        var context = new DragContext(vm, hit, pos.X, pos.Y, ctrl, shift, alt);
        e.Handled = true;
        if (hit.IsHeader && hit.TrackId is { } headerTrack)
        {
            vm.SelectTrack(headerTrack);
        }

        if (e.ClickCount >= 2 && DoubleClick(vm, hit))
        {
            return;
        }

        if (readOnly && IsEditOnlyHeaderZone(hit.Zone))
        {
            // Target, eye, lock, mute, solo and gain edit the project.
            return;
        }

        switch (hit.Zone)
        {
            case HitZone.Ruler:
                StartDrag(new ScrubDrag(), context, e.Pointer);
                return;
            case HitZone.Marker when hit.MarkerId is { } markerId:
                _pending = new PendingPress(context, readOnly ? null : new MarkerDrag(markerId), PressAction.JumpToMarker, null);
                e.Pointer.Capture(this);
                return;
            case HitZone.HeaderResize when hit.TrackId is { } resizeId && TimelineQuery.FindTrack(vm.Project, resizeId) is { } resizeTrack:
                StartDrag(new TrackResizeDrag(resizeId, resizeTrack.Kind), context, e.Pointer);
                return;
            case HitZone.HeaderTarget:
                vm.ToggleTrackTarget(hit.TrackId!);
                return;
            case HitZone.HeaderVisibility:
                vm.ToggleTrackHidden(hit.TrackId!);
                return;
            case HitZone.HeaderLock:
                vm.ToggleTrackLocked(hit.TrackId!);
                return;
            case HitZone.HeaderMute:
                vm.ToggleTrackMuted(hit.TrackId!);
                return;
            case HitZone.HeaderSolo:
                vm.ToggleTrackSolo(hit.TrackId!);
                return;
            case HitZone.HeaderGain when hit.TrackId is { } gainId:
                StartDrag(new TrackGainDrag(gainId), context, e.Pointer);
                return;
            case HitZone.Header:
            case HitZone.None:
                return;
        }

        if (hit.IsClip && hit.ClipId is { } clipId)
        {
            if (readOnly)
            {
                PressClipReadOnly(vm, clipId, context);
            }
            else
            {
                PressClip(vm, hit, clipId, context);
            }
        }
        else
        {
            _pending = hit.Zone switch
            {
                HitZone.Playhead => new PendingPress(context, new PlayheadDrag(), PressAction.None, null),
                HitZone.Transition => new PendingPress(context, null, PressAction.SelectTransition, null),
                HitZone.TransitionStart or HitZone.TransitionEnd when hit.TransitionId is { } transitionId =>
                    new PendingPress(context, readOnly ? null : new TransitionDrag(transitionId, hit.Zone == HitZone.TransitionEnd), PressAction.SelectTransition, null),
                HitZone.Gap => new PendingPress(context, new MarqueeDrag(), PressAction.SelectGap, null),
                _ => new PendingPress(context, new MarqueeDrag(), PressAction.ClearSelection, null),
            };
        }

        e.Pointer.Capture(this);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ViewModel is not { } vm)
        {
            return;
        }

        var pos = e.GetPosition(this);
        var (ctrl, shift, alt) = Modifiers(e.KeyModifiers);
        if (_drag is { } drag)
        {
            AutoScroll(vm, pos.X);
            drag.Context.MoveTo(pos.X, pos.Y);
            (drag.Context.Ctrl, drag.Context.Shift, drag.Context.Alt) = (ctrl, shift, alt);
            UpdateDrag();
            return;
        }

        if (_pending is { } pending)
        {
            var dx = pos.X - pending.Context.PressX;
            var dy = pos.Y - pending.Context.PressY;
            if ((dx * dx) + (dy * dy) >= DragThreshold * DragThreshold && pending.Drag is { } operation)
            {
                _pending = null;
                if (pending.Action == PressAction.ToggleClip && pending.ClipIds is { } ids && !ids.All(vm.SelectedClipIds.Contains))
                {
                    // Ctrl held at the press of a drag: the pressed clip joins the selection and moves with it.
                    vm.Select(ids, additive: true);
                }

                pending.Context.MoveTo(pos.X, pos.Y);
                (pending.Context.Ctrl, pending.Context.Shift, pending.Context.Alt) = (ctrl, shift, alt);
                StartDrag(operation, pending.Context, e.Pointer);
            }

            return;
        }

        UpdateHover(vm, pos);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            // Drags and pending clicks start only with the left button; releasing another one must not commit them.
            return;
        }

        if (_drag is { } drag)
        {
            _drag = null;
            drag.Operation.Commit();
            EndDrag();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_pending is { } pending && ViewModel is { } vm)
        {
            _pending = null;
            Click(vm, pending);
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _pending = null;
        CancelDrag();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverClipId is not null || _razorFrame is not null)
        {
            _hoverClipId = null;
            _razorFrame = null;
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (ViewModel is not { } vm)
        {
            return;
        }

        var view = vm.ViewState;
        var pos = e.GetPosition(this);
        var (ctrl, shift, alt) = Modifiers(e.KeyModifiers);
        var delta = e.Delta;
        if (ctrl)
        {
            view.ZoomBy(Math.Pow(WheelZoomFactor, delta.Y), Math.Max(0, pos.X - TimelineViewState.HeaderWidth));
        }
        else if (alt)
        {
            var row = vm.GetLayout().RowAt(view.ToContentY(pos.Y));
            if (row is not null)
            {
                view.SetTrackHeight(row.TrackId, row.Height + (delta.Y * 8));
            }
        }
        else if (shift)
        {
            // DESIGN 5.2: Shift+wheel scrolls tracks vertically. Some platforms deliver Shift+wheel as horizontal delta.
            var amount = delta.Y != 0 ? delta.Y : delta.X;
            view.SetScrollY(view.ScrollY - (amount * WheelTrackPixels));
        }
        else
        {
            view.ScrollByPixels(-(delta.Y + delta.X) * WheelScrollPixels);
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || ViewModel is not { } vm)
        {
            return;
        }

        var (ctrl, shift, alt) = Modifiers(e.KeyModifiers);
        if (_drag is { } drag)
        {
            if (e.Key == Key.Escape)
            {
                CancelDrag();
            }
            else
            {
                // Modifier changes during a drag (Ctrl insert, Shift snap off) apply at once.
                (drag.Context.Ctrl, drag.Context.Shift, drag.Context.Alt) = ModifiersWithKey(ctrl, shift, alt, e.Key, true);
                UpdateDrag();
            }

            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                vm.ClearSelectionCommand.Execute(null);
                break;
            case Key.Delete or Key.Back when vm.IsReadOnly:
                // An agent holds the project; the window's key routing normally claims these first.
                break;
            case Key.Delete when shift:
                vm.RippleDeleteSelectionCommand.Execute(null);
                break;
            case Key.Delete:
            case Key.Back:
                vm.DeleteSelectionCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_drag is { } drag)
        {
            var (ctrl, shift, alt) = Modifiers(e.KeyModifiers);
            (drag.Context.Ctrl, drag.Context.Shift, drag.Context.Alt) = ModifiersWithKey(ctrl, shift, alt, e.Key, false);
            UpdateDrag();
            e.Handled = true;
        }
    }

    private static (bool Ctrl, bool Shift, bool Alt) Modifiers(KeyModifiers modifiers) => (
        (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0,
        (modifiers & KeyModifiers.Shift) != 0,
        (modifiers & KeyModifiers.Alt) != 0);

    // KeyDown reports the modifiers before the key itself changes them, so the key is folded in.
    private static (bool Ctrl, bool Shift, bool Alt) ModifiersWithKey(bool ctrl, bool shift, bool alt, Key key, bool down) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl or Key.LWin or Key.RWin => (down, shift, alt),
        Key.LeftShift or Key.RightShift => (ctrl, down, alt),
        Key.LeftAlt or Key.RightAlt => (ctrl, shift, down),
        _ => (ctrl, shift, alt),
    };

    private static List<string> ReadAssetIds(DragEventArgs e)
    {
        // DragFormats are string-keyed names with a slash that the 11.3 DataTransfer formats cannot carry, so the
        // media bin and this reader keep the legacy IDataObject path on purpose.
#pragma warning disable CS0618
        var data = e.Data;
#pragma warning restore CS0618
        if (data.Contains(DragFormats.MediaAssetIds) && data.Get(DragFormats.MediaAssetIds) is string many)
        {
            return [.. many.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        }

        if (data.Contains(DragFormats.MediaAssetId) && data.Get(DragFormats.MediaAssetId) is string one && one.Length > 0)
        {
            return [one];
        }

        return [];
    }

    private static SourceDrag? ReadSourceDrag(DragEventArgs e)
    {
        // The same legacy IDataObject path as ReadAssetIds: the "cutlass/..." names need it.
#pragma warning disable CS0618
        var data = e.Data;
#pragma warning restore CS0618
        return data.Contains(DragFormats.SourceRange) && data.Get(DragFormats.SourceRange) is string text
            && SourceDrag.TryParse(text, out var drag) ? drag : null;
    }

    private static string? ReadEffectId(DragEventArgs e)
    {
#pragma warning disable CS0618
        var data = e.Data;
#pragma warning restore CS0618
        return data.Contains(EffectItem.DragFormat) && data.Get(EffectItem.DragFormat) is string id && id.Length > 0 ? id : null;
    }

    // The header buttons that edit the project (refused while an agent holds it). Resizing a track is view state.
    private static bool IsEditOnlyHeaderZone(HitZone zone) => zone is HitZone.HeaderTarget or HitZone.HeaderVisibility
        or HitZone.HeaderLock or HitZone.HeaderMute or HitZone.HeaderSolo or HitZone.HeaderGain;

    // Double-click (DESIGN 5.5): edits a marker, renames a track, adds a keyframe on the volume line, selects a transition.
    // While an agent holds the project only selecting a transition remains; the edits are swallowed.
    private static bool DoubleClick(TimelineViewModel vm, HitResult hit)
    {
        if (vm.IsReadOnly)
        {
            if (hit.Zone is HitZone.Transition or HitZone.TransitionStart or HitZone.TransitionEnd && hit.TransitionId is { } selected)
            {
                vm.SelectTransition(selected);
                return true;
            }

            return hit.IsHeader || hit.Zone is HitZone.Marker or HitZone.VolumeLine;
        }

        switch (hit.Zone)
        {
            case HitZone.Marker when hit.MarkerId is { } marker:
                vm.RequestMarkerEdit(marker);
                return true;
            case HitZone.Header when hit.TrackId is { } track:
                vm.RequestTrackRename(track);
                return true;
            case HitZone.VolumeLine when hit.ClipId is { } clip && vm.Tool != EditTool.Razor:
                vm.AddVolumeKey(clip, KeyFrameAt(vm, clip, hit.Frame));
                return true;
            case HitZone.Transition or HitZone.TransitionStart or HitZone.TransitionEnd when hit.TransitionId is { } transition:
                vm.SelectTransition(transition);
                return true;
            default:
                return false;
        }
    }

    // The clip-relative whole frame under a lane frame.
    private static long KeyFrameAt(TimelineViewModel vm, string clipId, double frame) =>
        TimelineQuery.FindClip(vm.Session.Current, clipId)?.Clip is { } clip ? (long)Math.Floor(frame) - clip.Start : 0;

    // A press on a clip while an agent holds the project: the clicks of the Select tool select (Ctrl toggles, Shift
    // adds), and no drag starts.
    private void PressClipReadOnly(TimelineViewModel vm, string clipId, DragContext context)
    {
        var ids = vm.ExpandLinked([clipId], context.PressAlt);
        PressAction action;
        if (context.PressCtrl)
        {
            action = PressAction.ToggleClip;
        }
        else if (context.PressShift)
        {
            vm.Select(ids, additive: true);
            action = PressAction.None;
        }
        else if (!vm.SelectedClipIds.Contains(clipId))
        {
            vm.Select(ids, additive: false);
            action = PressAction.None;
        }
        else
        {
            action = PressAction.SelectOnly;
        }

        _pending = new PendingPress(context, null, action, ids);
    }

    private void PressClip(TimelineViewModel vm, HitResult hit, string clipId, DragContext context)
    {
        if (vm.Tool == EditTool.Razor)
        {
            var frame = vm.ViewState.BoundaryAt(context.PressX - TimelineViewState.HeaderWidth);
            vm.RazorAt(clipId, frame, context.PressShift);
            return;
        }

        if (hit.Zone == HitZone.Keyframe && TimelineQuery.FindClip(vm.Session.Current, clipId)?.Clip is MediaClip keyed
            && hit.KeyIndex >= 0 && hit.KeyIndex < keyed.Audio.Keys.Length)
        {
            var key = keyed.Audio.Keys[hit.KeyIndex];
            if (context.PressAlt)
            {
                // Alt+click removes the keyframe.
                vm.RemoveVolumeKey(clipId, key.Frame);
                return;
            }

            vm.SelectVolumeKey(clipId, key.Frame);
            _pending = new PendingPress(context, new KeyframeDrag(clipId, hit.KeyIndex), PressAction.None, null);
            return;
        }

        if (hit.Zone == HitZone.VolumeLine)
        {
            if (context.PressCtrl)
            {
                // Ctrl+click on the volume line adds a keyframe (DESIGN 5.5).
                vm.AddVolumeKey(clipId, KeyFrameAt(vm, clipId, hit.Frame));
                return;
            }

            var start = TimelineQuery.FindClip(vm.Session.Current, clipId)?.Clip.Start ?? 0;
            _pending = new PendingPress(context, new VolumeLineDrag(clipId, hit.Frame - start), PressAction.None, null);
            return;
        }

        var ids = vm.ExpandLinked([clipId], context.PressAlt);
        var selected = vm.SelectedClipIds.Contains(clipId);
        PressAction action;
        if (context.PressCtrl)
        {
            action = PressAction.ToggleClip;
        }
        else if (context.PressShift)
        {
            vm.Select(ids, additive: true);
            action = PressAction.None;
        }
        else if (!selected)
        {
            vm.Select(ids, additive: false);
            action = PressAction.None;
        }
        else
        {
            // Pressing a selected clip keeps a multi-selection for the drag; a plain click then selects just it.
            action = PressAction.SelectOnly;
        }

        IDragOperation? drag = null;
        var fadeTool = vm.Tool is EditTool.Select or EditTool.Ripple;
        var edge = hit.Zone switch
        {
            HitZone.ClipStart => ClipEdge.Start,
            HitZone.ClipEnd => ClipEdge.End,

            // Roll and Slip have no fade handles: the corner squares sit at the edges and act like them there.
            HitZone.FadeInHandle when !fadeTool => ClipEdge.Start,
            HitZone.FadeOutHandle when !fadeTool => ClipEdge.End,
            _ => (ClipEdge?)null,
        };
        switch (vm.Tool)
        {
            case EditTool.Select or EditTool.Ripple when hit.Zone is HitZone.FadeInHandle or HitZone.FadeOutHandle:
                drag = new FadeHandleDrag(clipId, hit.Zone == HitZone.FadeInHandle);
                if (action == PressAction.ToggleClip)
                {
                    action = selected ? PressAction.None : PressAction.SelectOnly;
                }

                break;
            case EditTool.Select when edge is { } selectEdge:
                drag = new TrimDrag(clipId, selectEdge, ripple: context.PressCtrl);
                if (context.PressCtrl)
                {
                    // Ctrl+edge is a ripple trim, not a toggle.
                    action = selected ? PressAction.None : PressAction.SelectOnly;
                }

                break;
            case EditTool.Ripple when edge is { } rippleEdge:
                drag = new TrimDrag(clipId, rippleEdge, ripple: true);
                break;
            case EditTool.Select:
            case EditTool.Ripple:
                drag = new MoveClipsDrag(clipId);
                break;
            case EditTool.Roll when edge is { } rollEdge:
                // A cut without a butting neighbour has nothing to roll against, so the edge trims like the Select tool.
                drag = RollDrag.CutAt(vm.Session.Current, clipId, rollEdge) is { } cut
                    ? new RollDrag(cut.Left, cut.Right)
                    : new TrimDrag(clipId, rollEdge, ripple: false);
                break;
            case EditTool.Slip:
                // DESIGN 5.4: the Slip tool slips on the body and on the edges alike.
                drag = new SlipDrag(clipId);
                break;

            // The Roll tool on a body only selects (DESIGN 5.4: "none").
        }

        _pending = new PendingPress(context, drag, action, ids);
    }

    private void Click(TimelineViewModel vm, PendingPress pending)
    {
        var hit = pending.Context.PressHit;
        var additive = pending.Context.PressShift || pending.Context.PressCtrl;
        switch (pending.Action)
        {
            case PressAction.ToggleClip when pending.ClipIds is { } toggle:
                vm.ToggleSelection(toggle);
                break;
            case PressAction.SelectOnly when pending.ClipIds is { } only:
                vm.Select(only, additive: false);
                break;
            case PressAction.SelectTransition when hit.TransitionId is { } transition:
                vm.SelectTransition(transition);
                break;
            case PressAction.JumpToMarker when hit.MarkerId is { } markerId
                && vm.Session.Current.Sequence.Markers.FirstOrDefault(m => m.Id == markerId) is { } marker:
                vm.Playhead = marker.Frame;
                break;
            case PressAction.SelectGap when !additive && hit.TrackId is { } track:
                vm.SelectGap(track, hit.GapStart, hit.GapEnd);
                break;
            case PressAction.ClearSelection when !additive:
            case PressAction.SelectGap when !additive:
                vm.ClearSelectionCommand.Execute(null);
                break;
        }
    }

    private void StartDrag(IDragOperation operation, DragContext context, IPointer pointer)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        _drag = new ActiveDrag(operation, context);
        _hoverClipId = null;
        _razorFrame = null;
        vm.IsDragging = true;
        if (operation is not (TrackGainDrag or TrackResizeDrag))
        {
            // DESIGN 5.6: a timeline drag pauses playback; header drags (gain, height) keep it running.
            vm.NotifyDragStarted();
        }

        operation.Begin(context);
        pointer.Capture(this);
        UpdateDrag();
    }

    private void UpdateDrag()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        drag.Operation.Update(drag.Context);
        if (ViewModel is { } vm)
        {
            vm.DragHint = drag.Operation.Hint;
        }

        InvalidateVisual();
    }

    private void CancelDrag()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null;
        drag.Operation.Cancel();
        EndDrag();
    }

    private void EndDrag()
    {
        if (ViewModel is { } vm)
        {
            vm.IsDragging = false;
            vm.DragHint = null;
        }

        Cursor = Cursors.Arrow;
        InvalidateVisual();
    }

    private void AutoScroll(TimelineViewModel vm, double x)
    {
        var right = Bounds.Width - AutoScrollMargin;
        var left = TimelineViewState.HeaderWidth + AutoScrollMargin;
        if (x > right)
        {
            vm.ViewState.ScrollByPixels(Math.Min(40, (x - right) * 0.5) + 1);
        }
        else if (x < left && vm.ViewState.ScrollFrame > 0 && _drag?.Operation is not TrackResizeDrag)
        {
            vm.ViewState.ScrollByPixels(-(Math.Min(40, (left - x) * 0.5) + 1));
        }
    }

    private void UpdateHover(TimelineViewModel vm, Point pos)
    {
        var hit = HitTester.Hit(vm.Session.Current, vm.ViewState, vm.GetLayout(), pos.X, pos.Y, vm.Playhead, vm.SelectedClipIds);
        var razor = vm.Tool == EditTool.Razor && hit.IsClip;
        Cursor = hit.Zone switch
        {
            _ when razor => Cursors.Cross,
            _ when vm.Tool == EditTool.Slip && hit.IsClip => Cursors.SizeWestEast,
            HitZone.HeaderResize => Cursors.SizeNorthSouth,
            HitZone.ClipStart or HitZone.ClipEnd or HitZone.FadeInHandle or HitZone.FadeOutHandle
                when vm.Tool is EditTool.Select or EditTool.Ripple or EditTool.Roll => Cursors.SizeWestEast,
            HitZone.Playhead or HitZone.TransitionStart or HitZone.TransitionEnd => Cursors.SizeWestEast,
            HitZone.VolumeLine => Cursors.SizeNorthSouth,
            HitZone.Keyframe or HitZone.Marker => Cursors.Hand,
            HitZone.Transition or HitZone.HeaderTarget or HitZone.HeaderVisibility or HitZone.HeaderLock
                or HitZone.HeaderMute or HitZone.HeaderSolo => Cursors.Hand,
            HitZone.HeaderGain => Cursors.SizeWestEast,
            _ => Cursors.Arrow,
        };
        UpdateHoverTip(vm, hit);

        var hoverClip = hit.IsClip ? hit.ClipId : null;
        long? razorFrame = razor ? vm.ViewState.BoundaryAt(pos.X - TimelineViewState.HeaderWidth) : null;
        if (hoverClip != _hoverClipId || razorFrame != _razorFrame)
        {
            _hoverClipId = hoverClip;
            _razorFrame = razorFrame;
            InvalidateVisual();
        }
    }

    private static List<string> ReadFiles(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles() is { } items
            ? [.. items.Select(i => i.TryGetLocalPath()).OfType<string>()]
            : [];

    private static bool HasFiles(DragEventArgs e) => e.DataTransfer.Contains(DataFormat.File);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // While an agent holds the project every drop is refused (DragDropEffects.None).
        var preview = ViewModel is { IsReadOnly: false } vm ? BuildDrop(vm, e) : null;
        // Files from the file manager have no ghost until they are imported; the drop imports and places them.
        var files = preview is null && ViewModel is { ImportFiles: not null, IsReadOnly: false } && HasFiles(e);
        e.DragEffects = preview is null && !files ? DragDropEffects.None : DragDropEffects.Copy;
        if (preview != _drop)
        {
            _drop = preview;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var preview = ViewModel is { IsReadOnly: false } vm ? BuildDrop(vm, e) : null;
        _drop = null;
        if (preview is not null && ViewModel is { } target)
        {
            if (preview.Source is { } source)
            {
                target.PlaceSource(source, preview.TrackId, preview.Frame, preview.Insert);
            }
            else if (preview.EffectId is { } effectId)
            {
                target.Effects?.ApplyAt(effectId, preview.TrackId, preview.Frame);
            }
            else
            {
                target.PlaceMedia(preview.AssetIds, preview.TrackId, preview.Frame, preview.Insert);
            }

            e.DragEffects = DragDropEffects.Copy;
            Focus();
        }
        else if (ViewModel is { ImportFiles: not null, IsReadOnly: false } fileTarget && ReadFiles(e) is { Count: > 0 } files &&
                 DropPoint(fileTarget, e.GetPosition(this)) is { } point)
        {
            var (ctrl, _, _) = Modifiers(e.KeyModifiers);
            _ = fileTarget.DropFilesAsync(files, point.TrackId, point.Frame, ctrl);
            e.DragEffects = DragDropEffects.Copy;
            Focus();
        }

        InvalidateVisual();
        e.Handled = true;
    }

    // The hovered row (or the nearest one of the half the pointer is in) and the frame boundary under the pointer.
    private static (string TrackId, long Frame)? DropPoint(TimelineViewModel vm, Point pos)
    {
        var view = vm.ViewState;
        var layout = vm.GetLayout();
        var contentY = view.ToContentY(pos.Y);
        var row = layout.RowAt(contentY)
            ?? layout.NearestRowOfKind(contentY, contentY < layout.DividerTop ? TrackKind.Video : TrackKind.Audio)
            ?? layout.Rows.FirstOrDefault();
        return row is null ? null : (row.TrackId, Math.Max(0, view.BoundaryAt(pos.X - TimelineViewState.HeaderWidth)));
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (_drop is not null)
        {
            _drop = null;
            InvalidateVisual();
        }
    }

    private DropPreview? BuildDrop(TimelineViewModel vm, DragEventArgs e)
    {
        var ids = ReadAssetIds(e);
        var source = ids.Count == 0 ? ReadSourceDrag(e) : null;
        var effectId = ids.Count == 0 && source is null && vm.Effects is not null ? ReadEffectId(e) : null;
        if (ids.Count == 0 && source is null && effectId is null)
        {
            return null;
        }

        var pos = e.GetPosition(this);
        var view = vm.ViewState;
        var layout = vm.GetLayout();
        var contentY = view.ToContentY(pos.Y);
        var row = layout.RowAt(contentY)
            ?? layout.NearestRowOfKind(contentY, contentY < layout.DividerTop ? TrackKind.Video : TrackKind.Audio)
            ?? layout.Rows.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        var (ctrl, shift, _) = Modifiers(e.KeyModifiers);
        var frame = Math.Max(0, view.BoundaryAt(pos.X - TimelineViewState.HeaderWidth));
        if (effectId is not null)
        {
            // An effect acts where the pointer is (the nearest cut, the clip under it, a title at the frame), so the
            // ghost follows the pointer without snapping, and only the row exactly under the pointer takes it.
            if (layout.RowAt(contentY) is not { } hovered || vm.Effects?.PreviewAt(effectId, hovered.TrackId, frame) is not { } effect)
            {
                return null;
            }

            return new DropPreview([], hovered.TrackId, frame, false, ImmutableArray<ClipPlacement>.Empty, null)
            {
                EffectId = effectId,
                Effect = effect,
            };
        }

        var placements = source is not null ? vm.PreviewSourcePlacements(source, row.TrackId) : vm.PreviewPlacements(ids, row.TrackId);
        if (placements.IsEmpty)
        {
            return null;
        }

        long? snapFrame = null;
        if (vm.SnapEnabled && !shift)
        {
            var length = placements.Max(p => p.Clip.Start + p.Clip.Duration);
            var targets = SnapEngine.Collect(vm.Session.Current, vm.Playhead);
            var snap = SnapEngine.Resolve([frame, frame + length], 0, targets, view.PixelsPerFrame);
            if (frame + snap.Delta >= 0)
            {
                frame += snap.Delta;
                snapFrame = snap.SnapFrame;
            }
        }

        return new DropPreview(ids, row.TrackId, frame, ctrl, placements, snapFrame) { Source = source };
    }

    /// <summary>What a click (a press without a drag) does on release.</summary>
    private enum PressAction
    {
        /// <summary>Nothing more.</summary>
        None,

        /// <summary>Ctrl+click: toggle the clip (and partners).</summary>
        ToggleClip,

        /// <summary>Select only the pressed clip (and partners).</summary>
        SelectOnly,

        /// <summary>Select the pressed transition.</summary>
        SelectTransition,

        /// <summary>Select the pressed gap.</summary>
        SelectGap,

        /// <summary>Clear the selection (click on an empty lane).</summary>
        ClearSelection,

        /// <summary>Move the playhead to the pressed marker (a click on its head).</summary>
        JumpToMarker,
    }

    /// <summary>A press that has not moved 4 px yet.</summary>
    /// <param name="Context">The drag context created at the press.</param>
    /// <param name="Drag">The drag it becomes after 4 px, or null.</param>
    /// <param name="Action">What a release without a drag does.</param>
    /// <param name="ClipIds">The pressed clip and its partners, or null.</param>
    private sealed record PendingPress(DragContext Context, IDragOperation? Drag, PressAction Action, IReadOnlyList<string>? ClipIds);
}
