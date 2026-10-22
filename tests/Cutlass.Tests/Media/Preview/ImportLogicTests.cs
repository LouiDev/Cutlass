using System.Buffers.Binary;
using System.Security.Cryptography;
using Cutlass.Media.Services;

namespace Cutlass.Tests.Media.Preview;

/// <summary>The pure parts of import: the content fingerprint (TECH_BRIEF 6.2) and the poster command (6.5).</summary>
public sealed class ImportLogicTests : IDisposable
{
    private const int MiB = 1024 * 1024;
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "cutlass-import-" + Guid.NewGuid().ToString("N")[..12]);

    public ImportLogicTests()
    {
        Directory.CreateDirectory(_temp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData((3 * MiB) - 1)]
    [InlineData(3 * MiB)]
    [InlineData((7 * MiB) + 12345)]
    public async Task TheFingerprintHashesSizeAndFirstMiddleLastMiB(int size)
    {
        var bytes = new byte[size];
        new Random(size).NextBytes(bytes);
        var file = Path.Combine(_temp, $"f{size}.bin");
        await File.WriteAllBytesAsync(file, bytes);

        var expected = Expected(bytes);

        Assert.Equal(expected, await MediaImporter.ComputeFingerprintAsync(file, CancellationToken.None));
    }

    [Fact]
    public async Task TheFingerprintIgnoresPathAndMtimeButSeesContent()
    {
        var bytes = new byte[5 * MiB];
        new Random(5).NextBytes(bytes);
        var a = Path.Combine(_temp, "a.mp4");
        var b = Path.Combine(_temp, "b it's [odd] Ä.mp4");
        await File.WriteAllBytesAsync(a, bytes);
        await File.WriteAllBytesAsync(b, bytes);
        File.SetLastWriteTimeUtc(b, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var fa = await MediaImporter.ComputeFingerprintAsync(a, CancellationToken.None);
        Assert.Equal(fa, await MediaImporter.ComputeFingerprintAsync(b, CancellationToken.None));

        // A byte in the middle chunk changes it; a byte outside the sampled chunks does not.
        bytes[(bytes.Length - MiB) / 2] ^= 0xFF;
        await File.WriteAllBytesAsync(b, bytes);
        Assert.NotEqual(fa, await MediaImporter.ComputeFingerprintAsync(b, CancellationToken.None));
        bytes[(bytes.Length - MiB) / 2] ^= 0xFF;
        bytes[MiB + 10] ^= 0xFF;
        await File.WriteAllBytesAsync(b, bytes);
        Assert.Equal(fa, await MediaImporter.ComputeFingerprintAsync(b, CancellationToken.None));
    }

    [Fact]
    public void ThePosterCommandMatchesTheBrief()
    {
        var args = PosterService.BuildArguments("D:/Clips/it's.mp4", 0, 1_234_567, "D:/cache/posters/x.tmp-1a2b3c4d.jpg");

        string[] expected =
        [
            "-hide_banner", "-nostdin", "-v", "error", "-ss", "1.234567", "-i", "D:/Clips/it's.mp4", "-map", "0:0",
            "-frames:v", "1", "-vf", "scale=w=320:h=180:force_original_aspect_ratio=decrease", "-q:v", "4", "-y",
            "D:/cache/posters/x.tmp-1a2b3c4d.jpg",
        ];

        Assert.Equal(expected, args);
        Assert.DoesNotContain("-ss", PosterService.BuildArguments("D:/still.png", 2, null, "out.jpg"));
    }

    [Fact]
    public void PosterFilesAreNamedByTheFingerprintHex()
    {
        var hex = new string('a', 64);
        Assert.Equal(hex, PosterService.FileStem("sha256:" + hex));
        Assert.Matches("^[0-9a-f]{64}$", PosterService.FileStem("weird:name with spaces"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private static string Expected(byte[] bytes)
    {
        var size = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(size, bytes.LongLength);
        List<byte> data = [.. size];
        if (bytes.Length < 3 * MiB)
        {
            data.AddRange(bytes);
        }
        else
        {
            data.AddRange(bytes.AsSpan(0, MiB).ToArray());
            data.AddRange(bytes.AsSpan((bytes.Length - MiB) / 2, MiB).ToArray());
            data.AddRange(bytes.AsSpan(bytes.Length - MiB, MiB).ToArray());
        }

        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData([.. data]));
    }
}
