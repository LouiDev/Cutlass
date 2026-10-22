using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Time;

public sealed class RationalTests
{
    [Theory]
    [InlineData(2, 4, 1, 2)]
    [InlineData(3, -6, -1, 2)]
    [InlineData(-3, -6, 1, 2)]
    [InlineData(0, 7, 0, 1)]
    [InlineData(30000, 1001, 30000, 1001)]
    [InlineData(60, 2, 30, 1)]
    public void ConstructorNormalizes(long n, long d, long expectedNum, long expectedDen)
    {
        var r = new Rational(n, d);

        Assert.Equal(expectedNum, r.Num);
        Assert.Equal(expectedDen, r.Den);
        Assert.Equal(r, Rational.Create(n, d));
    }

    [Fact]
    public void EqualValuesAreEqualAfterNormalization()
    {
        Assert.Equal(new Rational(1, 2), new Rational(50, 100));
        Assert.Equal(new Rational(1, 2).GetHashCode(), new Rational(50, 100).GetHashCode());
    }

    [Fact]
    public void ZeroDenominatorThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rational.Create(1, 0));
    }

    [Theory]
    [InlineData("30000/1001", 30000, 1001)]
    [InlineData("30", 30, 1)]
    [InlineData("1:1", 1, 1)]
    [InlineData("16:9", 16, 9)]
    [InlineData(" 1/90000 ", 1, 90000)]
    [InlineData("-5/10", -1, 2)]
    [InlineData("4/2", 2, 1)]
    public void ParsesFfprobeForms(string text, long num, long den)
    {
        var r = Rational.Parse(text);

        Assert.Equal(num, r.Num);
        Assert.Equal(den, r.Den);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0/0")]
    [InlineData("1/0")]
    [InlineData("abc")]
    [InlineData("1/2/3")]
    [InlineData("29.97")]
    [InlineData("1/")]
    [InlineData("/2")]
    public void RejectsInvalidText(string text)
    {
        Assert.False(Rational.TryParse(text, out var value));
        Assert.Equal(Rational.Zero, value);
        Assert.Throws<FormatException>(() => Rational.Parse(text));
    }

    [Fact]
    public void ComparesExactlyBeyondDoublePrecision()
    {
        // Both values are 1 + 1e-18 apart, which doubles cannot distinguish.
        var a = new Rational(1_000_000_000_000_000_001, 1_000_000_000_000_000_000);
        var b = Rational.One;

        Assert.Equal(a.ToDouble(), b.ToDouble());
        Assert.True(a > b);
        Assert.True(b < a);
        Assert.Equal(1, a.CompareTo(b));
        Assert.Equal(0, new Rational(2, 4).CompareTo(new Rational(1, 2)));
        Assert.True(new Rational(-1, 2) < Rational.Zero);
    }

    [Fact]
    [UseCulture("de-DE")]
    public void FormatsForFfmpegWithInvariantCulture()
    {
        Assert.Equal("30000/1001", new Rational(30000, 1001).ToFfString());
        Assert.Equal("30", new Rational(30, 1).ToFfString());
        Assert.Equal("-1/2", new Rational(1, -2).ToFfString());
        Assert.Equal("1234567", new Rational(1234567, 1).ToString());
    }
}
