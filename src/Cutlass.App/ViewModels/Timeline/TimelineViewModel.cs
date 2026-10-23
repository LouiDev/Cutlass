using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Controls.Drag;
using Cutlass.App.Controls.Timeline;
using Cutlass.App.Services;
using Cutlass.App.ViewModels.MediaBin;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;

namespace Cutlass.App.ViewModels.Timeline;

/// <summary>
/// The timeline panel (DESIGN 5, TECH_BRIEF 10.1): playhead, tool, snapping, selection, In/Out, view state and every
/// timeline command. There are no per-clip view models; the <see cref="TimelineControl"/> draws
/// <see cref="ProjectSession.Current"/> directly. UI thread only. Selection and view state are never undoable.
/// </summary>
public sealed partial class TimelineViewModel : ObservableObject
{
    private const double ZoomStepFactor = 1.5;
    private const int StepMany = 10;
    private static readonly double ZoomRatio = TimelineViewState.MaxPixelsPerFrame / TimelineViewState.MinPixelsPerFrame;

    private IReadOnlySet<string> _selected = ImmutableHashSet<string>.Empty;
    private bool _syncingInOut;
    private bool _syncingScroll;

    /// <summary>Creates the view model over a session with random ids.</summary>
    /// <param name="session">The project session.</param>
    public TimelineViewModel(ProjectSession session)
        : this(session, RandomIdGenerator.Shared)
    {
    }

    /// <summary>Creates the view model with an explicit id generator (tests use a sequential one).</summary>
    /// <param name="session">The project session.</param>
    /// <param name="ids">The id generator for edits that create clips, links and markers.</param>
    public TimelineViewModel(ProjectSession session, IIdGenerator ids)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ids);
        Session = session;
        Ids = ids;
        ViewState = new TimelineViewState();
        Tool = EditTool.Select;
        SnapEnabled = true;
        LinkedSelection = true;
        StillDurationSeconds = 5;
        SyncScroll();
        SyncInOut();
        UpdateContent();
        session.Changed += OnSessionChanged;
        ViewState.Changed += OnViewStateChanged;
    }

    /// <summary>Raised whenever the selection (clips, transition or gap) is replaced.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when zoom, scroll, viewport or track heights changed (the control repaints).</summary>
    public event EventHandler? ViewChanged;

    /// <summary>Raised when a drag starts (DESIGN 5.6: starting a drag pauses playback).</summary>
    public event EventHandler? DragStarted;

    /// <summary>Raised by <see cref="EscapeCommand"/> while a drag runs; the control cancels the drag.</summary>
    public event EventHandler? CancelDragRequested;

    /// <summary>Raised with each warning of an edit (clamps, locked tracks), for a toast.</summary>
    public event EventHandler<string>? WarningRaised;

    /// <summary>Raised when new thumbnails or peaks arrived (the control repaints).</summary>
    public event EventHandler? ArtifactsChanged;

    /// <summary>The project session.</summary>
    public ProjectSession Session { get; }

    /// <summary>The id generator used for edits.</summary>
    public IIdGenerator Ids { get; }

    /// <summary>Zoom, scroll and track heights.</summary>
    public TimelineViewState ViewState { get; }

    /// <summary>The project as drawn (live during gestures).</summary>
    public Project Project => Session.Current;

    /// <summary>The playhead frame, clamped to <c>[0, max(SequenceEnd, 0)]</c>.</summary>
    [ObservableProperty]
    public partial long Playhead { get; set; }

    /// <summary>The active tool.</summary>
    [ObservableProperty]
    public partial EditTool Tool { get; set; }

    /// <summary>Snapping (S, magnet).</summary>
    [ObservableProperty]
    public partial bool SnapEnabled { get; set; }

    /// <summary>Linked selection (on by default).</summary>
    [ObservableProperty]
    public partial bool LinkedSelection { get; set; }

    /// <summary>The sequence In point (mirrors <see cref="Sequence.InFrame"/>; setting it records an undo step).</summary>
    [ObservableProperty]
    public partial long? InPoint { get; set; }

    /// <summary>The sequence Out point, exclusive (mirrors <see cref="Sequence.OutFrame"/>).</summary>
    [ObservableProperty]
    public partial long? OutPoint { get; set; }

    /// <summary>The selected transition, or null.</summary>
    [ObservableProperty]
    public partial string? SelectedTransitionId { get; private set; }

    /// <summary>The selected gap, or null.</summary>
    [ObservableProperty]
    public partial TimelineGap? SelectedGap { get; private set; }

    /// <summary>What the viewer should show during a trim drag, or null (set by the control).</summary>
    [ObservableProperty]
    public partial ViewerHint? DragHint { get; set; }

    /// <summary>The default still length in seconds for drops and three-point edits (Settings, 5 s by default).</summary>
    [ObservableProperty]
    public partial double StillDurationSeconds { get; set; }

    /// <summary>The logarithmic zoom slider, 0 (0.01 px per frame) to 100 (40 px per frame).</summary>
    [ObservableProperty]
    public partial double ZoomLevel { get; set; }

    /// <summary>The horizontal scroll bar value in pixels.</summary>
    [ObservableProperty]
    public partial double HorizontalOffset { get; set; }

    /// <summary>The horizontal scroll bar maximum in pixels.</summary>
    [ObservableProperty]
    public partial double HorizontalMaximum { get; private set; }

    /// <summary>The horizontal scroll bar viewport size in pixels.</summary>
    [ObservableProperty]
    public partial double HorizontalViewport { get; private set; }

    /// <summary>The vertical scroll bar value in pixels.</summary>
    [ObservableProperty]
    public partial double VerticalOffset { get; set; }

    /// <summary>The vertical scroll bar maximum in pixels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVerticalScroll))]
    public partial double VerticalMaximum { get; private set; }

    /// <summary>The vertical scroll bar viewport size in pixels.</summary>
    [ObservableProperty]
    public partial double VerticalViewport { get; private set; }

    /// <summary>
    /// True while the timeline control runs a drag (set by the control). Raises <c>PropertyChanged</c>, so the agent
    /// lock learns when a gesture ended (AGENT_INTEGRATION 3.9).
    /// </summary>
    public bool IsDragging
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// True while an agent holds or pauses the project (AGENT_INTEGRATION 3.7): the control starts only view drags
    /// (scrub, marquee, playhead, track height), clicks select, and edit drags, razor clicks, header buttons, edit
    /// context menu items and drops are refused; undo and redo are disabled. The session write gate is the backstop.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand), nameof(RedoCommand))]
    public partial bool IsReadOnly { get; set; }

    /// <summary>True when the tracks overflow vertically (the vertical scroll bar shows).</summary>
    public bool HasVerticalScroll => VerticalMaximum > 0;

    /// <summary>The selected clip ids. Replaced (never mutated) on every change.</summary>
    public IReadOnlySet<string> SelectedClipIds => _selected;

    /// <summary>Last frame + 1 of the current project (0 when empty).</summary>
    public long SequenceEnd => TimelineQuery.Duration(Session.Current);

    /// <summary>The default still length in frames at the sequence rate.</summary>
    public long StillDurationFrames
    {
        get
        {
            var rate = Session.Current.Sequence.Settings.Rate.Value.ToDouble();
            return Math.Max(1, (long)Math.Round(StillDurationSeconds * rate, MidpointRounding.AwayFromZero));
        }
    }

    /// <summary>The track layout of the current project at the current view state.</summary>
    /// <returns>The layout.</returns>
    public TrackLayout GetLayout() => ViewState.Layout(Session.Current.Sequence);

    /// <summary>
    /// Selects clips. Ids that do not exist or sit on locked tracks are ignored (DESIGN 5.5). Link groups are not
    /// expanded here; callers use <see cref="ExpandLinked"/>. A clip selection clears the transition and gap selection.
    /// </summary>
    /// <param name="clipIds">The clip ids.</param>
    /// <param name="additive">Add to the selection instead of replacing it.</param>
    public void Select(IEnumerable<string> clipIds, bool additive)
    {
        ArgumentNullException.ThrowIfNull(clipIds);
        var set = additive ? new HashSet<string>(_selected, StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
        set.UnionWith(Selectable(clipIds));
        SetSelection(set, null, null);
    }

    /// <summary>Toggles clips in the selection (Ctrl+click).</summary>
    /// <param name="clipIds">The clip ids (a clip and, with linked selection, its partners).</param>
    public void ToggleSelection(IEnumerable<string> clipIds)
    {
        ArgumentNullException.ThrowIfNull(clipIds);
        var ids = Selectable(clipIds).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var set = new HashSet<string>(_selected, StringComparer.Ordinal);
        if (ids.All(set.Contains))
        {
            set.ExceptWith(ids);
        }
        else
        {
            set.UnionWith(ids);
        }

        SetSelection(set, null, null);
    }

    /// <summary>Selects a transition (click on its block).</summary>
    /// <param name="transitionId">The transition id.</param>
    public void SelectTransition(string transitionId)
    {
        ArgumentNullException.ThrowIfNull(transitionId);
        if (TimelineQuery.FindTransition(Session.Current, transitionId) is { } found && !found.Track.Locked)
        {
            SetSelection(new HashSet<string>(StringComparer.Ordinal), transitionId, null);
        }
    }

    /// <summary>Selects a gap (a click without a drag on empty space between clips).</summary>
    /// <param name="trackId">The track.</param>
    /// <param name="start">The gap start.</param>
    /// <param name="end">The gap end (exclusive).</param>
    public void SelectGap(string trackId, long start, long end)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        if (TimelineQuery.FindTrack(Session.Current, trackId) is { Locked: false })
        {
            SetSelection(new HashSet<string>(StringComparer.Ordinal), null, new TimelineGap(trackId, start, end));
        }
    }

    /// <summary>Expands ids by their link groups when linked selection is on and Alt is not held.</summary>
    /// <param name="clipIds">The clip ids.</param>
    /// <param name="singleSide">True when Alt is held (one side only).</param>
    /// <returns>The ids to select.</returns>
    public IReadOnlyList<string> ExpandLinked(IEnumerable<string> clipIds, bool singleSide) =>
        LinkedSelection && !singleSide ? TimelineQuery.WithLinkedPartners(Session.Current, clipIds) : [.. clipIds];

    /// <summary>"," behaviour: inserts the whole media at the playhead (or the timeline In) on the targeted tracks.</summary>
    /// <param name="assetId">The media id.</param>
    public void InsertMediaAtPlayhead(string assetId) => SourceEditAt(assetId, null, null, EditMode.Insert);

    /// <summary>"." behaviour: overwrites with the whole media at the playhead (or the timeline In).</summary>
    /// <param name="assetId">The media id.</param>
    public void OverwriteMediaAtPlayhead(string assetId) => SourceEditAt(assetId, null, null, EditMode.Overwrite);

    /// <summary>
    /// A drop from the media bin (DESIGN 5.5): a video clip on the hovered video track and a linked audio clip on the
    /// matching audio track (same index, else A1), or the other way round when an audio track is hovered.
    /// </summary>
    /// <param name="assetId">The media id.</param>
    /// <param name="trackId">The hovered track.</param>
    /// <param name="frame">The drop frame.</param>
    /// <param name="insert">True for insert (Ctrl at drop), false for overwrite.</param>
    public void PlaceMedia(string assetId, string trackId, long frame, bool insert) => PlaceMedia([assetId], trackId, frame, insert);

    /// <summary>Drops several media items one after another as one undo step.</summary>
    /// <param name="assetIds">The media ids in order.</param>
    /// <param name="trackId">The hovered track.</param>
    /// <param name="frame">The drop frame.</param>
    /// <param name="insert">True for insert, false for overwrite.</param>
    public void PlaceMedia(IReadOnlyList<string> assetIds, string trackId, long frame, bool insert)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        ArgumentNullException.ThrowIfNull(trackId);
        var mode = insert ? EditMode.Insert : EditMode.Overwrite;
        var result = Run(p => PlaceSequential(p, assetIds, trackId, Math.Max(0, frame), mode));
        if (!result.Select.IsDefaultOrEmpty)
        {
            Select(result.Select, false);
        }
    }

    /// <summary>
    /// Imports files dropped from the file manager and returns their media ids in order, including files that were
    /// already in the project. Set by the shell; while it is null the timeline refuses file drops.
    /// </summary>
    public Func<IReadOnlyList<string>, Task<IReadOnlyList<string>>>? ImportFiles { get; set; }

    /// <summary>
    /// Counts project replacements (New, Open, Recover). Work that awaits (an import, a file drop) captures it first
    /// and drops its result when it changed, so nothing lands in a project that became current in the meantime.
    /// </summary>
    public long ProjectGeneration { get; private set; }

    /// <summary>
    /// A file drop onto the timeline (DESIGN 2.1): imports the files, then places them one after another at the drop
    /// frame like a drop from the media bin (one undo step for the placement).
    /// </summary>
    /// <param name="paths">The dropped files or folders.</param>
    /// <param name="trackId">The hovered track.</param>
    /// <param name="frame">The drop frame.</param>
    /// <param name="insert">True for insert (Ctrl at drop), false for overwrite.</param>
    /// <returns>A task that completes when the media was placed.</returns>
    public async Task DropFilesAsync(IReadOnlyList<string> paths, string trackId, long frame, bool insert)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(trackId);
        if (ImportFiles is not { } import || paths.Count == 0 || IsReadOnly)
        {
            return;
        }

        var generation = ProjectGeneration;
        var ids = await import(paths);
        if (generation != ProjectGeneration)
        {
            // Another project was opened while the files were imported: the drop belonged to the old one.
            return;
        }

        if (ids.Count > 0 && TimelineQuery.FindTrack(Session.Current, trackId) is not null)
        {
            PlaceMedia(ids, trackId, frame, insert);
        }
    }

    /// <summary>The clips a drop would place, with starts relative to the drop frame (the drag ghost).</summary>
    /// <param name="assetIds">The media ids in order.</param>
    /// <param name="trackId">The hovered track.</param>
    /// <returns>The placements.</returns>
    public ImmutableArray<ClipPlacement> PreviewPlacements(IReadOnlyList<string> assetIds, string trackId)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        var project = Session.Current;
        var builder = ImmutableArray.CreateBuilder<ClipPlacement>();
        long offset = 0;
        foreach (var id in assetIds)
        {
            var placements = PlacementsFor(project, id, trackId);
            long length = 0;
            foreach (var p in placements)
            {
                builder.Add(p with { Clip = p.Clip with { Start = p.Clip.Start + offset } });
                length = Math.Max(length, p.Clip.Duration);
            }

            offset += length;
        }

        return builder.ToImmutable();
    }

    /// <summary>Razor click (DESIGN 5.5): splits the clip and its partners, or every unlocked track with Shift.</summary>
    /// <param name="clipId">The clicked clip.</param>
    /// <param name="frame">The split frame.</param>
    /// <param name="allTracks">True for Shift+click.</param>
    public void RazorAt(string clipId, long frame, bool allTracks)
    {
        ArgumentNullException.ThrowIfNull(clipId);
        if (allTracks)
        {
            Run(p => TimelineEdits.Split(Ids, p, UnlockedTrackIds(p), frame));
        }
        else
        {
            Run(p => TimelineEdits.SplitLinked(Ids, p, clipId, frame));
        }
    }

    /// <summary>Toggles whether a track receives source edits and Ctrl+K splits.</summary>
    /// <param name="trackId">The track id.</param>
    public void ToggleTrackTarget(string trackId) => Run(p => TimelineEdits.SetTrack(p, trackId, t => t with { Targeted = !t.Targeted }, "Target Track"));

    /// <summary>Toggles a video track's visibility.</summary>
    /// <param name="trackId">The track id.</param>
    public void ToggleTrackHidden(string trackId) => Run(p => TimelineEdits.SetTrack(p, trackId, t => t with { Hidden = !t.Hidden }, "Track Visibility"));

    /// <summary>Toggles a track's lock. Locking drops its clips from the selection.</summary>
    /// <param name="trackId">The track id.</param>
    public void ToggleTrackLocked(string trackId) => Run(p => TimelineEdits.SetTrack(p, trackId, t => t with { Locked = !t.Locked }, "Lock Track"));

    /// <summary>Toggles an audio track's mute.</summary>
    /// <param name="trackId">The track id.</param>
    public void ToggleTrackMuted(string trackId) => Run(p => TimelineEdits.SetTrack(p, trackId, t => t with { Muted = !t.Muted }, "Mute Track"));

    /// <summary>Toggles an audio track's solo.</summary>
    /// <param name="trackId">The track id.</param>
    public void ToggleTrackSolo(string trackId) => Run(p => TimelineEdits.SetTrack(p, trackId, t => t with { Solo = !t.Solo }, "Solo Track"));

    /// <summary>Sets an audio track's gain (right-click on the header gain field resets it to 0 dB).</summary>
    /// <param name="trackId">The track id.</param>
    /// <param name="gainDb">The gain in dB, clamped to <see cref="TrackGain.MinDb"/> to <see cref="TrackGain.MaxDb"/>.</param>
    public void SetTrackGain(string trackId, double gainDb)
    {
        var gain = TrackGain.Clamp(gainDb);
        Run(p => TimelineEdits.SetTrack(p, trackId, t => t with { GainDb = gain }, "Track Gain"));
    }

    /// <summary>
    /// Thumbnail sheets and waveform peaks for the filmstrips and waveforms (DESIGN 5.1), or null while ffmpeg is
    /// unavailable. Set by the composition root.
    /// </summary>
    public IMediaArtifactSource? Artifacts
    {
        get;
        set
        {
            if (field is not null)
            {
                field.Changed -= OnArtifactsChanged;
            }

            field = value;
            if (value is not null)
            {
                value.Changed += OnArtifactsChanged;
            }

            ArtifactsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Answers whether a media's file is missing (DESIGN 11 offline), so the control hatches its clips and shows the
    /// offline badge; the clips themselves stay. Set by the shell to <see cref="MediaBinViewModel.IsMediaOffline"/>.
    /// While it is null only clips whose media is not in the project count as offline.
    /// </summary>
    public Func<string, bool>? IsMediaOffline { get; set; }

    /// <summary>Raised when the offline state of any media changed, so the control redraws.</summary>
    public event EventHandler? MediaLinkStatesChanged;

    /// <summary>Raises <see cref="MediaLinkStatesChanged"/> (called by the shell when the media bin's link states change).</summary>
    public void NotifyMediaLinkStatesChanged() => MediaLinkStatesChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="DragStarted"/> (called by the control).</summary>
    public void NotifyDragStarted() => DragStarted?.Invoke(this, EventArgs.Empty);

    /// <summary>Runs an edit through the session and reports its warnings.</summary>
    /// <param name="edit">The edit.</param>
    /// <param name="coalesceKey">The coalesce key, or null.</param>
    /// <returns>The result.</returns>
    public EditResult Run(Func<Project, EditResult> edit, string? coalesceKey = null)
    {
        var result = Session.Execute(edit, coalesceKey);
        ReportWarnings(result);
        return result;
    }

    /// <summary>Reports the warnings of an edit result (drags call it on commit).</summary>
    /// <param name="result">The result.</param>
    public void ReportWarnings(EditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Warnings.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var warning in result.Warnings.Distinct(StringComparer.Ordinal))
        {
            WarningRaised?.Invoke(this, warning);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Session.Undo();

    private bool CanUndo() => Session.CanUndo && !IsReadOnly;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Session.Redo();

    private bool CanRedo() => Session.CanRedo && !IsReadOnly;

    /// <summary>Delete, Backspace: lifts the selected clips, closes the selected gap or removes the selected transition.</summary>
    [RelayCommand]
    private void DeleteSelection()
    {
        if (SelectedVolumeKey is { } key)
        {
            RemoveVolumeKey(key.ClipId, key.Frame);
        }
        else if (SelectedGap is { } gap)
        {
            Run(p => TimelineEdits.CloseGap(p, gap.TrackId, gap.Start));
        }
        else if (SelectedTransitionId is { } transition)
        {
            Run(p => TimelineEdits.RemoveTransitions(p, [transition]));
        }
        else if (_selected.Count > 0)
        {
            var ids = _selected.ToList();
            Run(p => TimelineEdits.Lift(p, ids));
        }
    }

    /// <summary>Shift+Delete: ripple deletes the selected clips (or closes the selected gap).</summary>
    [RelayCommand]
    private void RippleDeleteSelection()
    {
        if (SelectedGap is not null || SelectedTransitionId is not null)
        {
            DeleteSelection();
        }
        else if (_selected.Count > 0)
        {
            var ids = _selected.ToList();
            Run(p => TimelineEdits.RippleDelete(p, ids));
        }
    }

    /// <summary>Ctrl+K: splits the selection at the playhead, or the targeted unlocked tracks when nothing is selected.</summary>
    [RelayCommand]
    private void SplitAtPlayhead()
    {
        var frame = Playhead;
        if (_selected.Count > 0)
        {
            var ids = _selected.ToList();
            Run(p => TimelineEdits.Split(Ids, p, [.. p.Sequence.Tracks.Select(t => t.Id)], frame, ids));
        }
        else
        {
            Run(p => TimelineEdits.Split(Ids, p, [.. p.Sequence.Tracks.Where(t => t.Targeted && !t.Locked).Select(t => t.Id)], frame));
        }
    }

    /// <summary>Ctrl+Shift+K: splits every unlocked track at the playhead.</summary>
    [RelayCommand]
    private void SplitAllAtPlayhead()
    {
        var frame = Playhead;
        Run(p => TimelineEdits.Split(Ids, p, UnlockedTrackIds(p), frame));
    }

    /// <summary>Ctrl+A: selects every clip on unlocked tracks.</summary>
    [RelayCommand]
    private void SelectAll() =>
        Select(Session.Current.Sequence.Tracks.Where(t => !t.Locked).SelectMany(t => t.Clips).Select(c => c.Id), false);

    /// <summary>Esc: cancels the running drag, otherwise deselects (DESIGN 5.3). Bind the window-level Esc here.</summary>
    [RelayCommand]
    private void Escape()
    {
        if (IsDragging)
        {
            CancelDragRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ClearSelection();
        }
    }

    /// <summary>Esc (when no drag runs): deselects everything.</summary>
    [RelayCommand]
    private void ClearSelection() => SetSelection(new HashSet<string>(StringComparer.Ordinal), null, null);

    /// <summary><c>+</c>: zooms in around the playhead.</summary>
    [RelayCommand]
    private void ZoomIn() => ViewState.ZoomBy(ZoomStepFactor, PlayheadAnchorX());

    /// <summary><c>-</c>: zooms out around the playhead.</summary>
    [RelayCommand]
    private void ZoomOut() => ViewState.ZoomBy(1 / ZoomStepFactor, PlayheadAnchorX());

    /// <summary>Shift+Z: zooms to fit the sequence.</summary>
    [RelayCommand]
    private void ZoomToFit() => ViewState.ZoomToFit(SequenceEnd);

    /// <summary>Home: the sequence start.</summary>
    [RelayCommand]
    private void GoToStart() => Playhead = 0;

    /// <summary>End: the sequence end.</summary>
    [RelayCommand]
    private void GoToEnd() => Playhead = SequenceEnd;

    /// <summary>Left: one frame back.</summary>
    [RelayCommand]
    private void StepBackward() => Playhead -= 1;

    /// <summary>Right: one frame forward.</summary>
    [RelayCommand]
    private void StepForward() => Playhead += 1;

    /// <summary>Shift+Left: ten frames back.</summary>
    [RelayCommand]
    private void StepBackwardMany() => Playhead -= StepMany;

    /// <summary>Shift+Right: ten frames forward.</summary>
    [RelayCommand]
    private void StepForwardMany() => Playhead += StepMany;

    /// <summary>Up: the previous edit point.</summary>
    [RelayCommand]
    private void PreviousEdit()
    {
        if (TimelineQuery.PreviousEditPoint(Session.Current, Playhead) is { } frame)
        {
            Playhead = frame;
        }
    }

    /// <summary>Down: the next edit point.</summary>
    [RelayCommand]
    private void NextEdit()
    {
        if (TimelineQuery.NextEditPoint(Session.Current, Playhead) is { } frame)
        {
            Playhead = frame;
        }
    }

    /// <summary>I: marks In at the playhead.</summary>
    [RelayCommand]
    private void SetIn()
    {
        var frame = Playhead;
        var outFrame = Session.Current.Sequence.OutFrame;
        // A new In at or after the current Out drops the Out instead of pushing it (the simplest consistent choice).
        Run(p => TimelineEdits.SetInOut(p, frame, outFrame is { } o && o <= frame ? null : outFrame));
    }

    /// <summary>O: marks Out so the frame under the playhead is the last frame of the range (Out is exclusive).</summary>
    [RelayCommand]
    private void SetOut()
    {
        var outFrame = Playhead + 1;
        var inFrame = Session.Current.Sequence.InFrame;
        Run(p => TimelineEdits.SetInOut(p, inFrame is { } i && i >= outFrame ? null : inFrame, outFrame));
    }

    /// <summary>X: marks In and Out around the clip under the playhead on the topmost targeted track that has one.</summary>
    [RelayCommand]
    private void MarkClip()
    {
        var project = Session.Current;
        var frame = Playhead;
        var clip = TrackPriority(project).Select(t => TimelineQuery.ClipAt(t, frame)).FirstOrDefault(c => c is not null);
        if (clip is not null)
        {
            Run(p => TimelineEdits.SetInOut(p, clip.Start, clip.End));
        }
    }

    /// <summary>Ctrl+Shift+X: clears In and Out.</summary>
    [RelayCommand]
    private void ClearInOut() => Run(p => TimelineEdits.SetInOut(p, null, null));

    /// <summary>Shift+I: goes to the In point.</summary>
    [RelayCommand]
    private void GoToIn()
    {
        if (InPoint is { } frame)
        {
            Playhead = frame;
        }
    }

    /// <summary>Shift+O: goes to the last frame of the In/Out range.</summary>
    [RelayCommand]
    private void GoToOut()
    {
        if (OutPoint is { } frame)
        {
            Playhead = Math.Max(0, frame - 1);
        }
    }

    /// <summary>M: adds a marker at the playhead.</summary>
    [RelayCommand]
    private void AddMarker()
    {
        var frame = Playhead;
        Run(p => TimelineEdits.AddMarker(Ids, p, frame));
    }

    /// <summary>Shift+M: the next marker.</summary>
    [RelayCommand]
    private void NextMarker()
    {
        var next = Session.Current.Sequence.Markers.Where(m => m.Frame > Playhead).Select(m => (long?)m.Frame).Min();
        if (next is { } frame)
        {
            Playhead = frame;
        }
    }

    /// <summary>Ctrl+Shift+M: the previous marker.</summary>
    [RelayCommand]
    private void PreviousMarker()
    {
        var previous = Session.Current.Sequence.Markers.Where(m => m.Frame < Playhead).Select(m => (long?)m.Frame).Max();
        if (previous is { } frame)
        {
            Playhead = frame;
        }
    }

    /// <summary>Alt+Left: nudges the selection one frame left.</summary>
    [RelayCommand]
    private void NudgeLeft() => Nudge(-1);

    /// <summary>Alt+Right: nudges the selection one frame right.</summary>
    [RelayCommand]
    private void NudgeRight() => Nudge(1);

    /// <summary>S: toggles snapping.</summary>
    [RelayCommand]
    private void ToggleSnap() => SnapEnabled = !SnapEnabled;

    /// <summary>Toggles linked selection.</summary>
    [RelayCommand]
    private void ToggleLinkedSelection() => LinkedSelection = !LinkedSelection;

    /// <summary>V, B, N, Y, C: selects a tool.</summary>
    /// <param name="tool">The tool.</param>
    [RelayCommand]
    private void SelectTool(EditTool tool) => Tool = tool;

    private void OnArtifactsChanged(object? sender, string mediaId) => ArtifactsChanged?.Invoke(this, EventArgs.Empty);

    partial void OnPlayheadChanged(long value)
    {
        var clamped = Math.Clamp(value, 0, Math.Max(SequenceEnd, 0));
        if (clamped != value)
        {
            Playhead = clamped;
            return;
        }

        ViewState.EnsureVisible(value);
    }

    partial void OnInPointChanged(long? value)
    {
        if (!_syncingInOut)
        {
            var outFrame = OutPoint;
            Run(p => TimelineEdits.SetInOut(p, value, outFrame));
            SyncInOut();
        }
    }

    partial void OnOutPointChanged(long? value)
    {
        if (!_syncingInOut)
        {
            var inFrame = InPoint;
            Run(p => TimelineEdits.SetInOut(p, inFrame, value));
            SyncInOut();
        }
    }

    partial void OnZoomLevelChanged(double value)
    {
        if (_syncingScroll)
        {
            return;
        }

        var level = Math.Clamp(value, 0, 100);
        ViewState.SetZoom(TimelineViewState.MinPixelsPerFrame * Math.Pow(ZoomRatio, level / 100), PlayheadAnchorX());
    }

    partial void OnHorizontalOffsetChanged(double value)
    {
        if (!_syncingScroll)
        {
            ViewState.ScrollToFrame(value / ViewState.PixelsPerFrame);
        }
    }

    partial void OnVerticalOffsetChanged(double value)
    {
        if (!_syncingScroll)
        {
            ViewState.SetScrollY(value);
        }
    }

    private static double PixelsToLevel(double ppf) =>
        Math.Clamp(Math.Log(ppf / TimelineViewState.MinPixelsPerFrame) / Math.Log(ZoomRatio) * 100, 0, 100);

    private static List<string> UnlockedTrackIds(Project p) => [.. p.Sequence.Tracks.Where(t => !t.Locked).Select(t => t.Id)];

    // Topmost targeted video track first (visual order), then targeted audio tracks from A1 down.
    private static IEnumerable<Track> TrackPriority(Project p) =>
        TimelineQuery.TracksOfKind(p, TrackKind.Video).Reverse().Concat(TimelineQuery.TracksOfKind(p, TrackKind.Audio))
            .Where(t => t.Targeted && !t.Locked);

    private void OnSessionChanged(object? sender, ProjectChangedEventArgs e)
    {
        if (e.Kind == ChangeKind.Saved)
        {
            return;
        }

        if (e.Kind == ChangeKind.Replace)
        {
            ProjectGeneration++;
            SetSelection(new HashSet<string>(StringComparer.Ordinal), null, null);
        }
        else if (!Session.InGesture)
        {
            // During a gesture an overwrite may hide selected clips; they come back on cancel, so filter afterwards.
            FilterSelection();
        }

        SyncInOut();
        UpdateContent();
        OnPropertyChanged(nameof(SequenceEnd));
        if (!Session.InGesture)
        {
            var clamped = Math.Clamp(Playhead, 0, Math.Max(SequenceEnd, 0));
            if (clamped != Playhead)
            {
                Playhead = clamped;
            }
        }

        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void OnViewStateChanged(object? sender, EventArgs e)
    {
        SyncScroll();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncScroll()
    {
        _syncingScroll = true;
        try
        {
            var ppf = ViewState.PixelsPerFrame;
            HorizontalViewport = ViewState.ViewportWidth;
            HorizontalMaximum = ViewState.MaxScrollFrame * ppf;
            HorizontalOffset = ViewState.ScrollFrame * ppf;
            VerticalViewport = ViewState.ViewportHeight;
            VerticalMaximum = ViewState.MaxScrollY;
            VerticalOffset = ViewState.ScrollY;
            ZoomLevel = PixelsToLevel(ppf);
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    private void UpdateContent() => ViewState.SetContent(SequenceEnd, GetLayout().ContentHeight);

    private void SyncInOut()
    {
        _syncingInOut = true;
        try
        {
            InPoint = Session.Current.Sequence.InFrame;
            OutPoint = Session.Current.Sequence.OutFrame;
        }
        finally
        {
            _syncingInOut = false;
        }
    }

    private void FilterSelection()
    {
        var project = Session.Current;
        var clips = Selectable(_selected).ToHashSet(StringComparer.Ordinal);
        var transition = SelectedTransitionId is { } t && TimelineQuery.FindTransition(project, t) is { Track.Locked: false } ? t : null;
        var gap = SelectedGap is { } g && TimelineQuery.FindTrack(project, g.TrackId) is { Locked: false } track
            && TimelineQuery.GapAt(track, g.Start) is { } now && now.Start == g.Start && now.End == g.End ? g : null;
        if (clips.Count != _selected.Count || transition != SelectedTransitionId || gap != SelectedGap)
        {
            SetSelection(clips, transition, gap);
        }

        FilterVolumeKey();
    }

    private IEnumerable<string> Selectable(IEnumerable<string> clipIds)
    {
        var project = Session.Current;
        foreach (var id in clipIds.Distinct(StringComparer.Ordinal))
        {
            if (TimelineQuery.FindClip(project, id) is { } found && !found.Track.Locked)
            {
                yield return id;
            }
        }
    }

    private void SetSelection(HashSet<string> clips, string? transitionId, TimelineGap? gap)
    {
        var same = clips.SetEquals(_selected) && transitionId == SelectedTransitionId && gap == SelectedGap;
        if (same)
        {
            return;
        }

        _selected = clips.ToImmutableHashSet(StringComparer.Ordinal);
        SelectedTransitionId = transitionId;
        SelectedGap = gap;
        FilterVolumeKey();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private double PlayheadAnchorX()
    {
        var x = ViewState.FrameToX(Playhead);
        return x >= 0 && x <= ViewState.ViewportWidth ? x : ViewState.ViewportWidth / 2;
    }

    private void Nudge(long delta)
    {
        if (_selected.Count == 0)
        {
            return;
        }

        var ids = _selected.ToList();
        // Repeated nudges of the same selection merge into one undo step (TECH_BRIEF 3.4 coalesce key).
        var key = "nudge:" + string.Join(",", ids.Order(StringComparer.Ordinal));
        Run(p => TimelineEdits.Nudge(Ids, p, ids, delta), key);
    }

    private void RippleTrimToPlayhead(ClipEdge edge)
    {
        var frame = Playhead;
        Run(p => TimelineEdits.RippleTrimToFrame(p, [.. TrackPriority(p).Select(t => t.Id)], frame, edge));
    }

    private EditResult PlaceSequential(Project project, IReadOnlyList<string> assetIds, string trackId, long frame, EditMode mode)
    {
        var label = mode == EditMode.Insert ? "Insert" : "Overwrite";
        var current = project;
        var at = frame;
        var warnings = ImmutableArray.CreateBuilder<string>();
        var select = ImmutableArray.CreateBuilder<string>();
        foreach (var id in assetIds)
        {
            var placements = PlacementsFor(current, id, trackId);
            if (placements.IsEmpty)
            {
                warnings.Add("The media is not in the project or has no stream for this track.");
                continue;
            }

            var result = TimelineEdits.Place(Ids, current, placements, at, mode);
            warnings.AddRange(result.Warnings);
            if (!result.Changed(current))
            {
                continue;
            }

            current = result.Project;
            select.AddRange(result.Select);
            var ends = result.Select.Select(c => TimelineQuery.FindClip(current, c)?.Clip.End ?? at).ToList();
            at = ends.Count > 0 ? ends.Max() : at;
        }

        return ReferenceEquals(current, project)
            ? EditResult.Unchanged(project, label, [.. warnings])
            : new EditResult(current, label, warnings.ToImmutable(), select.ToImmutable());
    }

    private ImmutableArray<ClipPlacement> PlacementsFor(Project project, string assetId, string trackId)
    {
        var media = TimelineQuery.FindMedia(project, assetId);
        if (media is null || DropTargets(project, trackId) is not { } targets)
        {
            return ImmutableArray<ClipPlacement>.Empty;
        }

        var (video, audio) = targets;

        var stream = media.Info.Video is not null ? StreamKind.Video : StreamKind.Audio;
        var probe = new MediaClip("c_probe", 0, 1, true, null, assetId, stream, 0, 100, VideoProps.Default, AudioProps.Default);
        var max = TimelineQuery.MaxFramesFrom(probe, media, project.Sequence.Settings.Rate, 100);
        var duration = max >= TimelineQuery.Unbounded ? StillDurationFrames : max;
        return duration < 1
            ? ImmutableArray<ClipPlacement>.Empty
            : TimelineEdits.SourcePlacements(project, assetId, 0, duration, video, audio);
    }

    // The tracks of a source drop (DESIGN 5.5): the hovered track plus the track of the other kind with the same index,
    // else the first of that kind (V1 or A1). Null when the hovered track does not exist.
    private static (string? Video, string? Audio)? DropTargets(Project project, string trackId)
    {
        if (TimelineQuery.FindTrack(project, trackId) is not { } track)
        {
            return null;
        }

        var index = TimelineQuery.KindIndex(project, trackId);
        var videos = TimelineQuery.TracksOfKind(project, TrackKind.Video);
        var audios = TimelineQuery.TracksOfKind(project, TrackKind.Audio);
        return track.Kind == TrackKind.Video
            ? (track.Id, (index < audios.Length ? audios[index] : audios.FirstOrDefault())?.Id)
            : ((index < videos.Length ? videos[index] : videos.FirstOrDefault())?.Id, track.Id);
    }
}
