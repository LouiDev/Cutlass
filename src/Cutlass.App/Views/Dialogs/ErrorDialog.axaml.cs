using Avalonia.Controls;
using Avalonia.Interactivity;
using Cutlass.App.ViewModels.Dialogs;

namespace Cutlass.App.Views.Dialogs;

/// <summary>The error dialog (DESIGN 9).</summary>
public sealed partial class ErrorDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public ErrorDialog()
    {
        InitializeComponent();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ErrorDialogViewModel vm && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(vm.DetailsText);
        }
    }
}
