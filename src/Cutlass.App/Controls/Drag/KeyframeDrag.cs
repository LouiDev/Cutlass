using System.Collections.Immutable;
using Cutlass.App.Controls.Timeline;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// Drags a volume keyframe of a selected audio clip (DESIGN 5.5): in time between its neighbours (whole frames inside
/// the clip) and in dB on the -60 to +12 dB scale. Shift constrains the drag to the axis that moved more. The key is
/// selected at the press, so Delete removes it afterwards.
/// </summary>
public sealed class KeyframeDrag : IDragOperation
{
    private readonly string _clipId;
    private readonly int _index;
    private TimelineDragState? _state;
    private VolumeKey _key = new(0, 0);
    private double _gain;
    private long _minFrame;
    private long _maxFrame;
    private double _clipHeight = 1;
    private long _frame;

    /// <summary>Creates the drag.</summary>
    /// <param name="clipId">The audio clip.</param>
    /// <param name="keyIndex">The keyframe index.</param>
    public KeyframeDrag(string clipId, int keyIndex)
    {
        ArgumentNullException.ThrowIfNull(clipId);
        _clipId = clipId;
        _index = keyIndex;
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

    /// <inheritdoc />
    public void Begin(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var vm = c.ViewModel;
        var origin = vm.Session.Current;
        if (TimelineQuery.FindClip(origin, _clipId) is not { } found || found.Track.Locked
            || found.Clip is not MediaClip { Stream: StreamKind.Audio } clip
            || clip.Audio.Keys.IsDefaultOrEmpty || _index < 0 || _index >= clip.Audio.Keys.Length)
        {
            return;
        }

        var keys = clip.Audio.Keys;
        _key = keys[_index];
        _frame = _key.Frame;
        _gain = clip.Audio.GainDb;
        _minFrame = _index > 0 ? keys[_index - 1].Frame + 1 : 0;
        _maxFrame = _index + 1 < keys.Length ? keys[_index + 1].Frame - 1 : clip.Duration - 1;
        _clipHeight = Math.Max(1, (vm.GetLayout().Row(found.Track.Id)?.Height ?? 48) - 2);
        _state = new TimelineDragState(vm, origin);
        vm.SelectVolumeKey(_clipId, _key.Frame);
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

        var dxPixels = c.X - c.PressX;
        var dyPixels = c.Y - c.PressY;
        var dFrames = c.FrameDelta;
        var dDb = VolumeScale.DbPerPixels(dyPixels, _clipHeight);
        if (c.Shift)
        {
            // Shift keeps one axis: whichever moved further.
            if (Math.Abs(dxPixels) >= Math.Abs(dyPixels))
            {
                dDb = 0;
            }
            else
            {
                dFrames = 0;
            }
        }

        var wantedFrame = _key.Frame + dFrames;
        var frame = Math.Clamp(wantedFrame, _minFrame, Math.Max(_minFrame, _maxFrame));
        var wantedDb = _gain + _key.Db + dDb;
        var level = Math.Clamp(wantedDb, VolumeScale.MinDb, VolumeScale.MaxDb);
        var db = Math.Round(level - _gain, 2);
        TooltipIsLimit = frame != wantedFrame || Math.Abs(level - wantedDb) > 1e-9;
        _frame = frame;
        var (clipId, index) = (_clipId, _index);
        var result = c.ViewModel.Session.UpdateGesture(p => TimelineEdits.UpdateClips(
            p, [clipId], clip => TimelineViewModel.WithKeys(clip, k => index < k.Length ? k.SetItem(index, new VolumeKey(frame, db)) : k), "Move Keyframe"));
        _state.Last = result;
        Tooltip = VolumeScale.Format(_gain + db) + "  " + Core.Time.Timecode.Format(frame, result.Project.Sequence.Settings.Rate);
    }

    /// <inheritdoc />
    public void Commit()
    {
        if (_state is null)
        {
            return;
        }

        var vm = _state.ViewModel;
        vm.Session.CommitGesture("Move Keyframe");
        if (_state.Last is { } last)
        {
            vm.ReportWarnings(last);
        }

        vm.SelectVolumeKey(_clipId, _frame);
        _state = null;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        if (_state is null)
        {
            return;
        }

        _state.ViewModel.Session.CancelGesture();
        _state.ViewModel.SelectVolumeKey(_clipId, _key.Frame);
        _state = null;
    }
}
