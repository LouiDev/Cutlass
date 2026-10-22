using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Cutlass.App.Views.Inspector;

/// <summary>
/// Renders any inspector section: its groups and rows (DESIGN 4.4). Text boxes with the <c>commitOnEnter</c> class bind
/// on lost focus; here Enter pushes their text to the view model and Esc restores the model's text.
/// </summary>
public sealed partial class InspectorSectionView : UserControl
{
    /// <summary>Creates the view.</summary>
    public InspectorSectionView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private static void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox { } box || !box.Classes.Contains("commitOnEnter"))
        {
            return;
        }

        var binding = BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty);
        if (e.Key == Key.Enter)
        {
            binding?.UpdateSource();
            box.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            binding?.UpdateTarget();
            e.Handled = true;
        }
    }
}
