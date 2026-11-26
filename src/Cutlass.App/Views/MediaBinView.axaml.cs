using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Cutlass.App.Controls.Timeline;
using Cutlass.App.Services;
using Cutlass.App.ViewModels.MediaBin;

namespace Cutlass.App.Views;

/// <summary>
/// The media panel (DESIGN 4.2). The view owns the pointer work: dragging a card out (with
/// <see cref="DragFormats.MediaAssetId"/>), double click to open the item in the Source monitor, and OS file drops.
/// </summary>
public sealed partial class MediaBinView : UserControl
{
    private const double DragThreshold = 4;

    private Point? _pressPoint;
    private MediaItemViewModel? _pressItem;
    private MediaItemViewModel? _skimItem;

    /// <summary>Creates the view.</summary>
    public MediaBinView()
    {
        InitializeComponent();
        foreach (var list in new[] { GridList, RowList })
        {
            list.AddHandler(PointerPressedEvent, OnItemPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            list.AddHandler(PointerMovedEvent, OnItemPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            list.AddHandler(PointerReleasedEvent, OnItemPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
            list.DoubleTapped += OnItemDoubleTapped;
        }

        // DESIGN 4.2 hover skim on the grid cards.
        GridList.AddHandler(PointerMovedEvent, OnSkimMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        GridList.PointerExited += (_, _) => StopSkim();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private MediaBinViewModel? ViewModel => DataContext as MediaBinViewModel;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FilmstripCache.Shared.Evicted += OnSheetEvicted;
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        FilmstripCache.Shared.Evicted -= OnSheetEvicted;
        StopSkim();
        base.OnDetachedFromVisualTree(e);
    }

    private static MediaItemViewModel? ItemAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as MediaItemViewModel;

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressItem = ItemAt(e.Source);
        _pressPoint = _pressItem is null ? null : point.Position;
    }

    private async void OnItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressPoint is not { } start || _pressItem is not { Asset: not null } item || ViewModel is { IsReadOnly: true })
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - start.X) < DragThreshold && Math.Abs(position.Y - start.Y) < DragThreshold)
        {
            return;
        }

        _pressPoint = null;
        _pressItem = null;
        // DragFormats is a string-keyed contract that TimelineControl reads through IDataObject; the new
        // DataTransfer formats cannot carry its '/' names, so the obsolete API is kept on purpose here.
#pragma warning disable CS0618
        var data = new DataObject();
        data.Set(DragFormats.MediaAssetId, item.Id);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
#pragma warning restore CS0618
    }

    private void OnSkimMoved(object? sender, PointerEventArgs e)
    {
        var container = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (container?.DataContext is not MediaItemViewModel { Asset: { } asset } item
            || ViewModel?.Artifacts?.TryGetSheet(asset) is not { } sheet
            || container.Bounds.Width <= 0)
        {
            StopSkim();
            return;
        }

        if (!ReferenceEquals(item, _skimItem))
        {
            StopSkim();
            _skimItem = item;
        }

        var tile = sheet.SkimTile(e.GetPosition(container).X / container.Bounds.Width);
        if (FilmstripCache.Shared.Get(tile.SheetPath) is { } bitmap)
        {
            item.SkimImage = new CroppedBitmap(bitmap, new PixelRect(tile.X, tile.Y, tile.Width, tile.Height));
        }
    }

    // The skim image is cut from a cached sheet that is disposed soon after eviction: drop it, the next move cuts a
    // new one.
    private void OnSheetEvicted(object? sender, Bitmap sheet)
    {
        if (_skimItem is { SkimImage: CroppedBitmap { Source: { } source } } item && ReferenceEquals(source, sheet))
        {
            item.SkimImage = null;
        }
    }

    private void StopSkim()
    {
        if (_skimItem is { } item)
        {
            item.SkimImage = null;
            _skimItem = null;
        }
    }

    private void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressPoint = null;
        _pressItem = null;
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ItemAt(e.Source) is { Asset: not null } item)
        {
            ViewModel?.OpenInSourceCommand.Execute(item);
        }
    }

    private void OnMediaTabTapped(object? sender, TappedEventArgs e) => ViewModel?.ShowMediaTabCommand.Execute(null);

    private void OnEffectsTabTapped(object? sender, TappedEventArgs e) => ViewModel?.ShowEffectsTabCommand.Execute(null);

    private void OnAgentTabTapped(object? sender, TappedEventArgs e) => ViewModel?.ShowAgentTabPageCommand.Execute(null);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // While an agent holds the project (AGENT_INTEGRATION 3.7) the bin refuses file drops.
        e.DragEffects = ViewModel is { IsReadOnly: false } && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { IsReadOnly: false } vm || e.DataTransfer.TryGetFiles() is not { } items)
        {
            return;
        }

        e.Handled = true;
        var paths = items.Select(i => i.TryGetLocalPath()).Where(p => p is not null).Select(p => p!).ToList();
        if (paths.Count > 0)
        {
            await vm.ImportPathsAsync(paths);
        }
    }
}
