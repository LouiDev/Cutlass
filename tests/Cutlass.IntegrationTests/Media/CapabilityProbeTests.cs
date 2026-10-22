using System.Text.Json.Nodes;
using Cutlass.Compiler.Capabilities;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.IntegrationTests.Media;

/// <summary>Version validation, capability lists and the script syntax probe on the real ffmpeg (TECH_BRIEF 5.2).</summary>
public sealed class CapabilityProbeTests
{
    [FfmpegFact]
    public async Task VersionIsParsedAndNewEnough()
    {
        await using var h = await MediaHarness.CreateAsync();

        Assert.False(string.IsNullOrWhiteSpace(h.Toolset.Version.BuildId));
        Assert.True(h.Toolset.Version.AvfilterMajor >= FfmpegCapabilities.MinimumAvfilterMajor);
        Assert.True(File.Exists(h.Toolset.FfprobePath));
        Assert.Equal(FfmpegLocator.ProbePathFor(h.Toolset.FfmpegPath), h.Toolset.FfprobePath);
    }

    [FfmpegFact]
    public async Task CapabilitiesListFiltersEncodersAndScriptSyntax()
    {
        await using var h = await MediaHarness.CreateAsync();
        var probe = new CapabilityProbe(h.Runner, h.Work, Path.Combine(h.Root, "capabilities.json"));

        var caps = await probe.ProbeAsync(h.Toolset, CancellationToken.None);

        Assert.Equal(h.Toolset.Version.BuildId, caps.BuildId);
        Assert.Equal(h.Toolset.Version.AvfilterMajor, caps.AvfilterMajor);
        Assert.Empty(caps.MissingRequiredFilters);
        Assert.True(caps.CanRender);
        Assert.True(caps.HasDrawtext);
        Assert.True(caps.Filters.Count > 100);
        foreach (var filter in new[] { "xfade", "acrossfade", "atempo", "overlay", "tpad", "apad", "adelay", "amix", "showwavespic", "concat" })
        {
            Assert.True(caps.HasFilter(filter), filter);
        }

        Assert.True(caps.HasEncoder("mpeg4"));
        Assert.True(caps.HasEncoder("aac"));
        Assert.True(caps.HasEncoder("pcm_s16le"));
        Assert.True(caps.HasEncoder("ffv1"));
        Assert.False(caps.HasEncoder("="));

        var expected = caps.AvfilterMajor >= 10 ? FilterScriptSyntax.Slash : FilterScriptSyntax.LegacyScript;
        Assert.Equal(expected, caps.ScriptSyntax);
        Assert.Equal(caps.AvfilterMajor >= 10 ? "demux" : "-1", caps.EncoderTimeBaseKeyword);
        Assert.True(File.Exists(Path.Combine(h.Work.Path, CapabilityProbe.ProbeFileName)));
    }

    [FfmpegFact]
    public async Task ScriptOptionReallyRunsAScriptFromTheWorkDirectory()
    {
        await using var h = await MediaHarness.CreateAsync();
        var probe = new CapabilityProbe(h.Runner, h.Work, cacheFile: null);
        var caps = await probe.ProbeAsync(h.Toolset, CancellationToken.None);
        Assert.NotNull(caps.ScriptOption);

        var script = h.Work.WriteScript("color=c=red:s=32x32:r=25:d=0.2[v]");
        var result = await h.Runner.RunAsync(
            h.Ffmpeg(["-hide_banner", "-nostdin", "-v", "error", caps.ScriptOption!, script, "-map", "[v]",
                      "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1"]),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(5 * 32 * 32 * 4, result.Stdout.Length);
    }

    [FfmpegFact]
    public async Task CacheIsUsedWhileTheExecutableIsUnchanged()
    {
        await using var h = await MediaHarness.CreateAsync();
        var cacheFile = Path.Combine(h.Root, "capabilities.json");
        var first = await new CapabilityProbe(h.Runner, h.Work, cacheFile).ProbeAsync(h.Toolset, CancellationToken.None);
        Assert.True(File.Exists(cacheFile));

        // Plant a marker filter in the cache: only a cache hit can report it.
        var json = JsonNode.Parse(await File.ReadAllTextAsync(cacheFile))!;
        json["filters"]!.AsArray().Add("zz_cache_marker");
        await File.WriteAllTextAsync(cacheFile, json.ToJsonString());

        var cached = await new CapabilityProbe(h.Runner, h.Work, cacheFile).ProbeAsync(h.Toolset, CancellationToken.None);
        Assert.True(cached.HasFilter("zz_cache_marker"));
        Assert.Equal(first.ScriptSyntax, cached.ScriptSyntax);
        Assert.Equal(first.Encoders, cached.Encoders);

        var refreshed = await new CapabilityProbe(h.Runner, h.Work, cacheFile).ProbeAsync(h.Toolset, CancellationToken.None, forceRefresh: true);
        Assert.False(refreshed.HasFilter("zz_cache_marker"));
    }

    [FfmpegFact]
    public async Task LocatorFindsTheTestFfmpegThroughTheSettingsPath()
    {
        await using var h = await MediaHarness.CreateAsync();
        var locator = FfmpegLocator.ForCurrentPlatform();

        var bySettingsFile = await locator.FindAsync(TestFfmpeg.FfmpegPath, h.Runner, h.Work.Path, CancellationToken.None);
        var bySettingsFolder = await locator.FindAsync(Path.GetDirectoryName(TestFfmpeg.FfmpegPath), h.Runner, h.Work.Path, CancellationToken.None);

        Assert.NotNull(bySettingsFile);
        Assert.Equal(Path.GetFullPath(TestFfmpeg.FfmpegPath), bySettingsFile.FfmpegPath, StringComparer.OrdinalIgnoreCase);
        Assert.NotNull(bySettingsFolder);
        Assert.Equal(bySettingsFile.FfmpegPath, bySettingsFolder.FfmpegPath, StringComparer.OrdinalIgnoreCase);
    }

    [FfmpegFact]
    public async Task ValidationRejectsAnExecutableThatIsNotFfmpeg()
    {
        await using var h = await MediaHarness.CreateAsync();

        // ffprobe passes the sibling check (its sibling is itself) but prints "ffprobe version", which does not parse.
        await Assert.ThrowsAsync<FfmpegException>(
            () => FfmpegToolset.ValidateAsync(TestFfmpeg.FfprobePath, h.Runner, h.Work.Path, CancellationToken.None));
    }
}
