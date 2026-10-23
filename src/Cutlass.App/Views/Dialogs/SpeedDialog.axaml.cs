using Avalonia.Controls;
using Cutlass.App.ViewModels.Dialogs;

namespace Cutlass.App.Views.Dialogs;

/// <summary>The speed dialog (Ctrl+R, DESIGN 5.5). Show it with <see cref="ShowAsync"/>.</summary>
public sealed partial class SpeedDialog : Window
{
    /// <summary>Creates the dialog (for the XAML loader and the designer).</summary>
    public SpeedDialog()
    {
        InitializeComponent();
    }

    /// <summary>Creates the dialog for a view model.</summary>
    /// <param name="viewModel">The dialog model.</param>
    public SpeedDialog(SpeedDialogViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.CloseRequested += (_, applied) => Close(applied);
    }

    /// <summary>
    /// Shows the dialog modally over <paramref name="owner"/>. Returns true when the user applied it; the caller then
    /// runs <see cref="SpeedDialogViewModel.CreateEdit"/> (the inspector's <c>OpenSpeedDialogCommand</c> does that).
    /// </summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="viewModel">The dialog model.</param>
    /// <returns>True when applied.</returns>
    public static async Task<bool> ShowAsync(Window owner, SpeedDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return await new SpeedDialog(viewModel).ShowDialog<bool>(owner);
    }
}
