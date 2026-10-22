using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using Cutlass.App.Controls;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels;

/// <summary>M6: volume keyframes, markers and the requests the view answers with inline editors.</summary>
public sealed partial class TimelineViewModel
{
    /// <summary>The selected volume keyframe (DESIGN 5.5: Delete removes it), or null.</summary>
    [ObservableProperty]
    public partial VolumeKeyRef? SelectedVolumeKey { get; private set; }

    /// <summary>Raised with a marker id when the user asks to edit a marker's name and colour (double-click).</summary>
    public event EventHandler<string>? MarkerEditRequested;

    /// <summary>Raised with a track id when the user asks to rename a track (double-click on its header).</summary>
    public event EventHandler<string>? TrackRenameRequested;

    /// <summary>
    /// Raised when the inline editors (marker editor, track rename box) must close as focus loss does, committing a
    /// valid value: an agent is about to take the lock (AGENT_INTEGRATION 3.2, <c>PrepareForAgent</c>).
    /// </summary>
    public event EventHandler? InlineEditorsCloseRequested;

    /// <summary>Asks the view to commit and close its inline editors (marker editor, track rename).</summary>
    public void CloseInlineEditors() => InlineEditorsCloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Asks the view to show the marker editor.</summary>
    /// <param name="markerId">The marker id.</param>
    public void RequestMarkerEdit(string markerId)
    {
        ArgumentNullException.ThrowIfNull(markerId);
        if (Session.Current.Sequence.Markers.Any(m => m.Id == markerId))
        {
            MarkerEditRequested?.Invoke(this, markerId);
        }
    }

    /// <summary>Asks the view to show the inline rename box of a track.</summary>
    /// <param name="trackId">The track id.</param>
    public void RequestTrackRename(string trackId)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        if (TimelineQuery.FindTrack(Session.Current, trackId) is not null)
        {
            TrackRenameRequested?.Invoke(this, trackId);
        }
    }

    /// <summary>Renames and recolours a marker (one undo step).</summary>
    /// <param name="markerId">The marker id.</param>
    /// <param name="name">The new name.</param>
    /// <param name="color">The colour as <c>#RRGGBB</c> (invalid values keep the old colour).</param>
    public void UpdateMarker(string markerId, string? name, string? color)
    {
        ArgumentNullException.ThrowIfNull(markerId);
        var newName = name?.Trim() ?? string.Empty;
        Run(p => TimelineEdits.UpdateMarker(p, markerId, m => m with { Name = newName, Color = color ?? m.Color }));
    }

    /// <summary>Removes a marker.</summary>
    /// <param name="markerId">The marker id.</param>
    public void RemoveMarker(string markerId)
    {
        ArgumentNullException.ThrowIfNull(markerId);
        Run(p => TimelineEdits.RemoveMarkers(p, [markerId]));
    }

    /// <summary>Selects a volume keyframe (the clip stays selected).</summary>
    /// <param name="clipId">The audio clip.</param>
    /// <param name="frame">The keyframe frame, relative to the clip start.</param>
    public void SelectVolumeKey(string clipId, long frame)
    {
        ArgumentNullException.ThrowIfNull(clipId);
        SelectedVolumeKey = FindKey(Session.Current, clipId, frame) is not null ? new VolumeKeyRef(clipId, frame) : null;
    }

    /// <summary>
    /// Adds a volume keyframe on the clip's volume line (Ctrl+click or double-click, DESIGN 5.5) and selects it. The
    /// key takes the envelope level at that frame, so the line keeps its shape.
    /// </summary>
    /// <param name="clipId">The audio clip.</param>
    /// <param name="frame">The frame relative to the clip start.</param>
    public void AddVolumeKey(string clipId, long frame)
    {
        ArgumentNullException.ThrowIfNull(clipId);
        if (TimelineQuery.FindClip(Session.Current, clipId)?.Clip is not MediaClip { Stream: StreamKind.Audio } clip)
        {
            return;
        }

        var at = Math.Clamp(frame, 0, clip.Duration - 1);
        if (clip.Audio.Keys.Any(k => k.Frame == at))
        {
            SelectVolumeKey(clipId, at);
            return;
        }

        var db = TimelineEdits.EnvelopeAt(clip.Audio.Keys, at);
        Run(p => TimelineEdits.UpdateClips(p, [clipId], c => WithKeys(c, k => k.Add(new VolumeKey(at, db)).Sort(KeyOrder)), "Add Keyframe"));
        if (FindKey(Session.Current, clipId, at) is not null)
        {
            SelectVolumeKey(clipId, at);
        }
    }

    /// <summary>Removes a volume keyframe (Alt+click on its dot, or Delete while it is selected).</summary>
    /// <param name="clipId">The audio clip.</param>
    /// <param name="frame">The keyframe frame, relative to the clip start.</param>
    public void RemoveVolumeKey(string clipId, long frame)
    {
        ArgumentNullException.ThrowIfNull(clipId);
        Run(p => TimelineEdits.UpdateClips(p, [clipId], c => WithKeys(c, k => k.RemoveAll(x => x.Frame == frame)), "Remove Keyframe"));
        if (SelectedVolumeKey is { } key && key.ClipId == clipId && key.Frame == frame)
        {
            SelectedVolumeKey = null;
        }
    }

    /// <summary>Replaces the keyframes of an audio clip through a transform (other clips are returned unchanged).</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="change">Transforms the keyframes.</param>
    /// <returns>The clip with new keyframes.</returns>
    public static Clip WithKeys(Clip clip, Func<ImmutableArray<VolumeKey>, ImmutableArray<VolumeKey>> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (clip is not MediaClip { Stream: StreamKind.Audio } m)
        {
            return clip;
        }

        var keys = m.Audio.Keys.IsDefault ? ImmutableArray<VolumeKey>.Empty : m.Audio.Keys;
        return m with { Audio = m.Audio with { Keys = change(keys) } };
    }

    private static readonly Comparer<VolumeKey> KeyOrder = Comparer<VolumeKey>.Create((a, b) => a.Frame.CompareTo(b.Frame));

    private static VolumeKey? FindKey(Project project, string clipId, long frame) =>
        TimelineQuery.FindClip(project, clipId)?.Clip is MediaClip { Stream: StreamKind.Audio } clip && !clip.Audio.Keys.IsDefault
            ? clip.Audio.Keys.FirstOrDefault(k => k.Frame == frame)
            : null;

    private void FilterVolumeKey()
    {
        if (SelectedVolumeKey is { } key && (!_selected.Contains(key.ClipId) || FindKey(Session.Current, key.ClipId, key.Frame) is null))
        {
            SelectedVolumeKey = null;
        }
    }
}
