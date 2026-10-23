using System.Collections.Immutable;
using Cutlass.App.Controls.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// The fade corner handles (DESIGN 5.1, 5.5): the fade-in handle drags right to lengthen the fade-in, the fade-out
/// handle drags left to lengthen the fade-out. Video clips and titles fade their opacity to transparent, audio clips
/// fade linearly. Lengths are whole frames, the fade's inner end snaps, and <c>FadeIn + FadeOut</c> stays within the
/// clip. The tooltip shows the fade length.
/// </summary>
public sealed class FadeHandleDrag : IDragOperation
{
    private readonly string _clipId;
    private readonly bool _fadeIn;
    private TimelineDragState? _state;
    private long _start;
    private long _other;
    private long _duration;
    private long _edgeFrame;
    private ImmutableArray<long> _targets = ImmutableArray<long>.Empty;

    /// <summary>Creates the drag.</summary>
    /// <param name="clipId">The clip.</param>
    /// <param name="fadeIn">True for the fade-in handle, false for the fade-out handle.</param>
    public FadeHandleDrag(string clipId, bool fadeIn)
    {
        ArgumentNullException.ThrowIfNull(clipId);
        _clipId = clipId;
        _fadeIn = fadeIn;
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
    public Project? OriginProject => null;

    /// <inheritdoc />
    public IReadOnlyCollection<string> OutlineClipIds => [];

    /// <summary>True for the fade-in handle.</summary>
    public bool IsFadeIn => _fadeIn;

    /// <summary>Returns a clip with a new fade-in or fade-out length (titles, video and audio clips).</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="fadeIn">True for the fade-in.</param>
    /// <param name="frames">The new length in frames.</param>
    /// <returns>The changed clip.</returns>
    public static Clip WithFade(Clip clip, bool fadeIn, long frames) => clip switch
    {
        TitleClip t => fadeIn ? t with { FadeInFrames = frames } : t with { FadeOutFrames = frames },
        MediaClip { Stream: StreamKind.Video } m => m with
        {
            Video = fadeIn ? m.Video with { FadeInFrames = frames } : m.Video with { FadeOutFrames = frames },
        },
        MediaClip m => m with
        {
            Audio = fadeIn ? m.Audio with { FadeInFrames = frames } : m.Audio with { FadeOutFrames = frames },
        },
        _ => clip,
    };

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

        var (fadeIn, fadeOut) = HitTester.Fades(found.Clip);
        _start = _fadeIn ? fadeIn : fadeOut;
        _other = _fadeIn ? fadeOut : fadeIn;
        _duration = found.Clip.Duration;
        _edgeFrame = _fadeIn ? found.Clip.Start + fadeIn : found.Clip.End - fadeOut;
        _targets = SnapEngine.Collect(origin, vm.Playhead, [_clipId]);
        _state = new TimelineDragState(vm, origin);
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
            var snap = SnapEngine.Resolve([_edgeFrame], delta, _targets, c.View.PixelsPerFrame);
            delta = snap.Delta;
            SnapFrame = snap.SnapFrame;
        }

        var wanted = _fadeIn ? _start + delta : _start - delta;
        var length = Math.Clamp(wanted, 0, Math.Max(0, _duration - _other));
        TooltipIsLimit = length != wanted;
        if (TooltipIsLimit)
        {
            SnapFrame = null;
        }

        var (clipId, fadeIn) = (_clipId, _fadeIn);
        var result = c.ViewModel.Session.UpdateGesture(p => TimelineEdits.UpdateClips(p, [clipId], clip => WithFade(clip, fadeIn, length), Label));
        _state.Last = result;
        Tooltip = (_fadeIn ? "Fade in " : "Fade out ") + Core.Time.Timecode.Format(length, result.Project.Sequence.Settings.Rate);
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (_state is null)
        {
            return;
        }

        _state.ViewModel.Session.CommitGesture(Label);
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

    private string Label => _fadeIn ? "Fade In" : "Fade Out";
}
