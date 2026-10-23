using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Cutlass.App.Controls.Timeline;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.App.Views.Dialogs;
using Cutlass.Core.Editing;

namespace Cutlass.App.Views;

/// <summary>
/// The timeline panel body: the <see cref="Controls.TimelineControl"/> plus its scroll bars, the inline track rename
/// box (double-click a header name) and the marker editor (double-click a marker). It also answers the view model's
/// confirmation requests with a modal message box.
/// </summary>
public sealed partial class TimelineView : UserControl
{
    /// <summary>The marker colours offered by the editor (the first is the default Marker token).</summary>
    public static readonly IReadOnlyList<string> MarkerColors =
        [TimelineEdits.DefaultMarkerColor, "#E5484D", "#F76B15", "#30A46C", "#3E9BFF", "#8E4EC6", "#E93D82", "#A0A0A0"];

    private TimelineViewModel? _vm;
    private string? _renameTrackId;
    private string? _markerId;
    private string _markerColor = TimelineEdits.DefaultMarkerColor;

    /// <summary>Creates the view.</summary>
    public TimelineView()
    {
        InitializeComponent();
        RenameBox.KeyDown += OnRenameKeyDown;
        RenameBox.LostFocus += (_, _) => CommitRename();

        // The window claims Esc first (DESIGN 7 focus rule), so the boxes listen to handled events too.
        RenameBox.AddHandler(KeyDownEvent, OnRenameEscape, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        MarkerName.AddHandler(KeyDownEvent, OnMarkerKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        MarkerOk.Click += (_, _) => CommitMarker();
        MarkerCancel.Click += (_, _) => CloseMarkerEditor();
        MarkerDelete.Click += (_, _) => DeleteMarker();
        foreach (var color in MarkerColors)
        {
            var swatch = new Button
            {
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 4),
                Focusable = false,
                Tag = color,
                Content = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(Color.Parse(color)) },
            };
            ToolTip.SetTip(swatch, color);
            swatch.Click += (_, _) => SelectMarkerColor(color);
            MarkerSwatches.Children.Add(swatch);
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is { } old)
        {
            old.TrackRenameRequested -= OnTrackRenameRequested;
            old.MarkerEditRequested -= OnMarkerEditRequested;
            old.InlineEditorsCloseRequested -= OnInlineEditorsCloseRequested;
            old.ConfirmAsync = null;
        }

        _vm = DataContext as TimelineViewModel;
        if (_vm is { } vm)
        {
            vm.TrackRenameRequested += OnTrackRenameRequested;
            vm.MarkerEditRequested += OnMarkerEditRequested;
            vm.InlineEditorsCloseRequested += OnInlineEditorsCloseRequested;
            vm.ConfirmAsync = ConfirmAsync;
        }
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return true;
        }

        return await new MessageDialog(title, message, ["Delete", "Cancel"], cancelIndex: 1).ShowAsync(owner) == 0;
    }

    private void OnTrackRenameRequested(object? sender, string trackId)
    {
        if (_vm is not { } vm || TimelineQuery.FindTrack(vm.Session.Current, trackId) is not { } track
            || vm.GetLayout().Row(trackId) is not { } row)
        {
            return;
        }

        _renameTrackId = trackId;
        var top = vm.ViewState.ToControlY(row.Top) + 4;
        Canvas.SetLeft(RenameBox, 38);
        Canvas.SetTop(RenameBox, Math.Max(TimelineViewState.RulerHeight, top));
        RenameBox.Text = track.Name;
        RenameBox.IsVisible = true;
        RenameBox.Focus();
        RenameBox.SelectAll();
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
    }

    private void OnRenameEscape(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && RenameBox.IsVisible)
        {
            _renameTrackId = null;
            RenameBox.IsVisible = false;
            Timeline.Focus();
        }
    }

    private void CommitRename()
    {
        if (_renameTrackId is not { } trackId)
        {
            return;
        }

        _renameTrackId = null;
        RenameBox.IsVisible = false;
        _vm?.RenameTrack(trackId, RenameBox.Text);
        Timeline.Focus();
    }

    /// <summary>An agent takes the lock: commit the open inline editors as focus loss does and close them.</summary>
    private void OnInlineEditorsCloseRequested(object? sender, EventArgs e)
    {
        if (RenameBox.IsVisible)
        {
            CommitRename();
        }

        if (MarkerEditor.IsVisible)
        {
            CommitMarker();
        }
    }

    private void OnMarkerEditRequested(object? sender, string markerId)
    {
        if (_vm is not { } vm || vm.Session.Current.Sequence.Markers.FirstOrDefault(m => m.Id == markerId) is not { } marker)
        {
            return;
        }

        _markerId = markerId;
        MarkerName.Text = marker.Name;
        SelectMarkerColor(marker.Color);
        var x = TimelineViewState.HeaderWidth + vm.ViewState.FrameToX(marker.Frame) - 8;
        var maxX = Math.Max(TimelineViewState.HeaderWidth, Bounds.Width - MarkerEditor.Width - 24);
        Canvas.SetLeft(MarkerEditor, Math.Clamp(x, TimelineViewState.HeaderWidth, maxX));
        Canvas.SetTop(MarkerEditor, TimelineViewState.RulerHeight + 2);
        MarkerEditor.IsVisible = true;
        MarkerName.Focus();
        MarkerName.SelectAll();
    }

    private void OnMarkerKeyDown(object? sender, KeyEventArgs e)
    {
        if (!MarkerEditor.IsVisible)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitMarker();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseMarkerEditor();
        }
    }

    private void SelectMarkerColor(string color)
    {
        _markerColor = color;
        foreach (var child in MarkerSwatches.Children.OfType<Button>())
        {
            var active = string.Equals(child.Tag as string, color, StringComparison.OrdinalIgnoreCase);
            child.BorderBrush = active ? Brushes.White : Brushes.Transparent;
            child.BorderThickness = new Thickness(active ? 2 : 0);
        }
    }

    private void CommitMarker()
    {
        if (_markerId is { } id)
        {
            _vm?.UpdateMarker(id, MarkerName.Text, _markerColor);
        }

        CloseMarkerEditor();
    }

    private void DeleteMarker()
    {
        if (_markerId is { } id)
        {
            _vm?.RemoveMarker(id);
        }

        CloseMarkerEditor();
    }

    private void CloseMarkerEditor()
    {
        _markerId = null;
        MarkerEditor.IsVisible = false;
        Timeline.Focus();
    }
}
