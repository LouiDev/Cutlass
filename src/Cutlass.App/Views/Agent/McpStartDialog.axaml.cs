using Avalonia.Controls;
using Cutlass.App.ViewModels.Agent;

namespace Cutlass.App.Views.Agent;

/// <summary>
/// The first-start dialog of Agent > Start MCP server (AGENT_INTEGRATION 6.6 and 9.4). Show it with
/// <see cref="ShowAsync"/>.
/// </summary>
public sealed partial class McpStartDialog : Window
{
    /// <summary>Creates the dialog (for the XAML loader and the designer).</summary>
    public McpStartDialog()
    {
        InitializeComponent();
    }

    /// <summary>Creates the dialog for a view model.</summary>
    /// <param name="viewModel">The dialog model.</param>
    public McpStartDialog(McpStartDialogViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.CloseRequested += (_, ok) => Close(ok);
    }

    /// <summary>Shows the dialog modally over <paramref name="owner"/>.</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="viewModel">The dialog model.</param>
    /// <returns>
    /// True for Start, or once a registration ran (Close or the close box); false for Cancel or the close box before.
    /// </returns>
    public static async Task<bool> ShowAsync(Window owner, McpStartDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return await new McpStartDialog(viewModel).ShowDialog<bool>(owner) || viewModel.IsDone;
    }
}
