using Cutlass.App.Services.Ffmpeg;

namespace Cutlass.Tests.App.Shell;

/// <summary>
/// The Settings ffmpeg location rules (TECH_BRIEF 5.2) on a fake file system: a full path to the executable or its
/// folder, an executable file and ffprobe next to it.
/// </summary>
public sealed class SettingsFfmpegPathTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "cutlass-ffmpeg-path");

    private static FfmpegPathCheck Check(bool isWindows, string[] files, string[] folders)
    {
        var fileSet = files.Select(Path.GetFullPath).ToHashSet(StringComparer.Ordinal);
        var folderSet = folders.Select(Path.GetFullPath).ToHashSet(StringComparer.Ordinal);
        return new FfmpegPathCheck(isWindows, p => fileSet.Contains(Path.GetFullPath(p)), p => folderSet.Contains(Path.GetFullPath(p)));
    }

    [Fact]
    public void AFolderResolvesToTheExecutableInside()
    {
        var bin = Path.Combine(Root, "bin");
        var check = Check(isWindows: true, [Path.Combine(bin, "ffmpeg.exe"), Path.Combine(bin, "ffprobe.exe")], [bin]);
        Assert.Null(check.Check(bin, out var ffmpeg));
        Assert.Equal(Path.Combine(bin, "ffmpeg.exe"), ffmpeg);
    }

    [Fact]
    public void TheExecutableItselfIsAccepted()
    {
        var bin = Path.Combine(Root, "bin");
        var exe = Path.Combine(bin, "ffmpeg");
        var check = Check(isWindows: false, [exe, Path.Combine(bin, "ffprobe")], [bin]);
        Assert.Null(check.Check("  \"" + exe + "\"  ", out var ffmpeg));
        Assert.Equal(exe, ffmpeg);
        Assert.Equal("ffmpeg", check.FfmpegFileName);
    }

    [Fact]
    public void AFolderWithoutFfmpegIsRefused()
    {
        var bin = Path.Combine(Root, "empty");
        var check = Check(isWindows: true, [], [bin]);
        Assert.Equal($"There is no ffmpeg.exe in {bin}.", check.Check(bin, out var ffmpeg));
        Assert.Null(ffmpeg);
    }

    [Fact]
    public void AMissingFileIsRefused()
    {
        var exe = Path.Combine(Root, "nowhere", "ffmpeg.exe");
        Assert.Equal($"{exe} is not an executable file.", Check(isWindows: true, [], []).Check(exe, out _));
    }

    [Fact]
    public void FfprobeMustSitNextToFfmpeg()
    {
        var exe = Path.Combine(Root, "only", "ffmpeg.exe");
        Assert.Equal(
            $"ffprobe was not found next to {exe}; Cutlass needs both.",
            Check(isWindows: true, [exe], [Path.Combine(Root, "only")]).Check(exe, out _));
    }

    [Fact]
    public void ThePlatformCheckRunsEnsureExecutableOnFfmpegAndFfprobe()
    {
        // Real files without execute bits, the way a static build unpacked from a zip arrives on Linux.
        var bin = Path.Combine(Root, "real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bin);
        try
        {
            var check = FfmpegPathCheck.ForCurrentPlatform();
            var exe = Path.Combine(bin, check.FfmpegFileName);
            var probe = Path.Combine(bin, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            File.WriteAllText(exe, string.Empty);
            File.WriteAllText(probe, string.Empty);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.SetUnixFileMode(probe, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            Assert.Null(check.Check(bin, out var ffmpeg));
            Assert.Equal(exe, ffmpeg);
            if (!OperatingSystem.IsWindows())
            {
                Assert.NotEqual(UnixFileMode.None, File.GetUnixFileMode(exe) & UnixFileMode.UserExecute);
                Assert.NotEqual(UnixFileMode.None, File.GetUnixFileMode(probe) & UnixFileMode.UserExecute);
            }
        }
        finally
        {
            Directory.Delete(bin, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnEmptyLocationAsksForOne(string? path) =>
        Assert.Equal("Enter the ffmpeg location, or use PATH.", Check(isWindows: true, [], []).Check(path, out _));

    [Theory]
    [InlineData("ffmpeg")]
    [InlineData("bin/ffmpeg.exe")]
    public void RelativeLocationsAreRefused(string path) =>
        Assert.Equal("Enter a full path to ffmpeg or its folder.", Check(isWindows: true, [path], []).Check(path, out _));
}
