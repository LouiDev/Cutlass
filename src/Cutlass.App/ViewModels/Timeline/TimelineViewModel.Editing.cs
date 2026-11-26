using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Controls.Timeline;
using Cutlass.App.ViewModels.Viewer;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels.Timeline;

/// <summary>M6 editing commands: three-point edits, match frame, gap selection accessors and trims to the playhead.</summary>
public sealed partial class TimelineViewModel
{
    /// <summary>Raised by <see cref="MatchFrameCommand"/> (F) with the clip under the playhead; the shell opens Source mode.</summary>
    public event EventHandler<MatchFrameEventArgs>? MatchFrameRequested;

    /// <summary>The track of the selected gap, or null.</summary>
    public string? SelectedGapTrackId => SelectedGap?.TrackId;

    /// <summary>The first frame of the selected gap, or null.</summary>
    public long? SelectedGapStart => SelectedGap?.Start;

    /// <summary>The end (exclusive) of the selected gap, or null.</summary>
    public long? SelectedGapEnd => SelectedGap?.End;

    /// <summary>
    /// Three-point insert (<c>,</c>, DESIGN 5.5): places the source range on the topmost targeted video and audio
    /// tracks at the timeline In if set, else at the playhead, and moves the playhead to the end of the placed clips.
    /// </summary>
    /// <param name="assetId">The source media.</param>
    /// <param name="sourceInFlicks">The source In in flicks (0 with no Source In).</param>
    /// <param name="sourceOutFlicks">The source Out (exclusive) in flicks, or null for the rest of the media.</param>
    public void SourceInsert(string assetId, long sourceInFlicks, long? sourceOutFlicks) =>
        SourceEditAt(assetId, sourceInFlicks, sourceOutFlicks, EditMode.Insert);

    /// <summary>Three-point overwrite (<c>.</c>); see <see cref="SourceInsert"/>.</summary>
    /// <param name="assetId">The source media.</param>
    /// <param name="sourceInFlicks">The source In in flicks (0 with no Source In).</param>
    /// <param name="sourceOutFlicks">The source Out (exclusive) in flicks, or null for the rest of the media.</param>
    public void SourceOverwrite(string assetId, long sourceInFlicks, long? sourceOutFlicks) =>
        SourceEditAt(assetId, sourceInFlicks, sourceOutFlicks, EditMode.Overwrite);

    /// <summary>
    /// The clip a match frame would open: the media clip under the playhead on the topmost targeted video track, else
    /// any media clip under the playhead (video tracks top down, then audio tracks).
    /// </summary>
    /// <returns>The arguments, or null when no media clip sits under the playhead.</returns>
    public MatchFrameEventArgs? FindMatchFrame()
    {
        var project = Session.Current;
        var frame = Playhead;
        var videos = TimelineQuery.TracksOfKind(project, TrackKind.Video).Reverse().ToList();
        var ordered = videos.Where(t => t.Targeted).Concat(videos.Where(t => !t.Targeted))
            .Concat(TimelineQuery.TracksOfKind(project, TrackKind.Audio));
        foreach (var track in ordered)
        {
            if (TimelineQuery.ClipAt(track, frame) is MediaClip clip)
            {
                var media = TimelineQuery.FindMedia(project, clip.MediaId);
                var flicks = media is not null && media.Info.IsStill
                    ? 0
                    : TimeMath.SourceFlicksAt(clip, frame, project.Sequence.Settings.Rate);
                return new MatchFrameEventArgs(clip.MediaId, flicks);
            }
        }

        return null;
    }

    /// <summary>F: raises <see cref="MatchFrameRequested"/> for the clip under the playhead.</summary>
    [RelayCommand]
    private void MatchFrame()
    {
        if (FindMatchFrame() is { } args)
        {
            MatchFrameRequested?.Invoke(this, args);
        }
    }

    /// <summary>
    /// The clip a freeze frame at <paramref name="frame"/> would hold (effects batch 4): the selected video media clip
    /// under the frame, or, with nothing selected, the video media clip under the frame on the first targeted unlocked
    /// video track that has one. Stills and frozen clips are skipped; the edit refuses them anyway.
    /// </summary>
    /// <param name="frame">The timeline frame.</param>
    /// <returns>The clip id, or null.</returns>
    public string? FindFreezeTarget(long frame)
    {
        var project = Session.Current;
        bool Holds(Clip c) => c is MediaClip { Stream: StreamKind.Video, Freeze: false } m && m.Start <= frame && frame < m.End
            && TimelineQuery.FindMedia(project, m.MediaId) is not { Info.IsStill: true };
        if (_selected.Count > 0)
        {
            return _selected.Select(id => TimelineQuery.FindClip(project, id)?.Clip).FirstOrDefault(c => c is not null && Holds(c))?.Id;
        }

        foreach (var track in TimelineQuery.TracksOfKind(project, TrackKind.Video).Where(t => t.Targeted && !t.Locked))
        {
            if (TimelineQuery.ClipAt(track, frame) is { } clip && Holds(clip))
            {
                return clip.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Holds the frame of <paramref name="clipId"/> at <paramref name="frame"/> for <paramref name="durationFrames"/>
    /// (the default still duration when null), rippling the following clips (the Freeze Frame command, the Effects tab
    /// preset and the smoke step), and selects the frozen clip.
    /// </summary>
    /// <param name="clipId">The video clip.</param>
    /// <param name="frame">The frame to hold.</param>
    /// <param name="durationFrames">The hold length in frames, or null for the default still duration.</param>
    /// <returns>The result.</returns>
    public EditResult FreezeFrameAt(string clipId, long frame, long? durationFrames = null)
    {
        ArgumentNullException.ThrowIfNull(clipId);
        var duration = durationFrames ?? StillDurationFrames;
        var result = Run(p => TimelineEdits.FreezeFrame(Ids, p, clipId, frame, duration, ripple: true));
        if (!result.Select.IsDefaultOrEmpty)
        {
            Select(result.Select, false);
        }

        return result;
    }

    /// <summary>Shift+F: a freeze frame of the clip under the playhead (see <see cref="FindFreezeTarget"/>).</summary>
    [RelayCommand]
    private void FreezeFrame()
    {
        var frame = Playhead;
        if (FindFreezeTarget(frame) is { } clipId)
        {
            FreezeFrameAt(clipId, frame);
        }
        else
        {
            // The same feedback as the Effects tab preset, through the warning toast of refused edits.
            ReportWarnings(EditResult.Unchanged(Session.Current, "Freeze Frame", FreezeTargetMissing));
        }
    }

    /// <summary>The feedback when a freeze frame finds no video clip to hold (the command and the Effects preset).</summary>
    public const string FreezeTargetMissing = "Select a video clip under the playhead to freeze.";

    /// <summary>Q: ripple trims the start of the clips under the playhead on the targeted tracks to the playhead.</summary>
    [RelayCommand]
    private void TrimStartToPlayhead() => RippleTrimToPlayhead(ClipEdge.Start);

    /// <summary>W: ripple trims the end of the clips under the playhead on the targeted tracks to the playhead.</summary>
    [RelayCommand]
    private void TrimEndToPlayhead() => RippleTrimToPlayhead(ClipEdge.End);

    /// <summary>"Close gap": closes the selected gap (Delete and Shift+Delete do the same while a gap is selected).</summary>
    [RelayCommand]
    private void CloseGap()
    {
        if (SelectedGap is { } gap)
        {
            Run(p => TimelineEdits.CloseGap(p, gap.TrackId, gap.Start));
        }
    }

    partial void OnSelectedGapChanged(TimelineGap? value)
    {
        OnPropertyChanged(nameof(SelectedGapTrackId));
        OnPropertyChanged(nameof(SelectedGapStart));
        OnPropertyChanged(nameof(SelectedGapEnd));
    }

    private void SourceEditAt(string assetId, long? sourceInFlicks, long? sourceOutFlicks, EditMode mode)
    {
        ArgumentNullException.ThrowIfNull(assetId);
        // A Source In at 0 with no Out is "no Source In/Out", so a timeline In and Out can set the duration (DESIGN 5.5).
        var inFlicks = sourceInFlicks is { } i && (i > 0 || sourceOutFlicks is not null) ? i : (long?)null;
        var playhead = Playhead;
        var still = StillDurationFrames;
        var result = Run(p => TimelineEdits.SourceEdit(Ids, p, assetId, inFlicks, sourceOutFlicks, playhead, mode, still));
        if (result.Select.IsDefaultOrEmpty)
        {
            return;
        }

        Select(result.Select, false);
        // The playhead moves to the end of the placed clips so repeated presses append (DESIGN 5.5).
        var end = result.Select.Select(id => TimelineQuery.FindClip(Session.Current, id)?.Clip.End ?? 0).Max();
        Playhead = end;
    }
}
