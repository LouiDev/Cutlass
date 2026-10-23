using System.Globalization;
using Avalonia.Controls;
using Cutlass.App.ViewModels.Inspector;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Timeline;

/// <summary>
/// Right-click menus (DESIGN 4.5, 5.5): track headers (rename, add above or below, delete), clips (cut, copy, paste,
/// link, "Move into sync" on drifted clips, delete), gaps ("Close gap"), markers, transitions and keyframes. The menus
/// only call view model members. While an agent holds the project (AGENT_INTEGRATION 3.7) the items that edit are
/// disabled; selecting and copying stay. Also the hover tip of shortened transitions.
/// </summary>
public sealed partial class TimelineControl
{
    private string? _hoverTip;

    private void ShowContextMenu(TimelineViewModel vm, HitResult hit)
    {
        var items = new List<Control>();
        var project = vm.Session.Current;
        var edit = !vm.IsReadOnly;
        if (hit.IsHeader && hit.TrackId is { } trackId && TimelineQuery.FindTrack(project, trackId) is { } track)
        {
            vm.SelectTrack(trackId);
            items.Add(Item("Rename...", () => vm.RequestTrackRename(trackId), edit));
            items.Add(Item("Add track above", () => vm.AddTrackNextTo(trackId, above: true), edit));
            items.Add(Item("Add track below", () => vm.AddTrackNextTo(trackId, above: false), edit));
            items.Add(new Separator());
            var kindCount = TimelineQuery.TracksOfKind(project, track.Kind).Length;
            items.Add(Item("Delete track", () => _ = vm.RemoveTrackAsync(trackId), enabled: edit && kindCount > 1 && !track.Locked));
        }
        else if (hit.Zone == HitZone.Marker && hit.MarkerId is { } markerId)
        {
            items.Add(Item("Edit marker...", () => vm.RequestMarkerEdit(markerId), edit));
            items.Add(Item("Delete marker", () => vm.RemoveMarker(markerId), edit));
        }
        else if (hit.Zone == HitZone.Keyframe && hit.ClipId is { } keyClip
            && TimelineQuery.FindClip(project, keyClip)?.Clip is MediaClip keyed && hit.KeyIndex >= 0 && hit.KeyIndex < keyed.Audio.Keys.Length)
        {
            var frame = keyed.Audio.Keys[hit.KeyIndex].Frame;
            items.Add(Item("Delete keyframe", () => vm.RemoveVolumeKey(keyClip, frame), edit));
        }
        else if (hit.IsTransition && hit.TransitionId is { } transitionId)
        {
            vm.SelectTransition(transitionId);
            items.Add(Item("Delete transition", () => vm.DeleteSelectionCommand.Execute(null), edit));
        }
        else if (hit.IsClip && hit.ClipId is { } clipId)
        {
            if (!vm.SelectedClipIds.Contains(clipId))
            {
                vm.Select(vm.ExpandLinked([clipId], singleSide: false), additive: false);
            }

            items.Add(Item("Cut", () => vm.CutCommand.Execute(null), edit));
            items.Add(Item("Copy", () => vm.CopyCommand.Execute(null)));
            items.Add(Item("Paste", () => vm.PasteCommand.Execute(null), edit && vm.HasClipboard));
            items.Add(new Separator());
            var linked = vm.SelectedClipIds.Select(id => TimelineQuery.FindClip(project, id)?.Clip).OfType<Clip>().ToList();
            if (linked.Count > 0 && linked.All(c => c.LinkId is not null))
            {
                items.Add(Item("Unlink", () => vm.UnlinkCommand.Execute(null), edit));
            }
            else
            {
                items.Add(Item("Link", () => vm.LinkCommand.Execute(null), edit && linked.Count >= 2));
            }

            if (TimelineQuery.SyncOffset(project, clipId) is { } offset && offset != 0)
            {
                var text = (offset > 0 ? "+" : string.Empty) + offset.ToString(CultureInfo.InvariantCulture);
                items.Add(Item("Move into sync (" + text + ")", () => vm.MoveClipIntoSync(clipId), edit));
            }

            items.Add(new Separator());
            items.Add(Item("Delete", () => vm.DeleteSelectionCommand.Execute(null), edit));
            items.Add(Item("Ripple delete", () => vm.RippleDeleteSelectionCommand.Execute(null), edit));
        }
        else if (hit.Zone == HitZone.Gap && hit.TrackId is { } gapTrack)
        {
            vm.SelectGap(gapTrack, hit.GapStart, hit.GapEnd);
            items.Add(Item("Close gap", () => vm.CloseGapCommand.Execute(null), edit));
            items.Add(Item("Paste", () => vm.PasteCommand.Execute(null), edit && vm.HasClipboard));
        }
        else if (hit.Zone is HitZone.Lane or HitZone.Playhead or HitZone.Ruler)
        {
            items.Add(Item("Paste", () => vm.PasteCommand.Execute(null), edit && vm.HasClipboard));
            items.Add(Item("Add marker", () => vm.AddMarkerCommand.Execute(null), edit));
            items.Add(new Separator());
            items.Add(Item("Add video track", () => vm.AddVideoTrackCommand.Execute(null), edit));
            items.Add(Item("Add audio track", () => vm.AddAudioTrackCommand.Execute(null), edit));
        }

        if (items.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu { ItemsSource = items };
        menu.Open(this);
    }

    private static MenuItem Item(string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    // Shortened transitions explain themselves on hover (DESIGN 5.5: "Shortened to 12 frames: ...").
    private void UpdateHoverTip(TimelineViewModel vm, HitResult hit)
    {
        string? tip = null;
        if (hit.IsTransition && hit.TransitionId is { } id && TransitionLimit(vm.Session.Current, id) is { } limit)
        {
            tip = limit;
        }

        if (tip != _hoverTip)
        {
            _hoverTip = tip;
            ToolTip.SetTip(this, tip);
            ToolTip.SetIsOpen(this, tip is not null);
        }
    }

    /// <summary>The red corner flag text of a transition (see <see cref="TransitionSectionViewModel.LimitText"/>).</summary>
    /// <param name="project">The project.</param>
    /// <param name="transitionId">The transition.</param>
    /// <returns>The text, or null.</returns>
    internal static string? TransitionLimit(Project project, string transitionId) =>
        TransitionSectionViewModel.LimitText(project, transitionId);
}
