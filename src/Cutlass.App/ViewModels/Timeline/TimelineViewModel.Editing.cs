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
