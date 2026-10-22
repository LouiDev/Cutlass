using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Cutlass.Agent.Imaging;

/// <summary>
/// A minimal PNG encoder (AGENT_INTEGRATION 8, "PNG without Avalonia"): 8-bit RGB (ffmpeg's BGRA alpha is always 255),
/// the Sub filter on every row, <see cref="ZLibStream"/> for the IDAT and a table CRC-32. Deterministic; no processes.
/// </summary>
public static class PngEncoder
{
    /// <summary>The 8-byte PNG signature.</summary>
    public static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes BGRA pixels (alpha ignored) as an RGB PNG.</summary>
    /// <param name="bgra">The pixels, <paramref name="stride"/> bytes per row.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="stride">Bytes per row (at least <c>width * 4</c>).</param>
    /// <returns>The PNG file bytes.</returns>
    public static byte[] EncodeBgra(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        Validate(width, height, stride, 4, bgra.Length);
        var rgb = new byte[width * 3 * height];
        for (var y = 0; y < height; y++)
        {
            var src = bgra.Slice(y * stride, width * 4);
            var dst = rgb.AsSpan(y * width * 3, width * 3);
            for (int x = 0, s = 0, t = 0; x < width; x++, s += 4, t += 3)
            {
                dst[t] = src[s + 2];
                dst[t + 1] = src[s + 1];
                dst[t + 2] = src[s];
            }
        }

        return EncodeRgb(rgb, width, height, width * 3);
    }

    /// <summary>Encodes RGB pixels as a PNG.</summary>
    /// <param name="rgb">The pixels, <paramref name="stride"/> bytes per row.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="stride">Bytes per row (at least <c>width * 3</c>).</param>
    /// <returns>The PNG file bytes.</returns>
    public static byte[] EncodeRgb(ReadOnlySpan<byte> rgb, int width, int height, int stride)
    {
        Validate(width, height, stride, 3, rgb.Length);
        var rowBytes = width * 3;
        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
        {
            var filtered = new byte[rowBytes + 1];
            filtered[0] = 1; // Sub
            for (var y = 0; y < height; y++)
            {
                var row = rgb.Slice(y * stride, rowBytes);
                for (var i = 0; i < rowBytes; i++)
                {
                    var left = i >= 3 ? row[i - 3] : (byte)0;
                    filtered[i + 1] = (byte)(row[i] - left);
                }

                z.Write(filtered);
            }
        }

        using var png = new MemoryStream();
        png.Write(Signature);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // colour type RGB
        ihdr[10] = 0; // deflate
        ihdr[11] = 0; // adaptive filtering
        ihdr[12] = 0; // no interlace
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", idat.GetBuffer().AsSpan(0, (int)idat.Length));
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>The CRC-32 (ISO 3309, as PNG uses it) of some bytes.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The CRC.</returns>
    public static uint Crc32(ReadOnlySpan<byte> data) => Update(0xFFFFFFFFu, data) ^ 0xFFFFFFFFu;

    private static void Validate(int width, int height, int stride, int bytesPerPixel, int length)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The image must have a positive size.");
        }

        if (stride < width * bytesPerPixel || (long)stride * (height - 1) + ((long)width * bytesPerPixel) > length)
        {
            throw new ArgumentException("The pixel buffer is too small for the size and stride.", nameof(stride));
        }
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        Encoding.ASCII.GetBytes(type, header[4..]);
        stream.Write(header);
        stream.Write(data);
        var crc = Update(0xFFFFFFFFu, header[4..]);
        crc = Update(crc, data) ^ 0xFFFFFFFFu;
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
        stream.Write(tail);
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
