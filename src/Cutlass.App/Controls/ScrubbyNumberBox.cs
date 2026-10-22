using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Cutlass.App.Controls;

/// <summary>
/// The numeric field of every inspector value (DESIGN 4.4). It shows the formatted number with a unit suffix; dragging
/// it horizontally changes the value by <see cref="Step"/> per pixel (Shift 10x, Ctrl 0.1x); a double click or Enter
/// opens a text box where Enter commits and Esc cancels; a right click resets to <see cref="DefaultValue"/>. Text is
/// parsed with the invariant culture and also accepts a comma decimal separator. A drag raises
/// <see cref="DragStartedEvent"/> once and <see cref="DragCompletedEvent"/> at the end (Esc cancels it), so the owner
/// can wrap the drag in one session gesture, and every user change raises <see cref="ValueChangedEvent"/>.
/// </summary>
/// <remarks>
/// DESIGN 4.4 says "drag the label"; here the whole value box is the drag handle and the label sits beside it, which
/// keeps the dense rows simple. A NaN <see cref="Value"/> (or <see cref="IsMixed"/>) shows a dash for a selection whose
/// values differ; any user value then differs from NaN, so it always reaches the binding.
/// </remarks>
public sealed class ScrubbyNumberBox : Border
{
    /// <summary>The value (two way by default).</summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ScrubbyNumberBox, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>The smallest value.</summary>
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<ScrubbyNumberBox, double>(nameof(Minimum), -1e9);

    /// <summary>The largest value.</summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ScrubbyNumberBox, double>(nameof(Maximum), 1e9);

    /// <summary>The value change per dragged pixel before modifiers.</summary>
    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<ScrubbyNumberBox, double>(nameof(Step), 1);

    /// <summary>The number of decimals shown and kept.</summary>
    public static readonly StyledProperty<int> DecimalsProperty =
        AvaloniaProperty.Register<ScrubbyNumberBox, int>(nameof(Decimals));

    /// <summary>The unit suffix, for example <c>%</c>, <c>dB</c> or <c>px</c>.</summary>
    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<ScrubbyNumberBox, string?>(nameof(Unit));

    /// <summary>The value a right click restores; NaN disables the reset.</summary>
    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<ScrubbyNumberBox, double>(nameof(DefaultValue), double.NaN);

    /// <summary>True when several selected items have different values; the box then shows a dash.</summary>
    public static readonly StyledProperty<bool> IsMixedProperty =
        AvaloniaProperty.Register<ScrubbyNumberBox, bool>(nameof(IsMixed));

    /// <summary>Executed when a drag starts (no parameter).</summary>
    public static readonly StyledProperty<ICommand?> DragStartedCommandProperty =
        AvaloniaProperty.Register<ScrubbyNumberBox, ICommand?>(nameof(DragStartedCommand));

    /// <summary>Executed when a drag ends, with a boxed <see cref="bool"/>: true when committed, false after Esc.</summary>
    public static readonly StyledProperty<ICommand?> DragCompletedCommandProperty =
        AvaloniaProperty.Register<ScrubbyNumberBox, ICommand?>(nameof(DragCompletedCommand));

    /// <summary>Raised whenever the user changes the value (drag, typing or reset).</summary>
    public static readonly RoutedEvent<RoutedEventArgs> ValueChangedEvent =
        RoutedEvent.Register<ScrubbyNumberBox, RoutedEventArgs>(nameof(ValueChanged), RoutingStrategies.Bubble);

    /// <summary>Raised when a drag starts (after the pointer moved a few pixels).</summary>
    public static readonly RoutedEvent<RoutedEventArgs> DragStartedEvent =
        RoutedEvent.Register<ScrubbyNumberBox, RoutedEventArgs>(nameof(DragStarted), RoutingStrategies.Bubble);

    /// <summary>Raised when a drag ends.</summary>
    public static readonly RoutedEvent<DragCompletedEventArgs> DragCompletedEvent =
        RoutedEvent.Register<ScrubbyNumberBox, DragCompletedEventArgs>(nameof(DragCompleted), RoutingStrategies.Bubble);

    private const double DragThreshold = 3;

    private readonly TextBlock _display;
    private readonly TextBox _editor;
    private bool _pressed;
    private bool _dragging;
    private bool _editing;
    private double _pressX;
    private double _lastX;
    private double _pressValue;
    private double _accumulated;

    static ScrubbyNumberBox()
    {
        FocusableProperty.OverrideDefaultValue<ScrubbyNumberBox>(true);
    }

    /// <summary>Creates the box.</summary>
    public ScrubbyNumberBox()
    {
        _display = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _editor = new TextBox
        {
            IsVisible = false,
            MinHeight = 0,
            Padding = new Thickness(5, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(0),
        };
        _editor.Classes.Add("scrubEditor");
        _editor.KeyDown += OnEditorKeyDown;
        _editor.LostFocus += (_, _) => CommitEdit();
        Child = new Panel { Children = { _display, _editor } };
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        UpdateDisplay();
    }

    /// <summary>Raised whenever the user changes the value.</summary>
    public event EventHandler<RoutedEventArgs>? ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    /// <summary>Raised when a drag starts.</summary>
    public event EventHandler<RoutedEventArgs>? DragStarted
    {
        add => AddHandler(DragStartedEvent, value);
        remove => RemoveHandler(DragStartedEvent, value);
    }

    /// <summary>Raised when a drag ends.</summary>
    public event EventHandler<DragCompletedEventArgs>? DragCompleted
    {
        add => AddHandler(DragCompletedEvent, value);
        remove => RemoveHandler(DragCompletedEvent, value);
    }

    /// <summary>The value.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The smallest value.</summary>
    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>The largest value.</summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>The change per dragged pixel before modifiers.</summary>
    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>The number of decimals.</summary>
    public int Decimals
    {
        get => GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    /// <summary>The unit suffix.</summary>
    public string? Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>The value a right click restores (NaN for none).</summary>
    public double DefaultValue
    {
        get => GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    /// <summary>True when the selection has different values.</summary>
    public bool IsMixed
    {
        get => GetValue(IsMixedProperty);
        set => SetValue(IsMixedProperty, value);
    }

    /// <summary>Executed when a drag starts.</summary>
    public ICommand? DragStartedCommand
    {
        get => GetValue(DragStartedCommandProperty);
        set => SetValue(DragStartedCommandProperty, value);
    }

    /// <summary>Executed when a drag ends with a boxed bool (true committed, false cancelled).</summary>
    public ICommand? DragCompletedCommand
    {
        get => GetValue(DragCompletedCommandProperty);
        set => SetValue(DragCompletedCommandProperty, value);
    }

    /// <summary>True while the text box is open.</summary>
    public bool IsEditing => _editing;

    /// <summary>
    /// Parses typed text with the invariant culture. A comma is accepted as the decimal separator (German keyboards);
    /// when both separators appear, the last one is the decimal separator and the other one groups thousands. A
    /// trailing unit (<c>%</c>, <c>dB</c>, <c>px</c>, <c>f</c>, ...) and a leading <c>+</c> are ignored.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The parsed value, or 0.</param>
    /// <returns>True when the text holds a finite number.</returns>
    public static bool TryParseValue(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        var end = s.Length;
        while (end > 0 && (char.IsLetter(s[end - 1]) || s[end - 1] is '%' or ' ' or '°'))
        {
            end--;
        }

        s = s[..end].Replace(" ", string.Empty, StringComparison.Ordinal).Replace("'", string.Empty, StringComparison.Ordinal);
        var comma = s.LastIndexOf(',');
        var dot = s.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
        {
            s = comma > dot
                ? s.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
                : s.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (comma >= 0)
        {
            s = s.Replace(',', '.');
        }

        if (!double.TryParse(
                s,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out var parsed)
            || !double.IsFinite(parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>Formats a value with a fixed number of decimals and invariant digits (never a negative zero).</summary>
    /// <param name="value">The value.</param>
    /// <param name="decimals">The decimals, 0 to 6.</param>
    /// <returns>For example <c>"1.50"</c>.</returns>
    public static string FormatValue(double value, int decimals)
    {
        var d = Math.Clamp(decimals, 0, 6);
        var rounded = Math.Round(value, d, MidpointRounding.AwayFromZero);
        if (rounded == 0)
        {
            rounded = 0;
        }

        return rounded.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a value with its unit as the box shows it, for example <c>"45%"</c> or <c>"-3.0 dB"</c>.</summary>
    /// <param name="value">The value (NaN shows a dash).</param>
    /// <param name="decimals">The decimals.</param>
    /// <param name="unit">The unit, or null.</param>
    /// <returns>The display text.</returns>
    public static string FormatDisplay(double value, int decimals, string? unit)
    {
        if (double.IsNaN(value))
        {
            return "–";
        }

        var number = FormatValue(value, decimals);
        if (string.IsNullOrEmpty(unit))
        {
            return number;
        }

        return char.IsLetter(unit[0]) ? number + " " + unit : number + unit;
    }

    /// <summary>Clamps a value to the range and rounds it to the decimals.</summary>
    /// <param name="value">The value.</param>
    /// <param name="minimum">The minimum.</param>
    /// <param name="maximum">The maximum.</param>
    /// <param name="decimals">The decimals.</param>
    /// <returns>The normalized value.</returns>
    public static double Normalize(double value, double minimum, double maximum, int decimals)
    {
        var clamped = Math.Clamp(value, Math.Min(minimum, maximum), Math.Max(minimum, maximum));
        return Math.Round(clamped, Math.Clamp(decimals, 0, 6), MidpointRounding.AwayFromZero);
    }

    /// <summary>The per-pixel multiplier of a drag: Shift 10x, Ctrl (or Cmd) 0.1x, else 1.</summary>
    /// <param name="modifiers">The held modifiers.</param>
    /// <returns>The multiplier.</returns>
    public static double DragFactor(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Shift) ? 10
        : modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta) ? 0.1
        : 1;

    /// <summary>
    /// The value after a drag of <paramref name="deltaPixels"/> from <paramref name="start"/>: the pixels times the step
    /// and the modifier factor, clamped and rounded.
    /// </summary>
    /// <param name="start">The value at the press (NaN counts as the minimum clamped to 0).</param>
    /// <param name="deltaPixels">The horizontal movement in pixels.</param>
    /// <param name="step">The change per pixel.</param>
    /// <param name="modifiers">The held modifiers.</param>
    /// <param name="minimum">The minimum.</param>
    /// <param name="maximum">The maximum.</param>
    /// <param name="decimals">The decimals.</param>
    /// <returns>The new value.</returns>
    public static double ScrubValue(
        double start, double deltaPixels, double step, KeyModifiers modifiers, double minimum, double maximum, int decimals)
    {
        var origin = double.IsNaN(start) ? Math.Clamp(0, Math.Min(minimum, maximum), Math.Max(minimum, maximum)) : start;
        return Normalize(origin + (deltaPixels * step * DragFactor(modifiers)), minimum, maximum, decimals);
    }

    /// <summary>Opens the text box (double click or Enter).</summary>
    public void BeginEdit()
    {
        if (_editing)
        {
            return;
        }

        _editing = true;
        _editor.Text = IsMixed || double.IsNaN(Value) ? string.Empty : FormatValue(Value, Decimals);
        _display.IsVisible = false;
        _editor.IsVisible = true;
        _editor.Focus();
        _editor.SelectAll();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == DecimalsProperty || change.Property == UnitProperty
            || change.Property == IsMixedProperty)
        {
            UpdateDisplay();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_editing || !IsEffectivelyEnabled)
        {
            return;
        }

        if (_pressed)
        {
            // Another button pressed during a left-button press or drag: neither a reset nor a new press.
            e.Handled = true;
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            ResetToDefault();
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            EndPress(commit: true);
            BeginEdit();
            e.Handled = true;
            return;
        }

        _pressed = true;
        _dragging = false;
        _pressX = point.Position.X;
        _lastX = _pressX;
        _pressValue = Value;
        _accumulated = ScrubValue(Value, 0, Step, KeyModifiers.None, Minimum, Maximum, 6);
        e.Pointer.Capture(this);
        Focus();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pressed)
        {
            return;
        }

        var x = e.GetPosition(this).X;
        if (!_dragging)
        {
            if (Math.Abs(x - _pressX) < DragThreshold)
            {
                return;
            }

            _dragging = true;
            _lastX = _pressX;
            RaiseEvent(new RoutedEventArgs(DragStartedEvent, this));
            if (DragStartedCommand is { } started && started.CanExecute(null))
            {
                started.Execute(null);
            }
        }

        // Incremental, so pressing or releasing Shift or Ctrl mid-drag changes the speed from here on.
        _accumulated = ScrubValue(_accumulated, x - _lastX, Step, e.KeyModifiers, Minimum, Maximum, 6);
        _lastX = x;
        SetUserValue(Normalize(_accumulated, Minimum, Maximum, Decimals));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        // A press starts only with the left button; releasing another one must not commit the running drag.
        if (_pressed && e.InitialPressMouseButton == MouseButton.Left)
        {
            e.Pointer.Capture(null);
            EndPress(commit: true);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndPress(commit: true);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _editing)
        {
            return;
        }

        if (e.Key == Key.Escape && _dragging)
        {
            var start = _pressValue;
            EndPress(commit: false);
            SetCurrentValue(ValueProperty, start);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && IsEffectivelyEnabled)
        {
            BeginEdit();
            e.Handled = true;
        }
    }

    private void EndPress(bool commit)
    {
        var wasDragging = _dragging;
        _pressed = false;
        _dragging = false;
        if (!wasDragging)
        {
            return;
        }

        RaiseEvent(new DragCompletedEventArgs(DragCompletedEvent, this, commit));
        if (DragCompletedCommand is { } completed && completed.CanExecute(commit))
        {
            completed.Execute(commit);
        }
    }

    private void ResetToDefault()
    {
        if (!double.IsNaN(DefaultValue))
        {
            SetUserValue(Normalize(DefaultValue, Minimum, Maximum, Decimals));
        }
    }

    private void SetUserValue(double value)
    {
        if (value.Equals(Value))
        {
            return;
        }

        SetCurrentValue(ValueProperty, value);
        RaiseEvent(new RoutedEventArgs(ValueChangedEvent, this));
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitEdit();
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseEditor();
            Focus();
            e.Handled = true;
        }
    }

    private void CommitEdit()
    {
        if (!_editing)
        {
            return;
        }

        var text = _editor.Text;
        CloseEditor();
        if (TryParseValue(text, out var parsed))
        {
            SetUserValue(Normalize(parsed, Minimum, Maximum, Decimals));
        }
    }

    private void CloseEditor()
    {
        _editing = false;
        _editor.IsVisible = false;
        _display.IsVisible = true;
    }

    private void UpdateDisplay() =>
        _display.Text = IsMixed ? "–" : FormatDisplay(Value, Decimals, Unit);

    /// <summary>Arguments of <see cref="DragCompletedEvent"/>.</summary>
    public sealed class DragCompletedEventArgs : RoutedEventArgs
    {
        /// <summary>Creates the arguments.</summary>
        /// <param name="routedEvent">The event.</param>
        /// <param name="source">The box.</param>
        /// <param name="committed">True when the drag committed, false after Esc.</param>
        public DragCompletedEventArgs(RoutedEvent routedEvent, object source, bool committed)
            : base(routedEvent, source)
        {
            Committed = committed;
        }

        /// <summary>True when the drag committed, false after Esc.</summary>
        public bool Committed { get; }
    }
}
