using System.Buffers;
using Cutlass.Media.Preview;

namespace Cutlass.Tests.Media.Preview;

/// <summary>The frame buffer rules: pooled frames return once, cached frames own their arrays, the LRU evicts.</summary>
public sealed class PreviewBuffersTests
{
    [Fact]
    public void ReturnIsIdempotentForPooledAndUnpooledFrames()
    {
        var pooled = new VideoFrame(ArrayPool<byte>.Shared.Rent(64), 4, 4, 16, 3, 9, pooled: true);
        var owned = new VideoFrame { Buffer = new byte[64], Width = 4, Height = 4, Stride = 16, Pooled = false };

        Assert.False(pooled.IsReturned);
        pooled.Return();
        pooled.Return();
        owned.Return();

        Assert.True(pooled.IsReturned);
        Assert.True(owned.IsReturned);
        Assert.Equal(3, pooled.TimelineFrame);
        Assert.Equal(9, pooled.RequestId);
    }

    [Fact]
    public void TheFrameCacheEvictsTheLeastRecentlyUsedFrameByBytes()
    {
        var cache = new FrameCache(maxBytes: 30);
        cache.Add("a", new byte[10]);
        cache.Add("b", new byte[10]);
        cache.Add("c", new byte[10]);
        Assert.True(cache.TryGet("a", out _));

        cache.Add("d", new byte[10]);

        Assert.Equal(3, cache.Count);
        Assert.Equal(30, cache.Bytes);
        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.Contains("c"));
        Assert.True(cache.TryGet("d", out var d));
        Assert.Equal(10, d.Length);
        Assert.Equal(3, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void TheFrameCacheSkipsOversizedFramesAndReplacesKeys()
    {
        var cache = new FrameCache(maxBytes: 100);
        cache.Add("a", new byte[40]);
        cache.Add("b", new byte[40]);
        cache.Add("c", new byte[40]);
        cache.Add("huge", new byte[101]);

        Assert.Equal(2, cache.Count);
        Assert.Equal(80, cache.Bytes);
        Assert.False(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("huge", out _));

        cache.Add("b", new byte[20]);
        Assert.Equal(60, cache.Bytes);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
    }

    [Fact]
    public void TheDefaultFrameCacheHolds512Megabytes()
    {
        Assert.Equal(512L * 1024 * 1024, new FrameCache().MaxBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameCache(0));
    }
}
