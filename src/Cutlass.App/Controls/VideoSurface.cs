using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Cutlass.App.Controls;

/// <summary>
/// The viewer canvas (DESIGN 4.3): a pure black frame letterboxed into the available space, the latest preview frame
/// scaled into it, an optional dim (placeholder while an exact frame decodes) and the 90% and 80% safe guides.
/// <see cref="Services.FramePresenter"/> sets <see cref="Frame"/> on the UI thread.
/// </summary>
public sealed class VideoSurface : Control
{
    /// <summary>The sequence width, used for the letterbox while no frame is shown.</summary>
    public static readonly StyledProperty<int> CanvasWidthProperty =
        AvaloniaProperty.Register<VideoSurface, int>(nameof(CanvasWidth), 1920);

    /// <summary>The sequence height, used for the letterbox while no frame is shown.</summary>
    public static readonly StyledProperty<int> CanvasHeightProperty =
        AvaloniaProperty.Register<VideoSurface, int>(nameof(CanvasHeight), 1080);

    /// <summary>Draws the safe guides.</summary>
    public static readonly StyledProperty<bool> ShowGuidesProperty =
        AvaloniaProperty.Register<VideoSurface, bool>(nameof(ShowGuides));

    /// <summary>Dims the frame (placeholder until the exact frame arrives).</summary>
    public static readonly StyledProperty<bool> IsDimmedProperty =
        AvaloniaProperty.Register<VideoSurface, bool>(nameof(IsDimmed));

    /// <summary>The thumbnail shown dimmed while an exact frame decodes (DESIGN 4.3), or null.</summary>
    public static readonly StyledProperty<IImage?> PlaceholderProperty =
        AvaloniaProperty.Register<VideoSurface, IImage?>(nameof(Placeholder));

    /// <summary>Shows <see cref="Placeholder"/> dimmed instead of the frame (an exact frame is decoding).</summary>
    public static readonly StyledProperty<bool> IsPlaceholderVisibleProperty =
        AvaloniaProperty.Register<VideoSurface, bool>(nameof(IsPlaceholderVisible));

    /// <summary>Hides the frame and shows only the black canvas (nothing to preview).</summary>
    public static readonly StyledProperty<bool> IsBlankProperty =
        AvaloniaProperty.Register<VideoSurface, bool>(nameof(IsBlank));

    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
    private static readonly IPen GuidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), 1);

    private Bitmap? _frame;

    static VideoSurface()
    {
        AffectsRender<VideoSurface>(CanvasWidthProperty, CanvasHeightProperty, ShowGuidesProperty, IsDimmedProperty, IsBlankProperty, PlaceholderProperty, IsPlaceholderVisibleProperty);
    }

    /// <summary>Creates the surface.</summary>
    public VideoSurface()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.MediumQuality);
    }

    /// <summary>The sequence width.</summary>
    public int CanvasWidth
    {
        get => GetValue(CanvasWidthProperty);
        set => SetValue(CanvasWidthProperty, value);
    }

    /// <summary>The sequence height.</summary>
    public int CanvasHeight
    {
        get => GetValue(CanvasHeightProperty);
        set => SetValue(CanvasHeightProperty, value);
    }

    /// <summary>Draws the safe guides.</summary>
    public bool ShowGuides
    {
        get => GetValue(ShowGuidesProperty);
        set => SetValue(ShowGuidesProperty, value);
    }

    /// <summary>Dims the frame.</summary>
    public bool IsDimmed
    {
        get => GetValue(IsDimmedProperty);
        set => SetValue(IsDimmedProperty, value);
    }

    /// <summary>The thumbnail shown dimmed while an exact frame decodes, or null.</summary>
    public IImage? Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Shows <see cref="Placeholder"/> dimmed instead of the frame.</summary>
    public bool IsPlaceholderVisible
    {
        get => GetValue(IsPlaceholderVisibleProperty);
        set => SetValue(IsPlaceholderVisibleProperty, value);
    }

    /// <summary>Shows only the black canvas.</summary>
    public bool IsBlank
    {
        get => GetValue(IsBlankProperty);
        set => SetValue(IsBlankProperty, value);
    }

    /// <summary>The frame shown. Set on the UI thread only (the presenter swaps its two bitmaps here).</summary>
    public Bitmap? Frame
    {
        get => _frame;
        set
        {
            _frame = value;
            InvalidateVisual();
        }
    }

    /// <summary>The largest rectangle of the given aspect ratio centred in the bounds.</summary>
    /// <param name="bounds">The available space.</param>
    /// <param name="width">The content width.</param>
    /// <param name="height">The content height.</param>
    /// <returns>The letterboxed rectangle.</returns>
    public static Rect Letterbox(Rect bounds, double width, double height)
    {
        if (width <= 0 || height <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return default;
        }

        var scale = Math.Min(bounds.Width / width, bounds.Height / height);
        var w = width * scale;
        var h = height * scale;
        return new Rect(bounds.X + ((bounds.Width - w) / 2), bounds.Y + ((bounds.Height - h) / 2), w, h);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var frame = IsBlank ? null : _frame;
        var target = frame is not null
            ? Letterbox(bounds, frame.PixelSize.Width, frame.PixelSize.Height)
            : Letterbox(bounds, CanvasWidth, CanvasHeight);
        if (target.Width <= 0 || target.Height <= 0)
        {
            return;
        }

        context.FillRectangle(Brushes.Black, target);
        if (!IsBlank && IsPlaceholderVisible && Placeholder is { } placeholder)
        {
            // The thumbnail of the topmost clip, dimmed, until the exact frame arrives (TECH_BRIEF 8.1 step 6).
            var size = placeholder.Size;
            var thumb = Letterbox(target, size.Width, size.Height);
            context.DrawImage(placeholder, new Rect(size), thumb);
            context.FillRectangle(DimBrush, target);
        }
        else if (frame is not null)
        {
            context.DrawImage(frame, new Rect(0, 0, frame.PixelSize.Width, frame.PixelSize.Height), target);
            if (IsDimmed)
            {
                context.FillRectangle(DimBrush, target);
            }
        }

        if (ShowGuides)
        {
            DrawGuide(context, target, 0.9);
            DrawGuide(context, target, 0.8);
        }
    }

    private static void DrawGuide(DrawingContext context, Rect canvas, double fraction)
    {
        var w = canvas.Width * fraction;
        var h = canvas.Height * fraction;
        var rect = new Rect(canvas.X + ((canvas.Width - w) / 2), canvas.Y + ((canvas.Height - h) / 2), w, h);
        // Half-pixel offset keeps the 1 px line crisp.
        context.DrawRectangle(null, GuidePen, rect.Deflate(0.5));
    }
}
