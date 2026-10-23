using Avalonia.Controls;
using Cutlass.App.ViewModels.Agent;

namespace Cutlass.App.Views.Agent;

/// <summary>Agent > Connect an agent client... (AGENT_INTEGRATION 9.4). Show it with <see cref="ShowAsync"/>.</summary>
public sealed partial class ConnectClientsDialog : Window
{
    private readonly ConnectClientsViewModel? _viewModel;

    /// <summary>Creates the dialog (for the XAML loader and the designer).</summary>
    public ConnectClientsDialog()
    {
        InitializeComponent();
    }

    /// <summary>Creates the dialog for the client list.</summary>
    /// <param name="viewModel">The client list.</param>
    public ConnectClientsDialog(ConnectClientsViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;
    }

    /// <summary>Shows the dialog modally over <paramref name="owner"/>.</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="viewModel">The client list.</param>
    /// <returns>A task that completes when the dialog closed.</returns>
    public static Task ShowAsync(Window owner, ConnectClientsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return new ConnectClientsDialog(viewModel).ShowDialog(owner);
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _viewModel))
        {
            Close();
        }
    }
}
