using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cutlass.App.Controls;
using Cutlass.App.Services;
using Cutlass.App.ViewModels;

namespace Cutlass.App.Views;

/// <summary>
/// The viewer panel (DESIGN 4.3). The view attaches its <see cref="Controls.VideoSurface"/> to the view model's
/// frame presenter, reports the surface's physical size for Auto quality, turns the view model's placeholder tile into
/// an image from the shared <see cref="FilmstripCache"/>, focuses the click-to-type timecode field, and lays out and
/// drives the Source mini ruler (scrub, In and Out handles). Every decision is made by the view models.
/// </summary>
public sealed partial class ViewerView : UserControl
{
    // How close (in pixels) a press must be to an In or Out handle to drag it instead of scrubbing.
    private const double HandleGrab = 6;

    // How far (in pixels) the pointer moves before a press on the canvas or a stream handle becomes a drag.
    private const double DragThreshold = 4;

    private ViewerViewModel? _attached;
    private RulerDrag _rulerDrag;
    private (Point Start, SourceStreams Streams)? _sourcePress;

    /// <summary>Creates the view.</summary>
    public ViewerView()
    {
        InitializeComponent();
        Surface.SizeChanged += (_, _) => ReportViewport();
        SourceRuler.SizeChanged += (_, _) => LayoutRuler();
        SourceRuler.PointerPressed += OnRulerPressed;
        SourceRuler.PointerMoved += OnRulerMoved;
        SourceRuler.PointerReleased += OnRulerReleased;
        SourceRuler.PointerCaptureLost += (_, _) => _rulerDrag = RulerDrag.None;
        PictureArea.PointerPressed += (_, e) => OnSourcePressed(e, SourceStreams.Both);
        VideoOnlyHandle.PointerPressed += (_, e) => OnSourcePressed(e, SourceStreams.Video);
        AudioOnlyHandle.PointerPressed += (_, e) => OnSourcePressed(e, SourceStreams.Audio);
        foreach (var element in new Control[] { PictureArea, VideoOnlyHandle, AudioOnlyHandle })
        {
            element.PointerMoved += OnSourceMoved;
            element.PointerReleased += (_, _) => _sourcePress = null;
        }

        TimecodeHit.PointerPressed += OnTimecodePressed;
        TimecodeBox.LostFocus += OnTimecodeLostFocus;
    }

    private enum RulerDrag
    {
        None,
        Scrub,
        In,
        Out,
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Attach();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FilmstripCache.Shared.Loaded += OnSheetLoaded;
        FilmstripCache.Shared.Evicted += OnSheetEvicted;
        Attach();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        FilmstripCache.Shared.Loaded -= OnSheetLoaded;
        FilmstripCache.Shared.Evicted -= OnSheetEvicted;
        if (_attached is { } vm)
        {
            vm.Presenter.Detach(Surface);
            vm.Presenter.DetachSecondary(SecondSurface);
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.Source.PropertyChanged -= OnSourcePropertyChanged;
        }

        _attached = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void Attach()
    {
        if (DataContext is not ViewerViewModel vm || VisualRoot is null || ReferenceEquals(vm, _attached))
        {
            return;
        }

        if (_attached is { } old)
        {
            old.Presenter.Detach(Surface);
            old.Presenter.DetachSecondary(SecondSurface);
            old.PropertyChanged -= OnViewModelPropertyChanged;
            old.Source.PropertyChanged -= OnSourcePropertyChanged;
        }

        _attached = vm;
        vm.Presenter.Attach(Surface);
        vm.Presenter.AttachSecondary(SecondSurface);
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.Source.PropertyChanged += OnSourcePropertyChanged;
        ReportViewport();
        UpdatePlaceholder();
        LayoutRuler();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewerViewModel.PlaceholderTile):
                UpdatePlaceholder();
                break;
            case nameof(ViewerViewModel.IsEditingTimecode) when _attached is { IsEditingTimecode: true }:
                // The field becomes visible in this layout pass; focus it and select the text once it is there.
                Dispatcher.UIThread.Post(() =>
                {
                    TimecodeBox.Focus();
                    TimecodeBox.SelectAll();
                });
                break;
            case nameof(ViewerViewModel.IsSourceActive):
                LayoutRuler();
                break;
            case nameof(ViewerViewModel.IsTwoUp):
                // The main surface becomes the left half of the two-up.
                Grid.SetColumnSpan(Surface, _attached?.IsTwoUp == true ? 1 : 2);
                Surface.Margin = _attached?.IsTwoUp == true ? new Thickness(8, 8, 4, 8) : new Thickness(8);
                if (_attached?.IsTwoUp != true)
                {
                    SecondSurface.Frame = null;
                }

                break;
        }
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SourceViewerViewModel.PlayheadFraction) or nameof(SourceViewerViewModel.InFraction)
            or nameof(SourceViewerViewModel.OutFraction) or nameof(SourceViewerViewModel.HasIn) or nameof(SourceViewerViewModel.HasOut))
        {
            LayoutRuler();
        }
    }

    private void OnSheetLoaded(object? sender, EventArgs e) => UpdatePlaceholder();

    // The placeholder is cut from a cached sheet that is disposed soon after eviction: cut it again (the sheet decodes
    // anew, and Loaded brings the placeholder back).
    private void OnSheetEvicted(object? sender, Bitmap sheet)
    {
        if (Surface.Placeholder is CroppedBitmap { Source: { } source } && ReferenceEquals(source, sheet))
        {
            UpdatePlaceholder();
        }
    }

    private void UpdatePlaceholder()
    {
        if (_attached?.PlaceholderTile is not { } tile)
        {
            Surface.Placeholder = null;
            return;
        }

        Surface.Placeholder = FilmstripCache.Shared.Get(tile.SheetPath) is { } sheet
            ? new CroppedBitmap(sheet, new PixelRect(tile.X, tile.Y, tile.Width, tile.Height))
            : null;
    }

    private void ReportViewport()
    {
        if (_attached is null)
        {
            return;
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        _attached.SetViewport(Surface.Bounds.Width * scaling, Surface.Bounds.Height * scaling);
    }

    // DESIGN 4.3: dragging the Source canvas drags video and audio, the "Video only" and "Audio only" handles one stream.
    private void OnSourcePressed(PointerPressedEventArgs e, SourceStreams streams)
    {
        _sourcePress = _attached is { IsSourceActive: true } && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            ? (e.GetPosition(this), streams)
            : null;
    }

    private async void OnSourceMoved(object? sender, PointerEventArgs e)
    {
        if (_sourcePress is not { } press)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - press.Start.X) < DragThreshold && Math.Abs(position.Y - press.Start.Y) < DragThreshold)
        {
            return;
        }

        _sourcePress = null;
        if (_attached?.CreateSourceDrag(press.Streams) is not { } payload)
        {
            return;
        }

        // The string-keyed "cutlass/..." formats need the obsolete DataObject API, like the media bin's drags.
#pragma warning disable CS0618
        var data = new DataObject();
        data.Set(DragFormats.SourceRange, payload.Format());
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
#pragma warning restore CS0618
    }

    private void OnTimecodePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_attached is { } vm && e.GetCurrentPoint(TimecodeHit).Properties.IsLeftButtonPressed)
        {
            vm.BeginTimecodeEdit();
            e.Handled = true;
        }
    }

    // Leaving the field without Enter cancels (Esc reaches the app first and cancels through the keymap).
    private void OnTimecodeLostFocus(object? sender, RoutedEventArgs e) => _attached?.CancelTimecodeEdit();

    private void LayoutRuler()
    {
        var source = _attached?.Source;
        var width = SourceRuler.Bounds.Width;
        if (source is null || width <= 0)
        {
            return;
        }

        var lo = source.InFraction * width;
        var hi = source.OutFraction * width;
        Canvas.SetLeft(SourceRange, lo);
        SourceRange.Width = Math.Max(0, hi - lo);
        SourceRange.IsVisible = source.HasIn || source.HasOut;
        Canvas.SetLeft(SourceInHandle, Math.Clamp(lo, 0, width - SourceInHandle.Width));
        SourceInHandle.IsVisible = source.HasIn;
        Canvas.SetLeft(SourceOutHandle, Math.Clamp(hi - SourceOutHandle.Width, 0, width - SourceOutHandle.Width));
        SourceOutHandle.IsVisible = source.HasOut;
        Canvas.SetLeft(SourcePlayhead, Math.Clamp((source.PlayheadFraction * width) - 1, 0, width - SourcePlayhead.Width));
    }

    private double Fraction(PointerEventArgs e)
    {
        var width = SourceRuler.Bounds.Width;
        return width > 0 ? e.GetPosition(SourceRuler).X / width : 0;
    }

    private void OnRulerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_attached?.Source is not { HasMedia: true } source || !e.GetCurrentPoint(SourceRuler).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var x = e.GetPosition(SourceRuler).X;
        var width = SourceRuler.Bounds.Width;
        _rulerDrag = source.HasOut && Math.Abs(x - (source.OutFraction * width)) <= HandleGrab ? RulerDrag.Out
            : source.HasIn && Math.Abs(x - (source.InFraction * width)) <= HandleGrab ? RulerDrag.In
            : RulerDrag.Scrub;
        e.Pointer.Capture(SourceRuler);
        e.Handled = true;
        Apply(Fraction(e));
    }

    private void OnRulerMoved(object? sender, PointerEventArgs e)
    {
        if (_rulerDrag != RulerDrag.None)
        {
            Apply(Fraction(e));
            e.Handled = true;
        }
    }

    private void OnRulerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_rulerDrag != RulerDrag.None)
        {
            _rulerDrag = RulerDrag.None;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void Apply(double fraction)
    {
        if (_attached?.Source is not { } source)
        {
            return;
        }

        switch (_rulerDrag)
        {
            case RulerDrag.In:
                source.SetIn(source.FrameAtFraction(fraction));
                break;
            case RulerDrag.Out:
                // The Out handle sits after the last frame of the range (Out is exclusive).
                source.SetOut(source.FrameAtFraction(fraction) + 1);
                break;
            case RulerDrag.Scrub:
                source.ScrubTo(fraction);
                break;
        }
    }
}
