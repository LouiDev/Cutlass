using Avalonia.Controls;
using Cutlass.App.ViewModels;

namespace Cutlass.App.Views;

/// <summary>The Relink dialog (DESIGN 4.2 and 11). Show it with <see cref="ShowAsync"/>.</summary>
public sealed partial class RelinkDialog : Window
{
    /// <summary>Creates the dialog (for the XAML loader and the designer).</summary>
    public RelinkDialog()
    {
        InitializeComponent();
    }

    /// <summary>Creates the dialog for a view model.</summary>
    /// <param name="viewModel">The dialog model.</param>
    public RelinkDialog(RelinkDialogViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
    }

    /// <summary>Shows the dialog modally over <paramref name="owner"/>.</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="viewModel">The dialog model.</param>
    /// <returns>A task that completes when the dialog closes.</returns>
    public static Task ShowAsync(Window owner, RelinkDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return new RelinkDialog(viewModel).ShowDialog(owner);
    }
}
