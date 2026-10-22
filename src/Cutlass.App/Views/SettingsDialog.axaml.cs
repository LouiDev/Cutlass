using Avalonia.Controls;
using Cutlass.App.ViewModels;

namespace Cutlass.App.Views;

/// <summary>The Settings dialog (Ctrl+, DESIGN 10). Show it with <see cref="ShowAsync"/>.</summary>
public sealed partial class SettingsDialog : Window
{
    /// <summary>Creates the dialog (for the XAML loader and the designer).</summary>
    public SettingsDialog()
    {
        InitializeComponent();
    }

    /// <summary>Creates the dialog for a view model.</summary>
    /// <param name="viewModel">The dialog model.</param>
    public SettingsDialog(SettingsViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.CloseRequested += (_, applied) => Close(applied);
    }

    /// <summary>Shows the dialog modally over <paramref name="owner"/>.</summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="viewModel">The dialog model.</param>
    /// <returns>True when the user pressed OK.</returns>
    public static async Task<bool> ShowAsync(Window owner, SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return await new SettingsDialog(viewModel).ShowDialog<bool>(owner);
    }
}
