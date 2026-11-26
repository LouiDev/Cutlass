using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cutlass.App.ViewModels.Agent;
using Cutlass.App.ViewModels.Dialogs;
using Cutlass.App.Views.Agent;
using Cutlass.App.Views.Dialogs;

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
    /// Opens a top-level menu of the window (its header without the access key underscore, for example <c>Agent</c>)
    /// and, optionally, a chain of submenus inside it (for example <c>Copy briefing for</c>), renders the window with
    /// every open menu into one PNG file and closes the menus (the smoke command <c>show-menu</c>). A windowed popup is
    /// drawn over the window at its screen position; an overlay popup is part of the window already.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="header">The menu header, for example <c>Agent</c>.</param>
    /// <param name="path">The PNG file.</param>
    /// <param name="submenus">The headers of the submenus to open in turn (access key underscores ignored), or none.</param>
    /// <returns>A task that completes when the file is written and the menu closed.</returns>
    public static async Task CaptureMenuAsync(Window window, string header, string path, IReadOnlyList<string>? submenus = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        var item = window.GetVisualDescendants().OfType<MenuItem>()
            .FirstOrDefault(m => m.Parent is Menu && HeaderIs(m, header))
            ?? throw new ArgumentException("The window has no top-level menu " + header + ".", nameof(header));
        var opened = new List<MenuItem> { item };
        item.IsSubMenuOpen = true;
        try
        {
            await SettleAsync();
            foreach (var name in submenus ?? [])
            {
                // The items of an open submenu live in its popup, which is its own visual tree.
                var parent = opened[^1];
                var sub = PopupOf(parent)?.Child?.GetVisualDescendants().OfType<MenuItem>().FirstOrDefault(m => HeaderIs(m, name))
                    ?? parent.Items.OfType<MenuItem>().FirstOrDefault(m => HeaderIs(m, name))
                    ?? throw new ArgumentException("The menu " + header + " has no submenu " + name + ".", nameof(submenus));
                sub.IsSubMenuOpen = true;
                opened.Add(sub);
                await SettleAsync();
            }

            window.UpdateLayout();
            var scaling = window.RenderScaling;
            var size = new PixelSize(
                Math.Max(1, (int)Math.Ceiling(window.Bounds.Width * scaling)),
                Math.Max(1, (int)Math.Ceiling(window.Bounds.Height * scaling)));
            var dpi = new Vector(96 * scaling, 96 * scaling);
            using var composed = new RenderTargetBitmap(size, dpi);
            using (var windowImage = new RenderTargetBitmap(size, dpi))
            {
                windowImage.Render(window);
                using var context = composed.CreateDrawingContext();
                context.DrawImage(windowImage, new Rect(window.Bounds.Size));
                foreach (var open in opened)
                {
                    if (PopupOf(open)?.Child is Visual child && TopLevel.GetTopLevel(child) is { } host && !ReferenceEquals(host, window))
                    {
                        host.UpdateLayout();
                        var hostSize = new PixelSize(
                            Math.Max(1, (int)Math.Ceiling(host.Bounds.Width * scaling)),
                            Math.Max(1, (int)Math.Ceiling(host.Bounds.Height * scaling)));
                        using var menuImage = new RenderTargetBitmap(hostSize, dpi);
                        menuImage.Render(host);
                        var offset = host.PointToScreen(default) - window.PointToScreen(default);
                        context.DrawImage(menuImage, new Rect(new Point(offset.X / scaling, offset.Y / scaling), host.Bounds.Size));
                    }
                }
            }

            using var stream = new MemoryStream();
            composed.Save(stream);
            var png = stream.ToArray();
            await Task.Run(() =>
            {
                if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
                {
                    Directory.CreateDirectory(folder);
                }

                File.WriteAllBytes(path, png);
            });
        }
        finally
        {
            for (var i = opened.Count - 1; i >= 0; i--)
            {
                opened[i].IsSubMenuOpen = false;
            }

            item.Close();
        }

        static bool HeaderIs(MenuItem menuItem, string text) =>
            menuItem.Header is string value
            && string.Equals(value.Replace("_", string.Empty, StringComparison.Ordinal), text.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);

        static Popup? PopupOf(MenuItem menuItem) => menuItem.GetVisualDescendants().OfType<Popup>().FirstOrDefault();

        static async Task SettleAsync()
        {
            for (var i = 0; i < 3; i++)
            {
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            }

            await Task.Delay(300);
        }
    }

    /// <summary>
    /// Opens the dialog window of a dialog view model over <paramref name="owner"/> (not modal), waits for its first
    /// layout, renders it into a PNG file and closes it. The smoke command <c>show-dialog</c> uses it.
    /// </summary>
    /// <param name="owner">The owner window.</param>
    /// <param name="viewModel">A Settings, Relink, Export, Speed, Sequence Settings, MCP start or Connect clients dialog view model.</param>
    /// <param name="path">The PNG file.</param>
    /// <param name="height">A window height for the capture (a long page shows its lower part), or null.</param>
    /// <returns>A task that completes when the dialog is captured and closed.</returns>
    public static async Task CaptureDialogAsync(Window owner, object viewModel, string path, double? height = null)
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
            McpStartDialogViewModel mcp => new McpStartDialog(mcp),
            ConnectClientsViewModel clients => new ConnectClientsDialog(clients),
            _ => throw new ArgumentException("No dialog window for " + viewModel.GetType().Name + ".", nameof(viewModel)),
        };

        if (height is { } tall)
        {
            window.SizeToContent = SizeToContent.Manual;
            window.Height = tall;
        }

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
