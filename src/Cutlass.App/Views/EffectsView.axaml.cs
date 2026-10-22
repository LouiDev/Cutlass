using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Cutlass.App.ViewModels;

namespace Cutlass.App.Views;

/// <summary>
/// The Effects tab (DESIGN 4.2). The view owns the pointer work: a click selects an item, a double click activates it
/// (a video transition becomes the default, anything else applies), and dragging an item out starts a drag with its id
/// under <see cref="EffectItem.DragFormat"/>.
/// </summary>
public sealed partial class EffectsView : UserControl
{
    private const double DragThreshold = 4;

    private Point? _pressPoint;
    private EffectItem? _pressItem;

    /// <summary>Creates the view.</summary>
    public EffectsView()
    {
        InitializeComponent();
        Lists.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Lists.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        Lists.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        Lists.DoubleTapped += OnDoubleTapped;
    }

    private EffectsViewModel? ViewModel => DataContext as EffectsViewModel;

    private static EffectItem? ItemAt(object? source) => (source as StyledElement)?.DataContext as EffectItem;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || ItemAt(e.Source) is not { } item)
        {
            return;
        }

        ViewModel?.SelectCommand.Execute(item);
        _pressItem = item;
        _pressPoint = point.Position;
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressPoint is not { } start || _pressItem is not { } item || ViewModel is { IsReadOnly: true })
        {
            // No drag to the timeline while an agent holds the project (AGENT_INTEGRATION 3.7).
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - start.X) < DragThreshold && Math.Abs(position.Y - start.Y) < DragThreshold)
        {
            return;
        }

        _pressPoint = null;
        _pressItem = null;
        // Same reason as the media bin: the string-keyed "cutlass/..." formats need the obsolete DataObject API.
#pragma warning disable CS0618
        var data = new DataObject();
        data.Set(EffectItem.DragFormat, item.Id);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
#pragma warning restore CS0618
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressPoint = null;
        _pressItem = null;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ItemAt(e.Source) is { } item && e.Source is not Button)
        {
            ViewModel?.ActivateCommand.Execute(item);
        }
    }
}
