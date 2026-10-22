using Avalonia.Input;
using Cutlass.App.Controls;
using Cutlass.App.ViewModels;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.App;

[UseCulture("de-DE")]
public sealed class InspectorScrubbyNumberBoxTests
{
    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("1,5", 1.5)]
    [InlineData(" -0,25 ", -0.25)]
    [InlineData("+3", 3)]
    [InlineData("1.234,5", 1234.5)]
    [InlineData("1,234.5", 1234.5)]
    [InlineData("45%", 45)]
    [InlineData("-6 dB", -6)]
    [InlineData("12 px", 12)]
    [InlineData("24f", 24)]
    [InlineData("1e2", 100)]
    public void ParsesInvariantAndCommaDecimals(string text, double expected)
    {
        Assert.True(ScrubbyNumberBox.TryParseValue(text, out var value));
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1..2")]
    [InlineData("--1")]
    public void RejectsGarbage(string? text) => Assert.False(ScrubbyNumberBox.TryParseValue(text, out _));

    [Fact]
    public void FormatsWithInvariantDigits()
    {
        Assert.Equal("1.50", ScrubbyNumberBox.FormatValue(1.5, 2));
        Assert.Equal("1235", ScrubbyNumberBox.FormatValue(1234.5, 0));
        Assert.Equal("0", ScrubbyNumberBox.FormatValue(-0.0001, 0));
        Assert.Equal("-2.3", ScrubbyNumberBox.FormatValue(-2.25, 1));
        Assert.Equal("45%", ScrubbyNumberBox.FormatDisplay(45, 0, "%"));
        Assert.Equal("-6.0 dB", ScrubbyNumberBox.FormatDisplay(-6, 1, "dB"));
        Assert.Equal("0.25", ScrubbyNumberBox.FormatDisplay(0.25, 2, null));
        Assert.Equal("–", ScrubbyNumberBox.FormatDisplay(double.NaN, 2, "%"));
    }

    [Fact]
    public void FormatAndParseRoundTrip()
    {
        foreach (var value in new[] { 0.0, 1.25, -3.5, 99.99, 12345.678 })
        {
            var text = ScrubbyNumberBox.FormatValue(value, 3);
            Assert.DoesNotContain(",", text, StringComparison.Ordinal);
            Assert.True(ScrubbyNumberBox.TryParseValue(text, out var parsed));
            Assert.Equal(value, parsed, 3);
        }
    }

    [Fact]
    public void NormalizeClampsAndRounds()
    {
        Assert.Equal(3, ScrubbyNumberBox.Normalize(7.2, 0, 3, 2));
        Assert.Equal(0.1, ScrubbyNumberBox.Normalize(-5, 0.1, 4, 2));
        Assert.Equal(1.24, ScrubbyNumberBox.Normalize(1.2351, 0, 4, 2));
        Assert.Equal(13, ScrubbyNumberBox.Normalize(12.5, 0, 100, 0));
    }

    [Fact]
    public void DragUsesStepAndModifiers()
    {
        Assert.Equal(1, ScrubbyNumberBox.DragFactor(KeyModifiers.None));
        Assert.Equal(10, ScrubbyNumberBox.DragFactor(KeyModifiers.Shift));
        Assert.Equal(0.1, ScrubbyNumberBox.DragFactor(KeyModifiers.Control));
        Assert.Equal(60, ScrubbyNumberBox.ScrubValue(50, 10, 1, KeyModifiers.None, 0, 100, 0));
        Assert.Equal(100, ScrubbyNumberBox.ScrubValue(50, 10, 1, KeyModifiers.Shift, 0, 100, 0));
        Assert.Equal(51, ScrubbyNumberBox.ScrubValue(50, 10, 1, KeyModifiers.Control, 0, 100, 0));
        Assert.Equal(1.2, ScrubbyNumberBox.ScrubValue(1, 20, 0.01, KeyModifiers.None, 0, 3, 2), 9);
        Assert.Equal(0, ScrubbyNumberBox.ScrubValue(1, -500, 0.01, KeyModifiers.None, 0, 3, 2));
        Assert.Equal(5, ScrubbyNumberBox.ScrubValue(double.NaN, 5, 1, KeyModifiers.None, 0, 100, 0));
    }

    [Fact]
    public void ColorFieldNormalizesTypedColors()
    {
        Assert.Equal("#FF0000FF", InspectorColorField.Normalize("#f00"));
        Assert.Equal("#11223344", InspectorColorField.Normalize("11223344"));
        Assert.Equal("#ABCDEFFF", InspectorColorField.Normalize(" #abcdef "));
        Assert.Null(InspectorColorField.Normalize("#12345"));
        Assert.Null(InspectorColorField.Normalize("#GG0000"));
        var color = InspectorColorField.ToColor("#11223344");
        Assert.Equal(0x44, color.A);
        Assert.Equal(0x11, color.R);
        Assert.Equal(0x22, color.G);
        Assert.Equal(0x33, color.B);
    }
}
