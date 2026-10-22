using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Cutlass.Media.Preview;

/// <summary>
/// One BGRA frame (TECH_BRIEF 8.1). Row <c>y</c> starts at <c>y * Stride</c> in <see cref="Buffer"/>, which may be
/// longer than <c>Stride * Height</c> when it was rented from <see cref="ArrayPool{T}.Shared"/>. ffmpeg's bgra alpha
/// is always 255, so the pixels are valid premultiplied BGRA as well.
/// </summary>
public sealed class VideoFrame
{
    private int _returned;

    /// <summary>Creates an empty frame for object initializers.</summary>
    public VideoFrame()
    {
    }

    /// <summary>Creates a frame.</summary>
    /// <param name="buffer">The pixels.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="stride">The bytes per row.</param>
    /// <param name="timelineFrame">The timeline frame shown.</param>
    /// <param name="requestId">The id of the request that produced it.</param>
    /// <param name="pooled">True when <paramref name="buffer"/> was rented from <see cref="ArrayPool{T}.Shared"/>.</param>
    [SetsRequiredMembers]
    public VideoFrame(byte[] buffer, int width, int height, int stride, long timelineFrame, long requestId, bool pooled)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Buffer = buffer;
        Width = width;
        Height = height;
        Stride = stride;
        TimelineFrame = timelineFrame;
        RequestId = requestId;
        Pooled = pooled;
    }

    /// <summary>The pixels, <see cref="Stride"/> bytes per row. Never write into it.</summary>
    public required byte[] Buffer { get; init; }

    /// <summary>The width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>The height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>The bytes per row (<c>Width * 4</c> for frames from ffmpeg).</summary>
    public required int Stride { get; init; }

    /// <summary>The timeline frame this picture shows.</summary>
    public long TimelineFrame { get; init; }

    /// <summary>The <see cref="FrameRequest.RequestId"/> that produced the frame.</summary>
    public long RequestId { get; init; }

    /// <summary>
    /// True when <see cref="Buffer"/> is rented from <see cref="ArrayPool{T}.Shared"/> and goes back on
    /// <see cref="Return"/>; false for cached frames, which own their arrays.
    /// </summary>
    public bool Pooled { get; init; }

    /// <summary>True once <see cref="Return"/> was called.</summary>
    public bool IsReturned => Volatile.Read(ref _returned) != 0;

    /// <summary>
    /// Releases the frame: a pooled buffer goes back to the pool, an unpooled one is left to its owner. Safe to call
    /// more than once and from any thread; the buffer must not be read afterwards.
    /// </summary>
    public void Return()
    {
        if (Interlocked.Exchange(ref _returned, 1) != 0)
        {
            return;
        }

        if (Pooled)
        {
            ArrayPool<byte>.Shared.Return(Buffer);
        }
    }
}
