using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.IntegrationTests.Media;

/// <summary>FfmpegLocator search order on a fake file system (TECH_BRIEF 5.2 and 11.1).</summary>
public sealed class FfmpegLocatorTests
{
    private static readonly bool IsWindows = OperatingSystem.IsWindows();
    private static readonly string Root = Path.GetPathRoot(Path.GetTempPath())!;
    private static readonly string Exe = IsWindows ? ".exe" : string.Empty;

    [Fact]
    public void SearchDirectoriesFollowTheBriefOrder()
    {
        var app = Dir("app");
        var pathA = Dir("pa");
        var pathB = Dir("pb");
        var env = Env(pathA, pathB);
        var locator = new FfmpegLocator(app, IsWindows, env, _ => false, _ => false);

        var dirs = locator.SearchDirectories();

        Assert.Equal(Path.Combine(app, "ffmpeg"), dirs[0]);
        Assert.Equal(app, dirs[1]);
        Assert.Equal(pathA, dirs[2]);
        Assert.Equal(pathB, dirs[3]);
        if (IsWindows)
        {
            Assert.Equal(Path.Combine(Dir("local"), "Microsoft", "WinGet", "Links"), dirs[4]);
            Assert.Equal(Path.Combine(Dir("profile"), "scoop", "shims"), dirs[5]);
            Assert.Equal(@"C:\ProgramData\chocolatey\bin", dirs[6]);
            Assert.Equal(@"C:\ffmpeg\bin", dirs[7]);
        }
        else
        {
            Assert.Equal(new[] { "/usr/bin", "/usr/local/bin", "/opt/ffmpeg/bin" }, dirs.Skip(4));
        }
    }

    [Fact]
    public void SettingsPathComesFirstThenAppFolderThenPath()
    {
        var app = Dir("app");
        var custom = Dir("custom");
        var onPath = Dir("onpath");
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dir in new[] { custom, Path.Combine(app, "ffmpeg"), app, onPath })
        {
            files.Add(Path.Combine(dir, "ffmpeg" + Exe));
            files.Add(Path.Combine(dir, "ffprobe" + Exe));
        }

        var locator = new FfmpegLocator(app, IsWindows, Env(onPath), files.Contains, d => d == custom);

        Assert.Equal(
            new[]
            {
                Path.Combine(custom, "ffmpeg" + Exe),
                Path.Combine(app, "ffmpeg", "ffmpeg" + Exe),
                Path.Combine(app, "ffmpeg" + Exe),
                Path.Combine(onPath, "ffmpeg" + Exe),
            },
            locator.FindCandidates(custom));

        // The setting may also name the executable itself.
        Assert.Equal(Path.Combine(custom, "ffmpeg" + Exe), locator.FindCandidates(Path.Combine(custom, "ffmpeg" + Exe))[0]);
        Assert.Equal(Path.Combine(app, "ffmpeg", "ffmpeg" + Exe), locator.FindCandidates(null)[0]);
    }

    [Fact]
    public void CandidatesWithoutFfprobeNextToThemAreSkipped()
    {
        var app = Dir("app");
        var lonely = Dir("lonely");
        var paired = Dir("paired");
        var files = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.Combine(lonely, "ffmpeg" + Exe),
            Path.Combine(paired, "ffmpeg" + Exe),
            Path.Combine(paired, "ffprobe" + Exe),
        };

        var locator = new FfmpegLocator(app, IsWindows, Env(lonely, paired, paired), files.Contains, _ => false);

        Assert.Equal(new[] { Path.Combine(paired, "ffmpeg" + Exe) }, locator.FindCandidates(null));
    }

    [Theory]
    [InlineData("/usr/bin/ffmpeg", true)]
    [InlineData("/snap/bin/ffmpeg", true)]
    [InlineData("/nix/store/x/bin/ffmpeg", true)]
    [InlineData("/opt/ffmpeg/bin/ffmpeg", false)]
    [InlineData("/home/me/tools/ffmpeg", false)]
    [InlineData("/usrlocal/ffmpeg", false)]
    public void SystemFoldersAreRecognised(string path, bool expected)
    {
        Assert.Equal(expected, FfmpegLocator.IsSystemPath(path));
    }

    [Fact]
    public void EnsureExecutableRestoresLostBitsOfAPickedFile()
    {
        using var temp = new TempDirectory("cutlass-exec");
        var file = temp.Combine(OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        File.WriteAllText(file, "#!/bin/sh\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }

        Assert.True(FfmpegLocator.EnsureExecutable(file));
        Assert.False(FfmpegLocator.EnsureExecutable(temp.Combine("missing")));
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(file);
            Assert.True((mode & UnixFileMode.UserExecute) != 0);
            Assert.True((mode & UnixFileMode.GroupExecute) != 0);
            Assert.True((mode & UnixFileMode.OtherExecute) == 0);
        }
    }

    [Fact]
    public void ProbePathKeepsFolderAndExtension()
    {
        var dir = Dir("tools");
        Assert.Equal(Path.Combine(dir, "ffprobe.exe"), FfmpegLocator.ProbePathFor(Path.Combine(dir, "ffmpeg.exe")));
        Assert.Equal(Path.Combine(dir, "ffprobe"), FfmpegLocator.ProbePathFor(Path.Combine(dir, "ffmpeg")));
    }

    private static string Dir(string name) => Path.Combine(Root, "fake-" + name);

    private static Func<string, string?> Env(params string[] pathEntries) => name => name switch
    {
        "PATH" => string.Join(IsWindows ? ';' : ':', pathEntries),
        "LOCALAPPDATA" => Dir("local"),
        "USERPROFILE" => Dir("profile"),
        _ => null,
    };
}
