using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Time;

public sealed class FrameRateTests
{
    public static TheoryData<long, long, long> FlicksPerFrameCases => new()
    {
        { 24000, 1001, 29_429_400 },
        { 24, 1, 29_400_000 },
        { 25, 1, 28_224_000 },
        { 30000, 1001, 23_543_520 },
        { 30, 1, 23_520_000 },
        { 50, 1, 14_112_000 },
        { 60000, 1001, 11_771_760 },
        { 60, 1, 11_760_000 },
    };

    [Theory]
    [MemberData(nameof(FlicksPerFrameCases))]
    public void FlicksPerFrameIsExactForEverySupportedRate(long num, long den, long expected)
    {
        var rate = new FrameRate(new Rational(num, den));

        Assert.Equal(expected, rate.FlicksPerFrame);
        Assert.True(rate.IsSupported);

        // Exact: FlicksPerFrame * num == FlicksPerSecond * den with no remainder.
        Assert.Equal((Int128)TimeMath.FlicksPerSecond * den, (Int128)rate.FlicksPerFrame * num);
    }

    [Fact]
    public void SupportedListsTheEightRatesInAscendingOrder()
    {
        Assert.Equal(8, FrameRate.Supported.Length);
        for (var i = 1; i < FrameRate.Supported.Length; i++)
        {
            Assert.True(FrameRate.Supported[i - 1].Value < FrameRate.Supported[i].Value);
        }
    }

    [Theory]
    [InlineData("2997/100", "30000/1001")]
    [InlineData("30000/1001", "30000/1001")]
    [InlineData("30", "30")]
    [InlineData("2398/100", "24000/1001")]
    [InlineData("24", "24")]
    [InlineData("25", "25")]
    [InlineData("12", "24000/1001")]
    [InlineData("1000", "60")]
    [InlineData("5994/100", "60000/1001")]
    [InlineData("48", "50")]
    [InlineData("90000/3003", "30000/1001")]
    public void NearestPicksTheClosestSupportedRate(string measured, string expected)
    {
        Assert.Equal(Rational.Parse(expected), FrameRate.Nearest(Rational.Parse(measured)).Value);
    }

    [Fact]
    public void NearestFallsBackTo30ForMissingRates()
    {
        Assert.Equal(FrameRate.Fps30, FrameRate.Nearest(Rational.Zero));
        Assert.Equal(FrameRate.Fps30, FrameRate.Nearest(new Rational(-25, 1)));
        Assert.Equal(FrameRate.Fps30, FrameRate.Nearest(default));
    }

    [Theory]
    [InlineData(24000, 1001, 24)]
    [InlineData(25, 1, 25)]
    [InlineData(30000, 1001, 30)]
    [InlineData(60000, 1001, 60)]
    [InlineData(50, 1, 50)]
    public void TimecodeBaseRoundsToNominalRate(long num, long den, int expected)
    {
        Assert.Equal(expected, new FrameRate(new Rational(num, den)).TimecodeBase);
    }

    [Fact]
    [UseCulture("de-DE")]
    public void DisplayNameUsesInvariantDecimals()
    {
        Assert.Equal("23.976", FrameRate.Fps23976.DisplayName);
        Assert.Equal("29.97", FrameRate.Fps2997.DisplayName);
        Assert.Equal("59.94", FrameRate.Fps5994.DisplayName);
        Assert.Equal("25", FrameRate.Fps25.DisplayName);
        Assert.Equal("30000/1001", FrameRate.Fps2997.ToString());
    }

    [Fact]
    public void UnsupportedRateIsReportedAsSuch()
    {
        Assert.False(new FrameRate(new Rational(12, 1)).IsSupported);
    }
}
