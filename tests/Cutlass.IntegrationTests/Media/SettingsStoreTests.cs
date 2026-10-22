using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Settings;

namespace Cutlass.IntegrationTests.Media;

/// <summary>SettingsStore: defaults, round trip, atomic save and corrupt files.</summary>
public sealed class SettingsStoreTests
{
    [Fact]
    public async Task MissingFileGivesDefaults()
    {
        using var temp = new TempDirectory("cutlass-settings");
        var store = new SettingsStore(temp.Combine("cfg", "settings.json"));

        var settings = await store.LoadAsync();

        Assert.Equal(new AppSettings(), settings);
        Assert.Equal(2, settings.AutosaveIntervalMinutes);
        Assert.Equal(20, settings.CacheLimitGb);
        Assert.Equal(ProxyPolicy.Auto, settings.ProxyPolicy);
    }

    [Fact]
    public async Task RoundTripKeepsEveryValue()
    {
        using var temp = new TempDirectory("cutlass-settings");
        var store = new SettingsStore(temp.Combine("cfg", "settings.json"));
        var settings = new AppSettings
        {
            AutosaveIntervalMinutes = 5,
            DefaultTransitionSeconds = 0.5,
            FfmpegPath = "C:/Tools/it's \u00C4/ffmpeg.exe",
            ProxyPolicy = ProxyPolicy.Never,
            DefaultPreviewQuality = PreviewQuality.Quarter,
            UseProxies = false,
            TopRowFraction = 0.6,
        };

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal(settings, loaded);
        var json = await File.ReadAllTextAsync(store.SettingsFile);
        Assert.Contains("\"proxyPolicy\": \"Never\"", json, StringComparison.Ordinal);
        Assert.Contains("\"defaultTransitionSeconds\": 0.5", json, StringComparison.Ordinal);
        Assert.False(File.Exists(store.SettingsFile + ".tmp"));
    }

    [Fact]
    public async Task UnknownAndMissingFieldsAreTolerated()
    {
        using var temp = new TempDirectory("cutlass-settings");
        var file = temp.Combine("settings.json");
        await File.WriteAllTextAsync(file, "{ \"cacheLimitGb\": 7, \"somethingNew\": [1, 2] }");

        var loaded = await new SettingsStore(file).LoadAsync();

        Assert.Equal(7, loaded.CacheLimitGb);
        Assert.Equal(2, loaded.AutosaveIntervalMinutes);
    }

    [Fact]
    public async Task CorruptFileIsMovedAsideAndDefaultsAreUsed()
    {
        using var temp = new TempDirectory("cutlass-settings");
        var file = temp.Combine("settings.json");
        await File.WriteAllTextAsync(file, "{ broken");

        var loaded = await new SettingsStore(file).LoadAsync();

        Assert.Equal(new AppSettings(), loaded);
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(file + ".bad"));
    }
}
