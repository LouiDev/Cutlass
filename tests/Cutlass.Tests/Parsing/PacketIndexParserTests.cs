using Cutlass.Media.Parsing;

namespace Cutlass.Tests.Parsing;

/// <summary>PacketIndexParser: N/A pts, discard flags, sorting, duplicates and the 5% rule (TECH_BRIEF 6.4).</summary>
public sealed class PacketIndexParserTests
{
    [Fact]
    public void SortsDeduplicatesAndUsesDtsForMissingPts()
    {
        const string csv = "0,-1024,K__\r\n2048,-512,___\r\n1024,0,___\r\nN/A,512,___\r\n1536,1024,___\r\n1024,1536,___\r\n";

        Assert.Equal(new long[] { 0, 512, 1024, 1536, 2048 }, PacketIndexParser.Parse(csv));
    }

    [Fact]
    public void ManyPacketsWithoutPtsGiveNoIndex()
    {
        // AVI mpeg4 with two B-frames: every third packet has no pts, and its dts is not the decoded timestamp.
        var lines = Enumerable.Range(0, 30).Select(i => (i % 3 == 0 ? "N/A" : i.ToString(System.Globalization.CultureInfo.InvariantCulture)) + $",{i},___");
        var few = Enumerable.Range(0, 30).Select(i => (i is 0 or 7 or 9 or 20 ? "N/A" : i.ToString(System.Globalization.CultureInfo.InvariantCulture)) + $",{i},___");

        Assert.Null(PacketIndexParser.Parse(string.Join('\n', lines)));
        Assert.Equal(30, PacketIndexParser.Parse(string.Join('\n', few))!.Length);
    }

    [Fact]
    public void DiscardedPacketsAreDroppedWithoutCountingAsUnusable()
    {
        var lines = Enumerable.Range(0, 10).Select(i => $"{i * 40},{i * 40},{(i < 5 ? "K_D" : "___")}");

        Assert.Equal(new long[] { 200, 240, 280, 320, 360 }, PacketIndexParser.Parse(string.Join('\n', lines)));
    }

    [Fact]
    public void UpToFivePercentUnusableIsTolerated()
    {
        var lines = Enumerable.Range(0, 100).Select(i => i < 5 ? "N/A,N/A,K__" : $"{i},{i},K__");

        var index = PacketIndexParser.Parse(string.Join('\n', lines));

        Assert.NotNull(index);
        Assert.Equal(95, index.Length);
    }

    [Fact]
    public void MoreThanFivePercentUnusableGivesNoIndex()
    {
        var lines = Enumerable.Range(0, 100).Select(i => i < 6 ? "N/A,N/A,K__" : $"{i},{i},K__");

        Assert.Null(PacketIndexParser.Parse(string.Join('\n', lines)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("0,0,K_D\n")]
    public void NothingUsableGivesNoIndex(string? csv)
    {
        Assert.Null(PacketIndexParser.Parse(csv));
    }

    [Fact]
    public void MalformedLinesCountAsUnusable()
    {
        Assert.Null(PacketIndexParser.Parse("0,0,K__\ngarbage\n"));
    }

    [Fact]
    public void ArgumentsMatchTheBrief()
    {
        Assert.Equal(
            new[] { "-v", "error", "-select_streams", "0", "-show_entries", "packet=pts,dts,flags", "-of", "csv=p=0", "/m/a b.mp4" },
            PacketIndexParser.BuildArguments(0, "/m/a b.mp4"));
    }
}
