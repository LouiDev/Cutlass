using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Cutlass.App.ViewModels;

namespace Cutlass.App.Views;

/// <summary>
/// The main editor window (DESIGN 3). The view applies and reads back the panel layout (GridSplitter positions,
/// saved in settings), routes keys through the view model's key binding service and runs the dirty prompt before
/// closing.
/// </summary>
public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    private bool _closeConfirmed;

    /// <summary>Creates the window and loads its XAML.</summary>
    public MainWindow()
    {
        InitializeComponent();
        PlayheadBox.LostFocus += OnPlayheadLostFocus;
        // DESIGN 4.3: working in the timeline panel brings the Timeline monitor back, so the keys follow what shows.
        TimelinePanel.AddHandler(PointerPressedEvent, OnTimelinePanelPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.CloseRequested -= OnCloseRequested;
            _vm.LayoutLoaded -= OnLayoutLoaded;
            _vm.FocusResetRequested -= OnFocusResetRequested;
            _vm.Keys.Detach();
        }

        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null)
        {
            _vm.CloseRequested += OnCloseRequested;
            _vm.LayoutLoaded += OnLayoutLoaded;
            _vm.FocusResetRequested += OnFocusResetRequested;
            _vm.Keys.Attach(this);
        }
    }

    /// <inheritdoc />
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || _vm is null || _vm.HasShutDown)
        {
            return;
        }

        // Ask first (dirty prompt, running export), then close for real.
        e.Cancel = true;
        CaptureLayout();
        if (await _vm.PrepareCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>
    /// Renders the window into a PNG file (smoke screenshots) through <see cref="WindowCapture.SaveAsync"/>, which
    /// lays it out first and works while another window covers this one.
    /// </summary>
    /// <param name="path">The PNG file.</param>
    /// <returns>A task that completes when the file is written.</returns>
    public Task CaptureAsync(string path) => WindowCapture.SaveAsync(this, path);

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    // AGENT_INTEGRATION 3.2: on grant, inline editors (timecode, track rename, inspector text) lose focus and commit.
    private void OnFocusResetRequested(object? sender, EventArgs e) => FocusManager?.ClearFocus();

    private void OnLayoutLoaded(object? sender, EventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var top = _vm.TopRowFraction;
        RootGrid.RowDefinitions[1].Height = new GridLength(top, GridUnitType.Star);
        RootGrid.RowDefinitions[3].Height = new GridLength(1 - top, GridUnitType.Star);
        var media = _vm.MediaColumnFraction;
        var inspector = _vm.InspectorColumnFraction;
        TopGrid.ColumnDefinitions[0].Width = new GridLength(media, GridUnitType.Star);
        TopGrid.ColumnDefinitions[2].Width = new GridLength(Math.Max(0.1, 1 - media - inspector), GridUnitType.Star);
        TopGrid.ColumnDefinitions[4].Width = new GridLength(inspector, GridUnitType.Star);
    }

    private void CaptureLayout()
    {
        if (_vm is null)
        {
            return;
        }

        var top = RootGrid.RowDefinitions[1].ActualHeight;
        var bottom = RootGrid.RowDefinitions[3].ActualHeight;
        var media = TopGrid.ColumnDefinitions[0].ActualWidth;
        var viewer = TopGrid.ColumnDefinitions[2].ActualWidth;
        var inspector = TopGrid.ColumnDefinitions[4].ActualWidth;
        var width = media + viewer + inspector;
        if (top + bottom <= 0 || width <= 0)
        {
            return;
        }

        _vm.UpdateLayout(top / (top + bottom), media / width, inspector / width);
    }

    private void OnPlayheadLostFocus(object? sender, RoutedEventArgs e) => _vm?.CommitPlayheadTextCommand.Execute(null);

    private void OnTimelinePanelPressed(object? sender, PointerPressedEventArgs e) => _vm?.Viewer.ShowTimeline();
}
