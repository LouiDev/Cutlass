using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Cutlass.App.Views;

/// <summary>
/// A small modal message box with a row of buttons (confirmations, "Save changes?", plain messages). It has no
/// bindings, so it is built in code. The result is the index of the pressed button; closing the window or Esc gives
/// the cancel index.
/// </summary>
public sealed class MessageDialog : Window
{
    private readonly int _cancelIndex;

    /// <summary>Creates the dialog.</summary>
    /// <param name="title">The window title.</param>
    /// <param name="message">The message.</param>
    /// <param name="buttons">The button labels, left to right; the first one is the primary (Accent) button.</param>
    /// <param name="cancelIndex">The result when the window is closed without a button.</param>
    public MessageDialog(string title, string message, IReadOnlyList<string> buttons, int cancelIndex)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        _cancelIndex = cancelIndex;
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (i == 0)
            {
                button.Classes.Add("accent");
                button.IsDefault = true;
            }

            if (i == cancelIndex)
            {
                button.IsCancel = true;
            }

            button.Click += (_, _) => Close(index);
            row.Children.Add(button);
        }

        Content = new Border
        {
            Padding = new Thickness(20, 18),
            Child = new StackPanel
            {
                Spacing = 18,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 },
                    row,
                },
            },
        };
    }

    /// <summary>Shows the dialog modally over an owner.</summary>
    /// <param name="owner">The owner window.</param>
    /// <returns>The index of the pressed button, or the cancel index.</returns>
    public async Task<int> ShowAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var result = await ShowDialog<int?>(owner);
        return result ?? _cancelIndex;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(_cancelIndex);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
