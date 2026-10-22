using Cutlass.Media.Platform;

namespace Cutlass.Tests.Media;

public sealed class AppPathsTests
{
    [Fact]
    public void WindowsLayoutUsesRoamingForSettingsAndLocalForDataAndCache()
    {
        var paths = AppPaths.Resolve(
            isWindows: true,
            _ => null,
            folder => folder switch
            {
                Environment.SpecialFolder.ApplicationData => Rooted("Users", "u", "AppData", "Roaming"),
                Environment.SpecialFolder.LocalApplicationData => Rooted("Users", "u", "AppData", "Local"),
                _ => Rooted("Users", "u"),
            });

        Assert.Equal(Full(Rooted("Users", "u", "AppData", "Roaming", "Cutlass", "settings.json")), paths.SettingsFile);
        Assert.Equal(Full(Rooted("Users", "u", "AppData", "Local", "Cutlass")), paths.DataDirectory);
        Assert.Equal(Full(Rooted("Users", "u", "AppData", "Local", "Cutlass", "Cache", "v1")), paths.CacheRoot);
        Assert.Equal(Path.Combine(paths.CacheRoot, "work"), paths.WorkDirectory);
        Assert.Equal(StringComparison.OrdinalIgnoreCase, paths.PathComparison);
    }

    [Fact]
    public void LinuxLayoutFallsBackToHomeWhenXdgIsUnset()
    {
        var paths = AppPaths.Resolve(isWindows: false, _ => null, _ => Rooted("home", "u"));

        Assert.Equal(Full(Rooted("home", "u", ".config", "cutlass", "settings.json")), paths.SettingsFile);
        Assert.Equal(Full(Rooted("home", "u", ".local", "share", "cutlass")), paths.DataDirectory);
        Assert.Equal(Full(Rooted("home", "u", ".cache", "cutlass", "v1")), paths.CacheRoot);
        Assert.Equal(StringComparison.Ordinal, paths.PathComparison);
    }

    [Fact]
    public void LinuxLayoutHonoursAbsoluteXdgVariablesAndIgnoresRelativeOnes()
    {
        var env = new Dictionary<string, string?>
        {
            ["XDG_CONFIG_HOME"] = Rooted("cfg"),
            ["XDG_DATA_HOME"] = "relative/data",
            ["XDG_CACHE_HOME"] = Rooted("fast", "cache"),
        };

        var paths = AppPaths.Resolve(isWindows: false, name => env.GetValueOrDefault(name), _ => Rooted("home", "u"));

        Assert.Equal(Full(Rooted("cfg", "cutlass", "settings.json")), paths.SettingsFile);
        Assert.Equal(Full(Rooted("home", "u", ".local", "share", "cutlass")), paths.DataDirectory);
        Assert.Equal(Full(Rooted("fast", "cache", "cutlass", "v1")), paths.CacheRoot);
    }

    [Fact]
    public void PathsCompareCaseInsensitivelyOnlyOnWindows()
    {
        var windows = new AppPaths(Rooted("a", "s.json"), Rooted("a"), Rooted("a", "c"), isWindows: true);
        var linux = new AppPaths(Rooted("a", "s.json"), Rooted("a"), Rooted("a", "c"), isWindows: false);
        var lower = Rooted("media", "clip.mp4");
        var upper = Rooted("MEDIA", "Clip.MP4");

        Assert.True(windows.PathsEqual(lower, upper));
        Assert.False(linux.PathsEqual(lower, upper));
        Assert.True(linux.PathsEqual(lower, Path.Combine(Rooted("media", "sub"), "..", "clip.mp4")));
    }

    [Fact]
    public void NormalizeTrimsTrailingSeparatorsButKeepsRoots()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;

        Assert.Equal(Full(Rooted("x", "y")), AppPaths.Normalize(Rooted("x", "y") + Path.DirectorySeparatorChar));
        Assert.Equal(root, AppPaths.Normalize(root));
    }

    [Fact]
    public void CurrentPlatformResolvesAbsolutePaths()
    {
        var paths = AppPaths.ForCurrentPlatform();

        Assert.True(Path.IsPathFullyQualified(paths.SettingsFile));
        Assert.True(Path.IsPathFullyQualified(paths.CacheRoot));
        Assert.EndsWith("v1", paths.CacheRoot, StringComparison.Ordinal);
        Assert.Equal(OperatingSystem.IsWindows(), paths.IsWindows);
    }

    private static string Rooted(params string[] parts)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;
        return Path.Combine([root, .. parts]);
    }

    private static string Full(string path) => Path.GetFullPath(path);
}
