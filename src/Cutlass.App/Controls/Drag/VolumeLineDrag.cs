using System.Collections.Immutable;
using Cutlass.App.Controls.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// Drags the volume line of a selected audio clip vertically (DESIGN 5.5). Without keyframes it changes the clip gain;
/// between keyframes it moves both keys of the pressed segment by the same amount (before the first or after the last
/// key, that key alone). Shift gives fine steps (a tenth of the scale). Levels stay within -60 to +12 dB.
/// </summary>
public sealed class VolumeLineDrag : IDragOperation
{
    /// <summary>The share of the normal step that Shift gives.</summary>
    public const double FineFactor = 0.1;

    private readonly string _clipId;
    private readonly double _pressFrame;
    private TimelineDragState? _state;
    private MediaClip? _clip;
    private IReadOnlyList<int> _segment = [];
    private double _clipHeight = 1;

    /// <summary>Creates the drag.</summary>
    /// <param name="clipId">The audio clip.</param>
    /// <param name="pressFrame">The pressed frame relative to the clip start (fractional).</param>
    public VolumeLineDrag(string clipId, double pressFrame)
    {
        ArgumentNullException.ThrowIfNull(clipId);
        _clipId = clipId;
        _pressFrame = pressFrame;
    }

    /// <inheritdoc />
    public string? Tooltip { get; private set; }

    /// <inheritdoc />
    public bool TooltipIsLimit { get; private set; }

    /// <inheritdoc />
    public ViewerHint? Hint => null;

    /// <inheritdoc />
    public long? SnapFrame => null;

    /// <inheritdoc />
    public Project? OriginProject => null;

    /// <inheritdoc />
    public IReadOnlyCollection<string> OutlineClipIds => [];

    /// <summary>The keyframe indices the drag moves (empty when it changes the clip gain).</summary>
    public IReadOnlyList<int> SegmentKeys => _segment;

    /// <inheritdoc />
    public void Begin(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var vm = c.ViewModel;
        var origin = vm.Session.Current;
        if (TimelineQuery.FindClip(origin, _clipId) is not { } found || found.Track.Locked
            || found.Clip is not MediaClip { Stream: StreamKind.Audio } clip)
        {
            return;
        }

        _clip = clip;
        _segment = VolumeScale.SegmentKeys(clip, _pressFrame);
        _clipHeight = Math.Max(1, (vm.GetLayout().Row(found.Track.Id)?.Height ?? 48) - 2);
        _state = new TimelineDragState(vm, origin);
        vm.Session.BeginGesture();
    }

    /// <inheritdoc />
    public void Update(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (_state is null || _clip is not { } clip)
        {
            return;
        }

        var dDb = VolumeScale.DbPerPixels(c.Y - c.PressY, _clipHeight) * (c.Shift ? FineFactor : 1);
        var gain = clip.Audio.GainDb;
        Clip Apply(Clip current)
        {
            if (current is not MediaClip m)
            {
                return current;
            }

            if (_segment.Count == 0)
            {
                return m with { Audio = m.Audio with { GainDb = Math.Round(Math.Clamp(gain + dDb, VolumeScale.MinDb, VolumeScale.MaxDb), 2) } };
            }

            var keys = m.Audio.Keys;
            foreach (var i in _segment)
            {
                if (i < keys.Length)
                {
                    var original = clip.Audio.Keys[i];
                    var level = Math.Clamp(gain + original.Db + dDb, VolumeScale.MinDb, VolumeScale.MaxDb);
                    keys = keys.SetItem(i, original with { Db = Math.Round(level - gain, 2) });
                }
            }

            return m with { Audio = m.Audio with { Keys = keys } };
        }

        var clipId = _clipId;
        var label = _segment.Count == 0 ? "Clip Gain" : "Volume";
        var result = c.ViewModel.Session.UpdateGesture(p => TimelineEdits.UpdateClips(p, [clipId], Apply, label));
        _state.Last = result;
        var now = TimelineQuery.FindClip(result.Project, _clipId)?.Clip as MediaClip ?? clip;
        var level = VolumeScale.LevelAt(now, _pressFrame);
        var wanted = VolumeScale.LevelAt(clip, _pressFrame) + dDb;
        TooltipIsLimit = Math.Abs(level - wanted) > 0.01;
        Tooltip = VolumeScale.Format(level);
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (_state is null)
        {
            return;
        }

        _state.ViewModel.Session.CommitGesture(_segment.Count == 0 ? "Clip Gain" : "Volume");
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
