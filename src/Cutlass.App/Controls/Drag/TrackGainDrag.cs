using Cutlass.App.Controls.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// Drags an audio track header's dB field (DESIGN 4.5) as one session gesture, so the whole drag is one undo step.
/// While playing, every update reaches the mixer live (TECH_BRIEF 8.2), without a restart.
/// </summary>
public sealed class TrackGainDrag : IDragOperation
{
    private readonly string _trackId;
    private DragContext? _context;
    private double _startDb;
    private double _gain;

    /// <summary>Creates the drag.</summary>
    /// <param name="trackId">The audio track.</param>
    public TrackGainDrag(string trackId)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        _trackId = trackId;
    }

    /// <inheritdoc />
    public string? Tooltip => _context is null ? null : TrackGain.Format(_gain);

    /// <inheritdoc />
    public bool TooltipIsLimit => _gain is TrackGain.MinDb or TrackGain.MaxDb;

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
        _context = c;
        _startDb = TimelineQuery.FindTrack(c.ViewModel.Project, _trackId)?.GainDb ?? 0;
        _gain = _startDb;
        c.ViewModel.Session.BeginGesture();
    }

    /// <inheritdoc />
    public void Update(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var gain = TrackGain.FromDrag(_startDb, c.X - c.PressX, c.Shift, c.Ctrl);
        _gain = gain;
        c.ViewModel.Session.UpdateGesture(p => TimelineEdits.SetTrack(p, _trackId, t => t with { GainDb = gain }, "Track Gain"));
    }

    /// <inheritdoc />
    public void Commit()
    {
        _context?.ViewModel.Session.CommitGesture("Track Gain");
        _context = null;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        _context?.ViewModel.Session.CancelGesture();
        _context = null;
    }
}
