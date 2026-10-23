using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels.Timeline;

/// <summary>M6 commands: copy and paste, link and sync, tracks, and the In/Out range.</summary>
public sealed partial class TimelineViewModel
{
    private ClipClipboard _clipboard = ClipClipboard.Empty;

    /// <summary>The track whose header was clicked last (Remove Track and the header context menu act on it), or null.</summary>
    [ObservableProperty]
    public partial string? SelectedTrackId { get; private set; }

    /// <summary>
    /// Asks the user to confirm a destructive action (title, message), returning true to go ahead. Set by the
    /// <see cref="Views.TimelineView"/>; while it is null the action runs without asking (it is undoable).
    /// </summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>Ask through <see cref="ConfirmAsync"/> before deleting a track that holds clips (Settings, DESIGN 10).</summary>
    public bool ConfirmTrackDeletes { get; set; } = true;

    /// <summary>True when Ctrl+C or Ctrl+X copied clips that Ctrl+V can paste.</summary>
    public bool HasClipboard => !_clipboard.IsEmpty;

    /// <summary>Makes a track the selected track (a click on its header). Clip, gap and transition selection stay.</summary>
    /// <param name="trackId">The track id, or null to clear.</param>
    public void SelectTrack(string? trackId) =>
        SelectedTrackId = trackId is not null && TimelineQuery.FindTrack(Session.Current, trackId) is not null ? trackId : null;

    /// <summary>Renames a track (double-click on its header name). Blank names are ignored.</summary>
    /// <param name="trackId">The track id.</param>
    /// <param name="name">The new name.</param>
    public void RenameTrack(string trackId, string? name)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return;
        }

        Run(p => TimelineEdits.SetTrack(p, trackId, t => t with { Name = trimmed }, "Rename Track"));
    }

    /// <summary>Adds a track of the same kind directly above or below a track (header context menu).</summary>
    /// <param name="trackId">The reference track.</param>
    /// <param name="above">True for above (visually), false for below.</param>
    public void AddTrackNextTo(string trackId, bool above)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        var project = Session.Current;
        if (TimelineQuery.FindTrack(project, trackId) is not { } track)
        {
            return;
        }

        var index = TimelineQuery.KindIndex(project, trackId);
        // Video tracks stack upward (V2 above V1), audio tracks downward (A2 below A1), so "above" maps differently.
        var at = track.Kind == TrackKind.Video ? (above ? index + 1 : index) : (above ? index : index + 1);
        AddTrack(track.Kind, at);
    }

    /// <summary>Removes a track, asking first through <see cref="ConfirmAsync"/> when it holds clips (DESIGN 4.5).</summary>
    /// <param name="trackId">The track id.</param>
    /// <returns>A task that completes when the track was removed or the user declined.</returns>
    public async Task RemoveTrackAsync(string trackId)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        if (TimelineQuery.FindTrack(Session.Current, trackId) is not { } track)
        {
            return;
        }

        if (!track.Clips.IsEmpty && ConfirmTrackDeletes && ConfirmAsync is { } confirm)
        {
            var count = track.Clips.Length;
            var message = track.Name + " holds " + count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (count == 1 ? " clip" : " clips") + ". Delete the track and its clips?";
            if (!await confirm("Delete track", message))
            {
                return;
            }
        }

        Run(p => TimelineEdits.RemoveTrack(p, trackId));
        if (SelectedTrackId == trackId && TimelineQuery.FindTrack(Session.Current, trackId) is null)
        {
            SelectedTrackId = null;
        }
    }

    /// <summary>"Move into sync" on a sync badge: moves the clip by its sync offset.</summary>
    /// <param name="clipId">The drifted clip.</param>
    public void MoveClipIntoSync(string clipId)
    {
        ArgumentNullException.ThrowIfNull(clipId);
        Run(p => TimelineEdits.MoveIntoSync(Ids, p, clipId));
    }

    /// <summary>Ctrl+C: copies the selected clips with their relative offsets and tracks.</summary>
    [RelayCommand]
    private void Copy()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        var copied = ClipClipboard.Copy(Session.Current, _selected);
        if (!copied.IsEmpty)
        {
            _clipboard = copied;
            OnPropertyChanged(nameof(HasClipboard));
        }
    }

    /// <summary>Ctrl+X: copies the selected clips, then lifts them.</summary>
    [RelayCommand]
    private void Cut()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        Copy();
        var ids = _selected.ToList();
        Run(p => TimelineEdits.Lift(p, ids) with { Label = "Cut" });
    }

    /// <summary>Ctrl+V: pastes at the playhead in overwrite mode on the same tracks (DESIGN 5.5).</summary>
    [RelayCommand]
    private void Paste() => PasteAt(EditMode.Overwrite);

    /// <summary>Ctrl+Shift+V: pastes at the playhead in insert mode.</summary>
    [RelayCommand]
    private void PasteInsert() => PasteAt(EditMode.Insert);

    /// <summary>Links the selected clips into one new link group.</summary>
    [RelayCommand]
    private void Link()
    {
        var ids = _selected.ToList();
        if (ids.Count > 0)
        {
            Run(p => TimelineEdits.Link(Ids, p, ids));
        }
    }

    /// <summary>Removes the selected clips from their link groups.</summary>
    [RelayCommand]
    private void Unlink()
    {
        var ids = _selected.ToList();
        if (ids.Count > 0)
        {
            Run(p => TimelineEdits.Unlink(p, ids));
        }
    }

    /// <summary>Ctrl+L: unlinks when every selected clip is linked, otherwise links the selection.</summary>
    [RelayCommand]
    private void ToggleLink()
    {
        var project = Session.Current;
        var clips = _selected.Select(id => TimelineQuery.FindClip(project, id)?.Clip).OfType<Clip>().ToList();
        if (clips.Count == 0)
        {
            return;
        }

        if (clips.All(c => c.LinkId is not null))
        {
            Unlink();
        }
        else
        {
            Link();
        }
    }

    /// <summary>Moves every selected clip that drifted from its partner back into sync (one undo step).</summary>
    [RelayCommand]
    private void MoveIntoSync()
    {
        var ids = _selected.Order(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        Run(p =>
        {
            var current = p;
            var warnings = ImmutableArray.CreateBuilder<string>();
            foreach (var id in ids)
            {
                // A partner moved earlier in this loop may already have restored the sync of this clip.
                var step = TimelineEdits.MoveIntoSync(Ids, current, id);
                warnings.AddRange(step.Warnings);
                current = step.Project;
            }

            return ReferenceEquals(current, p)
                ? EditResult.Unchanged(p, "Move into Sync", [.. warnings])
                : new EditResult(current, "Move into Sync", warnings.ToImmutable(), ImmutableArray<string>.Empty);
        });
    }

    /// <summary>"+ Track": adds a video track at the top.</summary>
    [RelayCommand]
    private void AddVideoTrack() => AddTrack(TrackKind.Video, null);

    /// <summary>"+ Track": adds an audio track at the bottom.</summary>
    [RelayCommand]
    private void AddAudioTrack() => AddTrack(TrackKind.Audio, null);

    /// <summary>Removes the selected track (confirms when it holds clips).</summary>
    /// <returns>A task that completes when done.</returns>
    [RelayCommand]
    private Task RemoveSelectedTrack() => SelectedTrackId is { } id ? RemoveTrackAsync(id) : Task.CompletedTask;

    /// <summary>Selects every clip on unlocked tracks that overlaps the In/Out range.</summary>
    [RelayCommand]
    private void SelectInOutRange()
    {
        if (InOutRange() is not { } range)
        {
            return;
        }

        var ids = Session.Current.Sequence.Tracks.Where(t => !t.Locked).SelectMany(t => t.Clips)
            .Where(c => c.Start < range.Out && c.End > range.In).Select(c => c.Id);
        Select(ids, false);
    }

    /// <summary>
    /// Removes the In/Out range from every unlocked track and closes it up: splits at In and Out, removes what lies
    /// between and moves everything after the Out left by the range length. The range is cleared afterwards.
    /// </summary>
    [RelayCommand]
    private void RippleDeleteInOutRange()
    {
        if (InOutRange() is not { } range)
        {
            return;
        }

        Run(p => RippleDeleteRange(p, range.In, range.Out));
    }

    private (long In, long Out)? InOutRange()
    {
        var seq = Session.Current.Sequence;
        if (seq.InFrame is null && seq.OutFrame is null)
        {
            return null;
        }

        // Only one of In/Out set: the range runs from the sequence start or to the sequence end (unspecified in DESIGN).
        var start = seq.InFrame ?? 0;
        var end = seq.OutFrame ?? TimelineQuery.Duration(Session.Current);
        return end > start ? (start, end) : null;
    }

    // AGENT_INTEGRATION 2.6: the In/Out ripple delete is TimelineEdits.RemoveRanges with one range on every unlocked
    // track (the generalised split, lift and move-left sequence that used to live here); the menu keeps its label.
    private EditResult RippleDeleteRange(Project project, long inFrame, long outFrame) =>
        TimelineEdits.RemoveRanges(Ids, project, [(inFrame, outFrame)], null, ripple: true) with { Label = "Ripple Delete Range" };

    private void PasteAt(EditMode mode)
    {
        if (_clipboard.IsEmpty)
        {
            return;
        }

        var clipboard = _clipboard;
        var at = Playhead;
        var result = Run(p => clipboard.Paste(Ids, p, at, mode));
        if (result.Select.IsDefaultOrEmpty)
        {
            return;
        }

        Select(result.Select, false);
        // Unspecified in DESIGN: like three-point edits, the playhead moves to the end of the pasted clips.
        Playhead = result.Select.Select(id => TimelineQuery.FindClip(Session.Current, id)?.Clip.End ?? at).Max();
    }

    private void AddTrack(TrackKind kind, int? kindIndex)
    {
        var result = Run(p => TimelineEdits.AddTrack(Ids, p, kind, kindIndex));
        if (!result.Select.IsDefaultOrEmpty)
        {
            SelectedTrackId = result.Select[0];
        }
    }
}
