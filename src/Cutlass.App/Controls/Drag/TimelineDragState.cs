using Cutlass.App.ViewModels;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>The state an edit drag keeps between Begin and Commit: the view model, the gesture start and the last result.</summary>
public sealed class TimelineDragState
{
    /// <summary>Creates the state.</summary>
    /// <param name="viewModel">The timeline view model.</param>
    /// <param name="origin">The project at the gesture start.</param>
    public TimelineDragState(TimelineViewModel viewModel, Project origin)
    {
        ViewModel = viewModel;
        Origin = origin;
    }

    /// <summary>The timeline view model.</summary>
    public TimelineViewModel ViewModel { get; }

    /// <summary>The project at the gesture start.</summary>
    public Project Origin { get; }

    /// <summary>The result of the last update, or null.</summary>
    public EditResult? Last { get; set; }
}
