using Avalonia.Controls;
using Cutlass.App.ViewModels.Dialogs;

namespace Cutlass.App.Views.Dialogs;

/// <summary>The sequence settings dialog ("Change..." in the inspector, DESIGN 4.4). Show it with <see cref="ShowAsync"/>.</summary>
public sealed partial class SequenceSettingsDialog : Window
{
    /// <summary>Creates the dialog (for the XAML loader and the designer).</summary>
    public SequenceSettingsDialog()
    {
        InitializeComponent();
    }

    /// <summary>Creates the dialog for a view model.</summary>
    /// <param name="viewModel">The dialog model.</param>
    public SequenceSettingsDialog(SequenceSettingsDialogViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        viewModel.CloseRequested += (_, applied) => Close(applied);
    }

    /// <summary>
    /// Shows the dialog modally over <paramref name="owner"/>. Returns true when the user applied it; the caller then
    /// runs <see cref="SequenceSettingsDialogViewModel.CreateEdit"/> (the inspector's <c>OpenSequenceSettingsCommand</c>
    /// does that).
    /// </summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="viewModel">The dialog model.</param>
    /// <returns>True when applied.</returns>
    public static async Task<bool> ShowAsync(Window owner, SequenceSettingsDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return await new SequenceSettingsDialog(viewModel).ShowDialog<bool>(owner);
    }
}
