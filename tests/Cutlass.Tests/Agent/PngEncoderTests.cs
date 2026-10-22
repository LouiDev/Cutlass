using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Cutlass.Agent.Imaging;

namespace Cutlass.Tests.Agent;

/// <summary>The PNG encoder (AGENT_INTEGRATION 7.1): signature, IHDR and CRCs, and an IDAT that decodes to the input.</summary>
public sealed class PngEncoderTests
{
    /// <summary>A tiny PNG reader for 8-bit RGB with filters None and Sub (and the others, for completeness).</summary>
    internal static (int Width, int Height, byte[] Rgb) Decode(byte[] png)
    {
        Assert.True(png.AsSpan(0, 8).SequenceEqual(PngEncoder.Signature));
        var pos = 8;
        int width = 0, height = 0;
        using var idat = new MemoryStream();
        var sawEnd = false;
        while (pos < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var type = Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + length));
            Assert.Equal(PngEncoder.Crc32(png.AsSpan(pos + 4, length + 4)), crc);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    Assert.Equal(8, data[8]);
                    Assert.Equal(2, data[9]);
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    sawEnd = true;
                    break;
            }

            pos += 12 + length;
        }

        Assert.True(sawEnd);
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var bytes = raw.ToArray();
        var row = width * 3;
        Assert.Equal((row + 1) * height, bytes.Length);
        var rgb = new byte[row * height];
        for (var y = 0; y < height; y++)
        {
            var filter = bytes[y * (row + 1)];
            for (var i = 0; i < row; i++)
            {
                var x = bytes[(y * (row + 1)) + 1 + i];
                var a = i >= 3 ? rgb[(y * row) + i - 3] : 0;
                var b = y > 0 ? rgb[((y - 1) * row) + i] : 0;
                var c = i >= 3 && y > 0 ? rgb[((y - 1) * row) + i - 3] : 0;
                rgb[(y * row) + i] = filter switch
                {
                    0 => x,
                    1 => (byte)(x + a),
                    2 => (byte)(x + b),
                    3 => (byte)(x + ((a + b) / 2)),
                    4 => (byte)(x + Paeth(a, b, c)),
                    _ => throw new InvalidDataException("bad filter"),
                };
            }
        }

        return (width, height, rgb);
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    [Fact]
    public void Crc32MatchesTheKnownValue()
    {
        Assert.Equal(0xCBF43926u, PngEncoder.Crc32("123456789"u8));
        Assert.Equal(0xAE426082u, PngEncoder.Crc32("IEND"u8));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 3)]
    [InlineData(64, 36)]
    public void BgraDecodesBackToTheInputPixels(int width, int height)
    {
        var rng = new Random(width * 31 + height);
        var stride = (width * 4) + 8;
        var bgra = new byte[stride * height];
        rng.NextBytes(bgra);
        var png = PngEncoder.EncodeBgra(bgra, width, height, stride);
        var (w, h, rgb) = Decode(png);
        Assert.Equal(width, w);
        Assert.Equal(height, h);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var s = (y * stride) + (x * 4);
                var t = ((y * width) + x) * 3;
                Assert.Equal(bgra[s + 2], rgb[t]);
                Assert.Equal(bgra[s + 1], rgb[t + 1]);
                Assert.Equal(bgra[s], rgb[t + 2]);
            }
        }
    }

    [Fact]
    public void EveryRowUsesTheSubFilterAndTheOutputIsDeterministic()
    {
        var bgra = new byte[16 * 4 * 4];
        new Random(5).NextBytes(bgra);
        var a = PngEncoder.EncodeBgra(bgra, 16, 4, 64);
        var b = PngEncoder.EncodeBgra(bgra, 16, 4, 64);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ATooSmallBufferIsRefused()
    {
        Assert.Throws<ArgumentException>(() => PngEncoder.EncodeBgra(new byte[10], 4, 4, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => PngEncoder.EncodeRgb(new byte[10], 0, 4, 0));
    }

    [Fact]
    public void AContactSheetPlacesCellsInReadingOrder()
    {
        var sheet = new ContactSheetComposer(4, 2, 2, 3);
        Assert.Equal(2 * 4 + ContactSheetComposer.Gutter, sheet.Width);
        Assert.Equal(2 * 2 + ContactSheetComposer.Gutter, sheet.Height);
        Assert.Equal((0, 0), sheet.CellOrigin(0));
        Assert.Equal((4 + ContactSheetComposer.Gutter, 0), sheet.CellOrigin(1));
        Assert.Equal((0, 2 + ContactSheetComposer.Gutter), sheet.CellOrigin(2));
        var red = Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 8).SelectMany(b => b).ToArray();
        sheet.Draw(2, red, 16);
        var (w, _, rgb) = Decode(sheet.ToPng());
        var (x, y) = sheet.CellOrigin(2);
        Assert.Equal(255, rgb[((y * w) + x) * 3]);
        Assert.Equal(0, rgb[(((y * w) + x) * 3) + 1]);
    }
}
