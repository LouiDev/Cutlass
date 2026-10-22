using Cutlass.Media.Cache;
using Cutlass.Media.Services;

namespace Cutlass.Tests.Media.Cache;

/// <summary>Fingerprint stability and the <c>fingerprints.json</c> memo (TECH_BRIEF 6.2).</summary>
public sealed class FingerprinterTests : IDisposable
{
    private const int MiB = 1024 * 1024;
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "cutlass-fp-" + Guid.NewGuid().ToString("N")[..12]);

    public FingerprinterTests()
    {
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        Directory.Delete(_temp, recursive: true);
    }

    [Fact]
    public async Task TheFingerprintIsStableAcrossInstancesRenamesAndMtimeAndMatchesTheImporter()
    {
        var bytes = new byte[(4 * MiB) + 17];
        new Random(42).NextBytes(bytes);
        var a = Path.Combine(_temp, "a.mov");
        await File.WriteAllBytesAsync(a, bytes);

        var first = await new Fingerprinter(null).GetAsync(a, CancellationToken.None);
        var second = await new Fingerprinter(null).GetAsync(a, CancellationToken.None);
        Assert.Equal(first, second);
        Assert.Equal(await MediaImporter.ComputeFingerprintAsync(a, CancellationToken.None), first);
        Assert.StartsWith("sha256:", first, StringComparison.Ordinal);
        Assert.Equal(7 + 64, first.Length);

        var renamed = Path.Combine(_temp, "renamed it's [x] Ä.mov");
        File.Move(a, renamed);
        File.SetLastWriteTimeUtc(renamed, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        Assert.Equal(first, await new Fingerprinter(null).GetAsync(renamed, CancellationToken.None));
    }

    [Fact]
    public async Task TheMemoAvoidsRehashingAndSurvivesANewInstance()
    {
        var file = Path.Combine(_temp, "clip.mp4");
        await File.WriteAllBytesAsync(file, new byte[1000]);
        var memo = Path.Combine(_temp, "cache", Fingerprinter.MemoFileName);

        var fingerprinter = new Fingerprinter(memo);
        var fp = await fingerprinter.GetAsync(file, CancellationToken.None);
        Assert.Equal(fp, await fingerprinter.GetAsync(file, CancellationToken.None));
        Assert.Equal(1, fingerprinter.HashCount);
        Assert.True(File.Exists(memo));

        var reopened = new Fingerprinter(memo);
        Assert.Equal(fp, await reopened.GetAsync(file, CancellationToken.None));
        Assert.Equal(0, reopened.HashCount);
    }

    [Fact]
    public async Task AChangedFileMissesTheMemo()
    {
        var file = Path.Combine(_temp, "clip.mp4");
        await File.WriteAllBytesAsync(file, new byte[1000]);
        var fingerprinter = new Fingerprinter(Path.Combine(_temp, Fingerprinter.MemoFileName));
        var before = await fingerprinter.GetAsync(file, CancellationToken.None);

        await File.WriteAllBytesAsync(file, Enumerable.Repeat((byte)1, 1000).ToArray());
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
        var after = await fingerprinter.GetAsync(file, CancellationToken.None);

        Assert.NotEqual(before, after);
        Assert.Equal(2, fingerprinter.HashCount);
    }

    [Fact]
    public async Task ACorruptMemoOnlyCostsARehash()
    {
        var file = Path.Combine(_temp, "clip.mp4");
        await File.WriteAllBytesAsync(file, new byte[10]);
        var memo = Path.Combine(_temp, Fingerprinter.MemoFileName);
        await File.WriteAllTextAsync(memo, "[ broken");

        var fingerprinter = new Fingerprinter(memo);
        var fp = await fingerprinter.GetAsync(file, CancellationToken.None);

        Assert.Equal(await MediaImporter.ComputeFingerprintAsync(file, CancellationToken.None), fp);
        Assert.Equal(1, fingerprinter.HashCount);
    }

    [Fact]
    public void TheMemoKeyIsPathSizeAndMtime()
    {
        var key = Fingerprinter.MemoKey(Path.Combine(_temp, "x.mp4"), 1234, 5678);

        Assert.EndsWith("|1234|5678", key, StringComparison.Ordinal);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(key, Fingerprinter.MemoKey(Path.Combine(_temp, "X.MP4"), 1234, 5678));
        }
    }
}
