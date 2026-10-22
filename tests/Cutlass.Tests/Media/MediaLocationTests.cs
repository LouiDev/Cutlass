using Cutlass.Core.Model;
using Cutlass.Media.Projects;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Media;

/// <summary>
/// Remote media paths from a project are not touched on open (DESIGN 11): UNC paths and network drives on Windows,
/// network file systems on Linux, unless they share the project's volume or the user confirmed them.
/// </summary>
public sealed class MediaLocationTests
{
    private static readonly DateTime Mtime = new(2026, 10, 1, 9, 12, 44, DateTimeKind.Utc);

    private static readonly (string, string)[] Mounts =
    [
        ("/", "ext4"),
        ("/home", "ext4"),
        ("/mnt/nas", "cifs"),
        ("/mnt/nas/sub mount", "nfs4"),
        ("/media/usb", "vfat"),
    ];

    [Theory]
    [InlineData(@"\\server\share\clip.mp4", @"C:\Projects\a.cutlass", true)]
    [InlineData(@"//server/share/clip.mp4", @"C:\Projects\a.cutlass", true)]
    [InlineData(@"\\?\UNC\server\share\clip.mp4", null, true)]
    [InlineData(@"\\.\pipe\clip", @"C:\Projects\a.cutlass", true)]
    [InlineData(@"\\server\share\clip.mp4", @"\\SERVER\Share\proj\a.cutlass", false)]
    [InlineData(@"Z:\clip.mp4", @"C:\Projects\a.cutlass", true)]
    [InlineData(@"Z:\clip.mp4", @"Z:\Projects\a.cutlass", false)]
    [InlineData(@"D:\clip.mp4", @"C:\Projects\a.cutlass", false)]
    [InlineData(@"\\?\D:\clip.mp4", @"C:\Projects\a.cutlass", false)]
    [InlineData(@"C:\clip.mp4", null, false)]
    [InlineData(@"Q:\clip.mp4", null, true)]
    public void WindowsUncAndNetworkDrivesOffTheProjectVolumeAreUntrusted(string media, string? project, bool expected)
    {
        static DriveType Type(string root) => root switch
        {
            @"C:\" or @"D:\" => DriveType.Fixed,
            @"Z:\" => DriveType.Network,
            _ => DriveType.NoRootDirectory,
        };

        Assert.Equal(expected, MediaLocation.IsUntrustedOnWindows(media, project, Type));
    }

    [Theory]
    [InlineData("/mnt/nas/clip.mp4", "/home/me/a.cutlass", true)]
    [InlineData("/mnt/nas/sub mount/clip.mp4", "/mnt/nas/a.cutlass", true)]
    [InlineData("/mnt/nas/clip.mp4", "/mnt/nas/proj/a.cutlass", false)]
    [InlineData("/mnt/nas/clip.mp4", null, true)]
    [InlineData("/media/usb/clip.mp4", "/home/me/a.cutlass", false)]
    [InlineData("/home/me/clip.mp4", null, false)]
    [InlineData("/mnt/nasty/clip.mp4", "/home/me/a.cutlass", false)]
    public void LinuxNetworkFileSystemsOffTheProjectMountAreUntrusted(string media, string? project, bool expected)
    {
        Assert.Equal(expected, MediaLocation.IsUntrustedOnLinux(media, project, Mounts));
    }

    [Fact]
    public void MountsAreParsedWithEscapedSpaces()
    {
        var text = "sysfs /sys sysfs rw 0 0\n//nas/media /mnt/nas/sub\\040mount cifs rw 0 0\n";

        var mounts = MediaLocation.ParseMounts(text);

        Assert.Equal([("/sys", "sysfs"), ("/mnt/nas/sub mount", "cifs")], mounts);
    }

    [Fact]
    public void UntrustedPathsAreNotLookedUpUntilConfirmed()
    {
        var remote = Asset("m_r", "//nas/share/clip.mp4");
        var local = Asset("m_l", "/data/clip.mp4");
        var project = new ProjectBuilder().Build() with { Media = [remote, local] };
        var looked = new List<string>();
        MediaFileFacts? Facts(string path)
        {
            looked.Add(path);
            return new MediaFileFacts(1_000_000, Mtime);
        }

        static bool Untrusted(string path) => path.Replace('\\', '/').StartsWith("//", StringComparison.Ordinal);

        var scan = MediaRelinker.Scan(project, null, Facts, Untrusted);

        Assert.Equal(MediaLinkState.Offline, scan.State("m_r"));
        Assert.Equal(MediaLinkState.Online, scan.State("m_l"));
        Assert.Equal(["m_r"], scan.UnconfirmedIds);
        Assert.DoesNotContain(looked, p => Untrusted(p));

        var confirmed = MediaRelinker.Scan(project, null, Facts, Untrusted, ["m_r"]);

        Assert.Equal(MediaLinkState.Online, confirmed.State("m_r"));
        Assert.Empty(confirmed.UnconfirmedIds);
    }

    [Fact]
    public void FactsAreReadOnlyForRegularFiles()
    {
        Assert.Null(MediaFileFacts.Of(Path.GetTempPath()));
        Assert.Null(MediaFileFacts.Of(OperatingSystem.IsWindows() ? @"\\.\NUL" : "/dev/null"));
        var file = Path.Combine(Path.GetTempPath(), "cutlass-facts-" + Guid.NewGuid().ToString("N")[..8] + ".mp4");
        File.WriteAllText(file, "x");
        try
        {
            Assert.Equal(1, MediaFileFacts.Of(file)!.SizeBytes);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static MediaAsset Asset(string id, string path) =>
        TestMedia.Asset(id, path, 4_000_000, video: true, audio: true) with { SizeBytes = 1_000_000, MtimeUtc = Mtime };
}
