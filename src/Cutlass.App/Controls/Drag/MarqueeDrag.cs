using Cutlass.App.ViewModels;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// Marquee selection (DESIGN 5.3): a drag on an empty lane selects every clip on an unlocked track that the rectangle
/// touches, plus link partners with linked selection on (Alt selects one side). Shift or Ctrl at the press adds to the
/// existing selection. A view drag: Esc restores the selection from before the drag.
/// </summary>
public sealed class MarqueeDrag : IDragOperation
{
    private TimelineViewModel? _viewModel;
    private List<string> _before = [];
    private List<string> _base = [];

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

    /// <summary>The frame of the press corner.</summary>
    public double StartFrame { get; private set; }

    /// <summary>The content y of the press corner.</summary>
    public double StartContentY { get; private set; }

    /// <summary>The frame of the pointer corner.</summary>
    public double EndFrame { get; private set; }

    /// <summary>The content y of the pointer corner.</summary>
    public double EndContentY { get; private set; }

    /// <summary>The clips inside a marquee.</summary>
    /// <param name="project">The project.</param>
    /// <param name="layout">The track layout.</param>
    /// <param name="frame0">One corner's frame.</param>
    /// <param name="y0">One corner's content y.</param>
    /// <param name="frame1">The other corner's frame.</param>
    /// <param name="y1">The other corner's content y.</param>
    /// <returns>The clip ids on unlocked tracks the rectangle touches.</returns>
    public static List<string> ClipsInside(Project project, TrackLayout layout, double frame0, double y0, double frame1, double y1)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(layout);
        var (f0, f1) = (Math.Min(frame0, frame1), Math.Max(frame0, frame1));
        var (top, bottom) = (Math.Min(y0, y1), Math.Max(y0, y1));
        var ids = new List<string>();
        foreach (var row in layout.Rows)
        {
            if (row.Bottom <= top || row.Top > bottom || TimelineQuery.FindTrack(project, row.TrackId) is not { Locked: false } track)
            {
                continue;
            }

            ids.AddRange(track.Clips.Where(c => c.Start < f1 && c.End > f0).Select(c => c.Id));
        }

        return ids;
    }

    /// <inheritdoc />
    public void Begin(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        _viewModel = c.ViewModel;
        _before = [.. c.ViewModel.SelectedClipIds];
        _base = c.PressShift || c.PressCtrl ? [.. _before] : [];
        StartFrame = c.PressFrame;
        StartContentY = c.PressContentY;
        EndFrame = c.PressFrame;
        EndContentY = c.PressContentY;
    }

    /// <inheritdoc />
    public void Update(DragContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (_viewModel is null)
        {
            return;
        }

        EndFrame = c.Frame;
        EndContentY = c.ContentY;
        var inside = ClipsInside(_viewModel.Session.Current, _viewModel.GetLayout(), StartFrame, StartContentY, EndFrame, EndContentY);
        _viewModel.Select(_base.Concat(_viewModel.ExpandLinked(inside, c.Alt)), false);
    }

    /// <inheritdoc />
    public void Commit() => _viewModel = null;

    /// <inheritdoc />
    public void Cancel()
    {
        _viewModel?.Select(_before, false);
        _viewModel = null;
    }
}
