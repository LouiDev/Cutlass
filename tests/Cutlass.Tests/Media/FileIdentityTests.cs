using Cutlass.Media.Platform;

namespace Cutlass.Tests.Media;

/// <summary>File identity and kind (TECH_BRIEF 9 and DESIGN 11): spelling does not matter, content does not either.</summary>
public sealed class FileIdentityTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cutlass-identity-" + Guid.NewGuid().ToString("N")[..8]);

    /// <summary>Creates the scratch folder.</summary>
    public FileIdentityTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "sub"));
    }

    [Fact]
    public void TheSameFileSpelledDifferentlyIsTheSame()
    {
        var file = Path.Combine(_folder, "Clip.mp4");
        File.WriteAllText(file, "x");
        var detour = Path.Combine(_folder, "sub", "..", OperatingSystem.IsWindows() ? "CLIP.MP4" : "Clip.mp4");

        Assert.True(FileIdentity.SameFile(file, detour));
        Assert.Equal(FileIdentity.TryGet(file), FileIdentity.TryGet(detour));
        Assert.NotNull(FileIdentity.TryGet(file));
    }

    [Fact]
    public void EqualContentInAnotherFileIsNotTheSame()
    {
        var a = Path.Combine(_folder, "a.mp4");
        var b = Path.Combine(_folder, "b.mp4");
        File.WriteAllText(a, "same");
        File.WriteAllText(b, "same");

        Assert.False(FileIdentity.SameFile(a, b));
        Assert.NotEqual(FileIdentity.TryGet(a), FileIdentity.TryGet(b));
    }

    [Fact]
    public void MissingFilesCompareByFullPath()
    {
        var missing = Path.Combine(_folder, "missing.mp4");

        Assert.Null(FileIdentity.TryGet(missing));
        Assert.True(FileIdentity.SameFile(missing, Path.Combine(_folder, "sub", "..", "missing.mp4")));
        Assert.False(FileIdentity.SameFile(missing, Path.Combine(_folder, "other.mp4")));
    }

    [Fact]
    public void KindsAreRegularFolderOrMissing()
    {
        var file = Path.Combine(_folder, "f.ttf");
        File.WriteAllText(file, "x");

        Assert.Equal(FileKind.Regular, FileIdentity.KindOf(file));
        Assert.Equal(FileKind.Directory, FileIdentity.KindOf(_folder));
        Assert.Equal(FileKind.Missing, FileIdentity.KindOf(Path.Combine(_folder, "none")));
        Assert.Equal(FileKind.Other, FileIdentity.KindOf(OperatingSystem.IsWindows() ? @"\\.\NUL" : "/dev/null"));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Temp folder; the OS cleans it eventually.
        }
    }
}
