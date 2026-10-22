using Cutlass.Media.Parsing;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Parsing;

/// <summary>ProgressParser: split reads, trimmed values, N/A, out_time_ms and the end block (TECH_BRIEF 9).</summary>
[UseCulture("de-DE")]
public sealed class ProgressParserTests
{
    private const string TwoBlocks =
        "frame=50\nfps=0.00\nstream_0_0_q=2.0\nbitrate=N/A\ntotal_size=N/A\nout_time_us=2000000\nout_time_ms=2000000\n" +
        "out_time=00:00:02.000000\ndup_frames=0\ndrop_frames=0\nspeed= 5.7x\nprogress=continue\n" +
        "frame=100\nout_time_us=4000000\nout_time_ms=4000000\nspeed=6.25x\nprogress=end\n";

    [Fact]
    public void WholeTextGivesBothBlocks()
    {
        var parser = new ProgressParser();

        var blocks = parser.Feed(TwoBlocks);

        Assert.Equal(
            new[]
            {
                new ProgressBlock(2_000_000, 50, 5.7, IsEnd: false),
                new ProgressBlock(4_000_000, 100, 6.25, IsEnd: true),
            },
            blocks);
        Assert.Equal(blocks[^1], parser.Last);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(64)]
    public void SplitReadsGiveTheSameBlocks(int chunkSize)
    {
        var parser = new ProgressParser();
        var blocks = new List<ProgressBlock>();
        var text = TwoBlocks.Replace("\n", "\r\n", StringComparison.Ordinal);

        for (var i = 0; i < text.Length; i += chunkSize)
        {
            blocks.AddRange(parser.Feed(text.AsSpan(i, Math.Min(chunkSize, text.Length - i))));
        }

        Assert.Equal(2, blocks.Count);
        Assert.Equal(new ProgressBlock(2_000_000, 50, 5.7, IsEnd: false), blocks[0]);
        Assert.True(blocks[1].IsEnd);
    }

    [Fact]
    public void NotAvailableValuesAreSkipped()
    {
        var parser = new ProgressParser();

        var block = Assert.Single(parser.Feed("frame=0\nout_time_us=N/A\nout_time_ms=N/A\nspeed=N/A\nprogress=continue\n"));

        Assert.Equal(new ProgressBlock(null, 0, null, IsEnd: false), block);
    }

    [Fact]
    public void OutTimeMsIsMicrosecondsToo()
    {
        var parser = new ProgressParser();

        Assert.Null(parser.FeedLine("out_time_ms=1500000"));
        var block = parser.FeedLine("progress=continue");

        Assert.Equal(1_500_000, block!.OutTimeMicros);
    }

    [Fact]
    public void ValuesDoNotLeakIntoTheNextBlock()
    {
        var parser = new ProgressParser();
        parser.Feed("out_time_us=10\nspeed=2x\nprogress=continue\n");

        var block = Assert.Single(parser.Feed("frame=3\nprogress=continue\n"));

        Assert.Equal(new ProgressBlock(null, 3, null, IsEnd: false), block);
    }

    [Theory]
    [InlineData(0, 1_000_000, 0.0)]
    [InlineData(500_000, 1_000_000, 0.5)]
    [InlineData(2_000_000, 1_000_000, 1.0)]
    [InlineData(-5, 1_000_000, 0.0)]
    [InlineData(5, 0, 0.0)]
    public void FractionIsClamped(long outTime, long total, double expected)
    {
        Assert.Equal(expected, ProgressParser.Fraction(outTime, total), 6);
    }
}
