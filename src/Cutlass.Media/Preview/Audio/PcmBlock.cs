using System.Buffers;
using System.Runtime.InteropServices;

namespace Cutlass.Media.Preview.Audio;

/// <summary>
/// Up to one output buffer of interleaved s16le PCM read from the playback audio windows (TECH_BRIEF 8.2). The bytes
/// come from <see cref="ArrayPool{T}.Shared"/> and go back on <see cref="Return"/>.
/// </summary>
internal sealed class PcmBlock
{
    private int _returned;

    /// <summary>Rents a block.</summary>
    /// <param name="capacityFrames">The frames the block can hold.</param>
    /// <param name="channels">The interleaved channel count.</param>
    /// <param name="premixed">True when the channels are a Mixed stereo pair (the fallback).</param>
    public PcmBlock(int capacityFrames, int channels, bool premixed)
    {
        CapacityFrames = capacityFrames;
        Channels = channels;
        Premixed = premixed;
        Bytes = ArrayPool<byte>.Shared.Rent(capacityFrames * channels * sizeof(short));
    }

    /// <summary>The pooled bytes.</summary>
    public byte[] Bytes { get; }

    /// <summary>The frames the block can hold.</summary>
    public int CapacityFrames { get; }

    /// <summary>The interleaved channel count.</summary>
    public int Channels { get; }

    /// <summary>True when the input is a Mixed stereo pair.</summary>
    public bool Premixed { get; }

    /// <summary>The frames filled so far.</summary>
    public int Frames { get; set; }

    /// <summary>True when no more frames fit.</summary>
    public bool IsFull => Frames >= CapacityFrames;

    /// <summary>The filled samples.</summary>
    public ReadOnlySpan<short> Samples => MemoryMarshal.Cast<byte, short>(Bytes.AsSpan(0, Frames * Channels * sizeof(short)));

    /// <summary>The free part of the block, as bytes.</summary>
    /// <param name="frames">How many frames to expose.</param>
    /// <returns>The writable memory.</returns>
    public Memory<byte> Free(int frames) => Bytes.AsMemory(Frames * Channels * sizeof(short), frames * Channels * sizeof(short));

    /// <summary>Returns the bytes to the pool. Safe to call more than once.</summary>
    public void Return()
    {
        if (Interlocked.Exchange(ref _returned, 1) == 0)
        {
            ArrayPool<byte>.Shared.Return(Bytes);
        }
    }
}
