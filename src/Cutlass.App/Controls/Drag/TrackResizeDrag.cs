using Cutlass.App.Controls.Timeline;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>Drags the bottom edge of a track header to resize the track (DESIGN 5.1: 32 to 160 px). A view drag.</summary>
public sealed class TrackResizeDrag : IDragOperation
{
    private readonly string _trackId;
    private readonly TrackKind _kind;
    private TimelineViewState? _view;
    private double _startHeight;

    /// <summary>Creates the drag.</summary>
    /// <param name="trackId">The track.</param>
    /// <param name="kind">The track kind.</param>
    public TrackResizeDrag(string trackId, TrackKind kind)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        _trackId = trackId;
        _kind = kind;
    }

    /// <inheritdoc />
    public string? Tooltip => null;

    /// <inheritdoc />
    public bool TooltipIsLimit => false;

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
        _view = c.View;
        _startHeight = c.View.TrackHeight(_trackId, _kind);
    }

    /// <inheritdoc />
    public void Update(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        _view?.SetTrackHeight(_trackId, _startHeight + (c.Y - c.PressY));
    }

    /// <inheritdoc />
    public void Commit() => _view = null;

    /// <inheritdoc />
    public void Cancel()
    {
        _view?.SetTrackHeight(_trackId, _startHeight);
        _view = null;
    }
}
