using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Cutlass.App.Services;

namespace Cutlass.App.Controls;

/// <summary>
/// The stereo master peak meter (DESIGN 4.5) at the timeline's right edge: two vertical bars from
/// <see cref="MeterBallistics.FloorDb"/> to 0 dBFS, green up to -6 dBFS, yellow up to -1 dBFS and red above (DESIGN 8),
/// each with a peak-hold line, plus a dB scale. The viewer view model feeds the levels while playing.
/// </summary>
public sealed class AudioMeter : Control
{
    /// <summary>The left bar level in dBFS.</summary>
    public static readonly StyledProperty<double> LeftDbProperty =
        AvaloniaProperty.Register<AudioMeter, double>(nameof(LeftDb), MeterBallistics.FloorDb);

    /// <summary>The right bar level in dBFS.</summary>
    public static readonly StyledProperty<double> RightDbProperty =
        AvaloniaProperty.Register<AudioMeter, double>(nameof(RightDb), MeterBallistics.FloorDb);

    /// <summary>The left peak-hold level in dBFS.</summary>
    public static readonly StyledProperty<double> LeftPeakDbProperty =
        AvaloniaProperty.Register<AudioMeter, double>(nameof(LeftPeakDb), MeterBallistics.FloorDb);

    /// <summary>The right peak-hold level in dBFS.</summary>
    public static readonly StyledProperty<double> RightPeakDbProperty =
        AvaloniaProperty.Register<AudioMeter, double>(nameof(RightPeakDb), MeterBallistics.FloorDb);

    /// <summary>The top of the green zone.</summary>
    public const double GreenTopDb = -6;

    /// <summary>The top of the yellow zone.</summary>
    public const double YellowTopDb = -1;

    private const double BarWidth = 5;
    private const double BarGap = 2;
    private const double Inset = 4;
    private const double LabelSize = 9;

    private static readonly double[] ScaleDb = [0, -6, -12, -24, -48];

    // DESIGN 8 meter colours (MeterLowColor, MeterMidColor and MeterHighColor in the theme).
    private static readonly Color LowColor = Color.Parse("#3DDC84");
    private static readonly Color MidColor = Color.Parse("#F5D547");
    private static readonly Color HighColor = Color.Parse("#FF4D4D");

    static AudioMeter()
    {
        AffectsRender<AudioMeter>(LeftDbProperty, RightDbProperty, LeftPeakDbProperty, RightPeakDbProperty);
    }

    /// <summary>The left bar level in dBFS.</summary>
    public double LeftDb
    {
        get => GetValue(LeftDbProperty);
        set => SetValue(LeftDbProperty, value);
    }

    /// <summary>The right bar level in dBFS.</summary>
    public double RightDb
    {
        get => GetValue(RightDbProperty);
        set => SetValue(RightDbProperty, value);
    }

    /// <summary>The left peak-hold level in dBFS.</summary>
    public double LeftPeakDb
    {
        get => GetValue(LeftPeakDbProperty);
        set => SetValue(LeftPeakDbProperty, value);
    }

    /// <summary>The right peak-hold level in dBFS.</summary>
    public double RightPeakDb
    {
        get => GetValue(RightPeakDbProperty);
        set => SetValue(RightPeakDbProperty, value);
    }

    /// <summary>The fraction of the meter height a level reaches (0 at the floor, 1 at 0 dBFS).</summary>
    /// <param name="db">The level in dBFS.</param>
    /// <returns>The fraction.</returns>
    public static double Fraction(double db) =>
        double.IsFinite(db) ? Math.Clamp((db - MeterBallistics.FloorDb) / -MeterBallistics.FloorDb, 0, 1) : 0;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(30, double.IsInfinity(availableSize.Height) ? 120 : availableSize.Height);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var palette = TimelinePalette.Default;
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(TimelinePalette.Brush(palette.Bg1), bounds);
        var top = bounds.Top + Inset + 6;
        var bottom = bounds.Bottom - Inset;
        var height = bottom - top;
        if (height < 10)
        {
            return;
        }

        var left = bounds.Left + Inset;
        DrawBar(context, palette, new Rect(left, top, BarWidth, height), LeftDb, LeftPeakDb);
        DrawBar(context, palette, new Rect(left + BarWidth + BarGap, top, BarWidth, height), RightDb, RightPeakDb);

        var tick = new Pen(TimelinePalette.Brush(palette.TextDisabled), 1);
        var labelX = left + (2 * BarWidth) + BarGap + 2;
        foreach (var db in ScaleDb)
        {
            var y = Math.Round(bottom - (Fraction(db) * height)) + 0.5;
            context.DrawLine(tick, new Point(labelX, y), new Point(labelX + 2, y));
            var text = new FormattedText(
                db.ToString("0", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default),
                LabelSize,
                TimelinePalette.Brush(palette.TextSecondary));
            context.DrawText(text, new Point(labelX + 3, y - (text.Height / 2)));
        }
    }

    private static Color ZoneColor(double db) =>
        db > YellowTopDb ? HighColor : db > GreenTopDb ? MidColor : LowColor;

    private static void DrawBar(DrawingContext context, TimelinePalette palette, Rect bar, double levelDb, double peakDb)
    {
        context.FillRectangle(TimelinePalette.Brush(palette.Bg0), bar);
        var level = Fraction(levelDb);
        if (level > 0)
        {
            // Draw the zones the level reaches, bottom up.
            foreach (var (from, to) in new[] { (MeterBallistics.FloorDb, GreenTopDb), (GreenTopDb, YellowTopDb), (YellowTopDb, 0.0) })
            {
                if (levelDb <= from)
                {
                    break;
                }

                var y0 = bar.Bottom - (Fraction(Math.Min(levelDb, to)) * bar.Height);
                var y1 = bar.Bottom - (Fraction(from) * bar.Height);
                context.FillRectangle(TimelinePalette.Brush(ZoneColor(to - 0.01)), new Rect(bar.Left, y0, bar.Width, Math.Max(0, y1 - y0)));
            }
        }

        if (peakDb > MeterBallistics.FloorDb)
        {
            var y = Math.Round(bar.Bottom - (Fraction(peakDb) * bar.Height)) + 0.5;
            context.DrawLine(new Pen(TimelinePalette.Brush(ZoneColor(peakDb)), 1), new Point(bar.Left, y), new Point(bar.Right, y));
        }
    }
}
