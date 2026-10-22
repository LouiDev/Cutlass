using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Cutlass.App.ViewModels;

namespace Cutlass.App.Views;

/// <summary>The welcome overlay shown while no project is open (DESIGN 4.1). It accepts OS file drops.</summary>
public sealed partial class WelcomeOverlay : UserControl
{
    /// <summary>Creates the overlay.</summary>
    public WelcomeOverlay()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not WelcomeViewModel vm || e.DataTransfer.TryGetFiles() is not { } items)
        {
            return;
        }

        e.Handled = true;
        var paths = items.Select(i => i.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToList();
        await vm.DropMediaAsync(paths);
    }
}
