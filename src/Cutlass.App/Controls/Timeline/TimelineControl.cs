using System.Collections.Immutable;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Cutlass.App.Controls.Drag;
using Cutlass.App.Services;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Session;

namespace Cutlass.App.Controls.Timeline;

/// <summary>
/// The custom-drawn timeline (DESIGN 5, TECH_BRIEF 10.2): track headers, ruler, lanes, clips, transitions, In/Out,
/// markers, snap line and playhead, drawn from <see cref="TimelineViewModel.Project"/> (live during gestures) and the
/// <see cref="TimelineViewState"/>. The control owns all pointer, wheel, key and drag-and-drop input and turns it into
/// view model calls and <see cref="IDragOperation"/> drags; the view model never touches the control.
/// </summary>
public sealed partial class TimelineControl : Control
{
    /// <summary>The view model property.</summary>
    public static readonly StyledProperty<TimelineViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<TimelineControl, TimelineViewModel?>(nameof(ViewModel));

    private TimelineViewModel? _attached;
    private TimelinePalette _palette = TimelinePalette.Default;

    static TimelineControl()
    {
        FocusableProperty.OverrideDefaultValue<TimelineControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<TimelineControl>(true);
    }

    /// <summary>Creates the control and registers it as a drop target for media bin drags.</summary>
    public TimelineControl()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    /// <summary>The timeline view model to draw and edit.</summary>
    public TimelineViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewModelProperty)
        {
            CancelDrag();
            Attach(VisualRoot is null ? null : ViewModel);
            UpdateViewport(Bounds.Size);
            InvalidateVisual();
        }
        else if (change.Property == BoundsProperty)
        {
            UpdateViewport(Bounds.Size);
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _palette = TimelinePalette.From(this);
        LoadIcons();
        FilmstripCache.Shared.Loaded += OnFilmstripLoaded;
        Attach(ViewModel);
        UpdateViewport(Bounds.Size);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelDrag();
        FilmstripCache.Shared.Loaded -= OnFilmstripLoaded;
        Attach(null);
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // The timeline fills whatever it is given; in an unbounded slot it asks for a usable default.
        var width = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height;
        return new Size(width, height);
    }

    private void Attach(TimelineViewModel? vm)
    {
        if (ReferenceEquals(vm, _attached))
        {
            return;
        }

        if (_attached is { } old)
        {
            old.Session.Changed -= OnSessionChanged;
            old.SelectionChanged -= OnRepaint;
            old.ViewChanged -= OnRepaint;
            old.PropertyChanged -= OnViewModelPropertyChanged;
            old.CancelDragRequested -= OnCancelDragRequested;
            old.ArtifactsChanged -= OnArtifactsChanged;
            old.MediaLinkStatesChanged -= OnRepaint;
        }

        _attached = vm;
        if (vm is not null)
        {
            vm.Session.Changed += OnSessionChanged;
            vm.SelectionChanged += OnRepaint;
            vm.ViewChanged += OnRepaint;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.CancelDragRequested += OnCancelDragRequested;
            vm.ArtifactsChanged += OnArtifactsChanged;
            vm.MediaLinkStatesChanged += OnRepaint;
        }
    }

    private void UpdateViewport(Size size) =>
        ViewModel?.ViewState.SetViewport(size.Width - TimelineViewState.HeaderWidth, size.Height - TimelineViewState.RulerHeight);

    private void OnSessionChanged(object? sender, ProjectChangedEventArgs e)
    {
        if (e.Kind != ChangeKind.Saved)
        {
            InvalidateVisual();
        }
    }

    private void OnRepaint(object? sender, EventArgs e) => InvalidateVisual();

    private void OnCancelDragRequested(object? sender, EventArgs e) => CancelDrag();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TimelineViewModel.Playhead):
            case nameof(TimelineViewModel.InPoint):
            case nameof(TimelineViewModel.OutPoint):
            case nameof(TimelineViewModel.Tool):
            case nameof(TimelineViewModel.SnapEnabled):
            case nameof(TimelineViewModel.SelectedVolumeKey):
            case nameof(TimelineViewModel.SelectedTrackId):
                InvalidateVisual();
                break;
            case nameof(TimelineViewModel.IsReadOnly):
                // The Accent frame, and no drop ghost may linger.
                _drop = null;
                InvalidateVisual();
                break;
        }
    }

    /// <summary>
    /// A pending drop: media bin items, a Source monitor range (<see cref="Source"/>) or an Effects tab item
    /// (<see cref="EffectId"/>), with the clips or span it would change, at a frame, on which track.
    /// </summary>
    /// <param name="AssetIds">The dragged media ids (empty for Source and effect drops).</param>
    /// <param name="TrackId">The hovered track.</param>
    /// <param name="Frame">The (snapped) drop frame.</param>
    /// <param name="Insert">True while Ctrl is held.</param>
    /// <param name="Placements">The ghost clips, relative to <paramref name="Frame"/>.</param>
    /// <param name="SnapFrame">The engaged snap target, or null.</param>
    private sealed record DropPreview(
        IReadOnlyList<string> AssetIds, string TrackId, long Frame, bool Insert, ImmutableArray<ClipPlacement> Placements, long? SnapFrame)
    {
        /// <summary>The Source monitor range of a Source drop, or null.</summary>
        public SourceDrag? Source { get; init; }

        /// <summary>The effect id of an Effects tab drop, or null.</summary>
        public string? EffectId { get; init; }

        /// <summary>The span an effect drop changes (drawn instead of <see cref="Placements"/>), or null.</summary>
        public EffectDropPreview? Effect { get; init; }
    }

    /// <summary>The Avalonia-facing state of the current or pending drag.</summary>
    /// <param name="Operation">The drag.</param>
    /// <param name="Context">Its pointer state.</param>
    private sealed record ActiveDrag(IDragOperation Operation, DragContext Context);

    /// <summary>Pointer shapes used over the hit zones.</summary>
    private static class Cursors
    {
        public static readonly Cursor Arrow = new(StandardCursorType.Arrow);
        public static readonly Cursor SizeWestEast = new(StandardCursorType.SizeWestEast);
        public static readonly Cursor SizeNorthSouth = new(StandardCursorType.SizeNorthSouth);
        public static readonly Cursor Cross = new(StandardCursorType.Cross);
        public static readonly Cursor Hand = new(StandardCursorType.Hand);
    }

    /// <summary>The header icon geometries from <c>Themes/Icons.axaml</c>, or null when missing.</summary>
    private Geometry? _iconEye;

    private Geometry? _iconLock;

    private Geometry? _iconMute;

    private Geometry? _iconSolo;

    private void LoadIcons()
    {
        _iconEye = FindGeometry("IconEye");
        _iconLock = FindGeometry("IconLock");
        _iconMute = FindGeometry("IconMute");
        _iconSolo = FindGeometry("IconSolo");
    }

    private Geometry? FindGeometry(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as Geometry : null;
}
