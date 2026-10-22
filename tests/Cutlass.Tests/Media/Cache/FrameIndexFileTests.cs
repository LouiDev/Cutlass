using Cutlass.Compiler;
using Cutlass.Core.Time;
using Cutlass.Media.Services;

namespace Cutlass.Tests.Media.Cache;

/// <summary>The <c>frames.idx</c> format (TECH_BRIEF 6.1): <c>"CTLX" u16 ver, i64 tbNum, i64 tbDen, i32 count, i64 pts[count]</c>.</summary>
public sealed class FrameIndexFileTests
{
    [Fact]
    public void AnIndexRoundTripsWithTheDocumentedLayout()
    {
        var index = new FrameIndex(new Rational(1, 12800), [-1024, 0, 512, 1024, 1_000_000_000_000]);
        using var stream = new MemoryStream();
        FrameIndexService.WriteIndex(stream, index.TimeBase, index);
        var bytes = stream.ToArray();

        Assert.Equal(26 + (8 * 5), bytes.Length);
        Assert.Equal("CTLX"u8.ToArray(), bytes[..4]);
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 4));
        Assert.Equal(1L, BitConverter.ToInt64(bytes, 6));
        Assert.Equal(12800L, BitConverter.ToInt64(bytes, 14));
        Assert.Equal(5, BitConverter.ToInt32(bytes, 22));
        Assert.Equal(-1024L, BitConverter.ToInt64(bytes, 26));

        stream.Position = 0;
        Assert.True(FrameIndexService.TryReadIndex(stream, out var timeBase, out var read));
        Assert.Equal(index.TimeBase, timeBase);
        Assert.NotNull(read);
        Assert.Equal(index.Pts.ToArray(), read.Pts.ToArray());
    }

    [Fact]
    public void ACountOfZeroRecordsNoUsableIndex()
    {
        using var stream = new MemoryStream();
        FrameIndexService.WriteIndex(stream, new Rational(1, 1000), null);
        stream.Position = 0;

        Assert.True(FrameIndexService.TryReadIndex(stream, out var timeBase, out var index));
        Assert.Equal(new Rational(1, 1000), timeBase);
        Assert.Null(index);
    }

    [Fact]
    public void CorruptDataIsRejected()
    {
        var index = new FrameIndex(new Rational(1, 25), [0, 1, 2]);
        using var stream = new MemoryStream();
        FrameIndexService.WriteIndex(stream, index.TimeBase, index);
        var good = stream.ToArray();

        Assert.False(FrameIndexService.TryReadIndex(new MemoryStream(good[..^1]), out _, out _));
        var wrongMagic = (byte[])good.Clone();
        wrongMagic[0] = (byte)'X';
        Assert.False(FrameIndexService.TryReadIndex(new MemoryStream(wrongMagic), out _, out _));
        var wrongVersion = (byte[])good.Clone();
        wrongVersion[4] = 9;
        Assert.False(FrameIndexService.TryReadIndex(new MemoryStream(wrongVersion), out _, out _));
        var descending = (byte[])good.Clone();
        BitConverter.GetBytes(5L).CopyTo(descending, 26);
        Assert.False(FrameIndexService.TryReadIndex(new MemoryStream(descending), out _, out _));
        Assert.False(FrameIndexService.TryReadIndexFile(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".idx"), out _, out _));
    }
}
