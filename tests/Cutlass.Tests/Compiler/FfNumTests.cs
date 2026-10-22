using Cutlass.Compiler.Graph;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Compiler;

[UseCulture("de-DE")]
public sealed class FfNumTests
{
    [Theory]
    [InlineData(0, "0.000000")]
    [InlineData(1, "0.000001")]
    [InlineData(12_345_678, "12.345678")]
    [InlineData(1_000_000, "1.000000")]
    [InlineData(-500_000, "-0.500000")]
    [InlineData(-1, "-0.000001")]
    [InlineData(long.MinValue, "-9223372036854.775808")]
    public void SecondsFromMicros(long micros, string expected)
    {
        Assert.Equal(expected, FfNum.Seconds(micros));
    }

    [Fact]
    public void SecondsFromRationalFloorsToTheMicrosecond()
    {
        Assert.Equal("0.033366", FfNum.Seconds(new Rational(1001, 30000)));
        Assert.Equal("0.333333", FfNum.Seconds(new Rational(1, 3)));
        Assert.Equal("-0.333334", FfNum.Seconds(new Rational(-1, 3)));
    }

    [Theory]
    [InlineData(1.5, "1.500000")]
    [InlineData(0.0000004, "0.000000")]
    [InlineData(-0.0000004, "0.000000")]
    [InlineData(-2.25, "-2.250000")]
    [InlineData(1234567.1234567, "1234567.123457")]
    public void DecimalUsesF6Invariant(double value, string expected)
    {
        Assert.Equal(expected, FfNum.Decimal(value));
    }

    [Theory]
    [InlineData(1.0, "1")]
    [InlineData(0.5, "0.5")]
    [InlineData(0.6, "0.6")]
    [InlineData(1.0 / 3.0, "0.333")]
    [InlineData(0.0005, "0.001")]
    [InlineData(0.0, "0")]
    [InlineData(-0.0001, "0")]
    public void AlphaTrimsToThreeDecimals(double value, string expected)
    {
        Assert.Equal(expected, FfNum.Alpha(value));
    }

    [Fact]
    public void RatesAndIntegers()
    {
        Assert.Equal("30000/1001", FfNum.Rate(FrameRate.Fps2997));
        Assert.Equal("25", FfNum.Rate(FrameRate.Fps25));
        Assert.Equal("24000/1001", FfNum.Rate(new Rational(48000, 2002)));
        Assert.Equal("1234567", FfNum.Integer(1_234_567));
        Assert.Equal("-12", FfNum.Integer(-12));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsNonFiniteValues(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FfNum.Decimal(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => FfNum.Alpha(value));
    }
}
