using Cutlass.Media.Platform;

namespace Cutlass.IntegrationTests.Media;

/// <summary>AppPaths resolved on the machine running the tests (TECH_BRIEF 5.1).</summary>
public sealed class AppPathsCurrentOsTests
{
    [Fact]
    public void CurrentPlatformLayoutMatchesTheBrief()
    {
        var paths = AppPaths.ForCurrentPlatform();

        Assert.Equal(OperatingSystem.IsWindows(), paths.IsWindows);
        Assert.Equal("settings.json", Path.GetFileName(paths.SettingsFile));
        Assert.Equal(Path.Combine(paths.CacheRoot, "work"), paths.WorkDirectory);
        Assert.Equal(Path.Combine(paths.CacheRoot, "pids.json"), paths.PidsFile);
        Assert.Equal(Path.Combine(paths.CacheRoot, "capabilities.json"), paths.CapabilitiesFile);
        Assert.True(Path.IsPathFullyQualified(paths.SettingsFile));
        Assert.True(Path.IsPathFullyQualified(paths.DataDirectory));
        Assert.True(Path.IsPathFullyQualified(paths.CacheRoot));

        if (OperatingSystem.IsWindows())
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert.Equal(Path.Combine(roaming, "Cutlass", "settings.json"), paths.SettingsFile, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(local, "Cutlass"), paths.DataDirectory, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(local, "Cutlass", "Cache", "v1"), paths.CacheRoot, StringComparer.OrdinalIgnoreCase);
            Assert.True(paths.PathsEqual(paths.CacheRoot, paths.CacheRoot.ToUpperInvariant()));
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var cacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            var expectedCache = string.IsNullOrEmpty(cacheHome) || !Path.IsPathRooted(cacheHome)
                ? Path.Combine(home, ".cache", "cutlass", "v1")
                : Path.Combine(cacheHome, "cutlass", "v1");
            Assert.Equal(AppPaths.Normalize(expectedCache), paths.CacheRoot);
            Assert.EndsWith(Path.Combine("cutlass", "settings.json"), paths.SettingsFile, StringComparison.Ordinal);
            Assert.False(paths.PathsEqual("/tmp/A", "/tmp/a"));
        }
    }
}
