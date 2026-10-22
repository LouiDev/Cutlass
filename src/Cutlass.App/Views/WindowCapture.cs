using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cutlass.App.ViewModels;

namespace Cutlass.App.Views;

/// <summary>
/// Renders windows into PNG files for smoke screenshots (TECH_BRIEF 10.4). It draws the visual tree itself, so it
/// works while another window covers the one captured. <see cref="CaptureDialogAsync"/> opens a real dialog window
/// for a dialog view model, captures it and closes it again (the smoke dialog service never shows dialogs).
/// </summary>
public static class WindowCapture
{
    /// <summary>
    /// Lays the window out and renders it into a PNG file. Encoding runs on the UI thread; only the file write leaves
    /// it.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="path">The PNG file.</param>
    /// <returns>A task that completes when the file is written.</returns>
    public static async Task SaveAsync(Window window, string path)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // RenderTargetBitmap draws the current visual tree without a layout pass, so a step that just changed the
        // Inspector section would be captured half laid out (a clipped header, empty rows). Lay out first.
        window.UpdateLayout();
        var scaling = window.RenderScaling;
        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Width * scaling)),
            Math.Max(1, (int)Math.Ceiling(window.Bounds.Height * scaling)));
        byte[] png;
        using (var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling)))
        {
            bitmap.Render(window);
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            png = stream.ToArray();
        }

        await Task.Run(() =>
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllBytes(path, png);
        });
    }

    /// <summary>
    /// Opens the dialog window of a dialog view model over <paramref name="owner"/> (not modal), waits for its first
    /// layout, renders it into a PNG file and closes it. The smoke command <c>show-dialog</c> uses it.
    /// </summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="viewModel">A Settings, Relink, Export, Speed or Sequence Settings dialog view model.</param>
    /// <param name="path">The PNG file.</param>
    /// <returns>A task that completes when the dialog is captured and closed.</returns>
    public static async Task CaptureDialogAsync(Window owner, object viewModel, string path)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(viewModel);
        Window window = viewModel switch
        {
            SettingsViewModel settings => new SettingsDialog(settings),
            RelinkDialogViewModel relink => new RelinkDialog(relink),
            ExportDialogViewModel export => new ExportDialog { DataContext = export },
            SpeedDialogViewModel speed => new SpeedDialog(speed),
            SequenceSettingsDialogViewModel sequence => new SequenceSettingsDialog(sequence),
            _ => throw new ArgumentException("No dialog window for " + viewModel.GetType().Name + ".", nameof(viewModel)),
        };

        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Opened += (_, _) => opened.TrySetResult();
        window.Show(owner);
        try
        {
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Bindings, item templates and text layout settle over a few dispatcher passes after Opened.
            for (var i = 0; i < 3; i++)
            {
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            }

            await Task.Delay(150);
            await SaveAsync(window, path);
        }
        finally
        {
            window.Close();
        }
    }
}
