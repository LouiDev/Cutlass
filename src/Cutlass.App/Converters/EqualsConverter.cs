using System.Globalization;
using Avalonia.Data.Converters;

namespace Cutlass.App.Converters;

/// <summary>
/// True when the bound value equals the converter parameter, for example the active tool against
/// <c>{x:Static vm:EditTool.Razor}</c>. One way only.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static EqualsConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value, parameter);

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("EqualsConverter is one way.");
}
