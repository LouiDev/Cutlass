using System.Globalization;
using System.Text.RegularExpressions;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Services;

// Not ".Preview": that namespace would hide TimelineKit.Preview(...) in the Export tests, which import it statically.
namespace Cutlass.IntegrationTests.PreviewTests;

/// <summary>
/// Import (TECH_BRIEF 6.2 to 6.5): every fixture clip, including the odd path, becomes a correct asset registered in
/// the catalog, with a content fingerprint and a poster that fits 320x180; audio-only media has no poster.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed partial class MediaImporterTests
{
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public MediaImporterTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task EveryFixtureClipImportsWithAssetAndPoster()
    {
        await using var kit = await PreviewKit.CreateAsync();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in _media.AllFiles)
        {
            var result = await kit.Importer.ImportAsync(file, CancellationToken.None);
            var asset = result.Asset;
            var info = new FileInfo(file);
            var probe = await kit.Probe.ProbeAsync(file, CancellationToken.None);

            Assert.StartsWith("m_", asset.Id, StringComparison.Ordinal);
            Assert.True(ids.Add(asset.Id), "Media ids must be unique.");
            Assert.Equal(Path.GetFullPath(file), asset.Path);
            Assert.Null(asset.RelativePath);
            Assert.Equal(info.Length, asset.SizeBytes);
            Assert.Equal(info.LastWriteTimeUtc, asset.MtimeUtc);
            Assert.Equal(probe.Info, asset.Info);
            Assert.Matches(FingerprintRegex(), asset.Fingerprint);
            Assert.Equal(await MediaImporter.ComputeFingerprintAsync(file, CancellationToken.None), asset.Fingerprint);

            var render = kit.Catalog.Get(asset.Id);
            Assert.NotNull(render);
            Assert.Equal(asset, render.Asset);
            Assert.Equal(asset.Path, render.Original.Path);
            Assert.Equal(asset.Info.FormatStartMicros, render.Original.FormatStartMicros);
            Assert.Null(render.Proxy);
            Assert.True(kit.Catalog.IsIndexReady(asset.Id));

            if (asset.Info.Video is null)
            {
                Assert.Null(result.PosterPath);
                continue;
            }

            if (!asset.Info.IsStill && file != _media.Oracle25AviBFrames)
            {
                // The B-frame AVI has no usable packet index on purpose (TECH_BRIEF 6.4: CFR fallback).
                Assert.NotNull(render.Original.Index);
            }

            if (file == _media.Oracle25AviBFrames)
            {
                Assert.Contains("VFR-unsafe", result.Warning, StringComparison.Ordinal);
            }
            else
            {
                Assert.True(result.Warning is null, $"{file}: {result.Warning}");
            }
            Assert.NotNull(result.PosterPath);
            Assert.Equal(kit.Posters.PathFor(asset), result.PosterPath);
            var bytes = await File.ReadAllBytesAsync(result.PosterPath);
            Assert.True(bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8, $"{result.PosterPath} is not a JPEG.");
            var (w, h) = await ImageSizeAsync(result.PosterPath);
            Assert.True(w <= 320 && h <= 180 && (w == 320 || h == 180 || w == 319 || h == 179), $"{file}: poster {w}x{h}");
            var display = asset.Info.Video;
            Assert.True(Math.Abs(((double)w / h) - ((double)display.DisplayWidth / display.DisplayHeight)) < 0.05, $"{file}: poster {w}x{h} vs {display.DisplayWidth}x{display.DisplayHeight}");
        }

        Assert.Empty(Directory.EnumerateFiles(kit.Posters.PosterDirectory, "*.tmp-*"));
    }

    [FfmpegFact]
    public async Task TheOddPathImportsAndSharesTheFingerprintOfItsCopy()
    {
        await using var kit = await PreviewKit.CreateAsync();
        var odd = await kit.Importer.ImportAsync(_media.OddPathClip, CancellationToken.None);
        var original = await kit.Importer.ImportAsync(_media.Cfr25Av, CancellationToken.None);

        Assert.Equal(Path.GetFullPath(_media.OddPathClip), odd.Asset.Path);
        Assert.Equal(original.Asset.Fingerprint, odd.Asset.Fingerprint);
        Assert.NotEqual(original.Asset.Id, odd.Asset.Id);
        Assert.Equal(original.PosterPath, odd.PosterPath);
        Assert.True(File.Exists(odd.PosterPath));
        Assert.NotNull(kit.Catalog.Get(odd.Asset.Id));
    }

    [FfmpegFact]
    public async Task AudioOnlyMediaHasNoPoster()
    {
        await using var kit = await PreviewKit.CreateAsync();
        foreach (var file in new[] { _media.Tone, _media.Impulse })
        {
            var result = await kit.Importer.ImportAsync(file, CancellationToken.None);
            Assert.Null(result.Asset.Info.Video);
            Assert.NotNull(result.Asset.Info.Audio);
            Assert.Null(result.PosterPath);
            Assert.Null(result.Warning);
            Assert.Null(await kit.Posters.GetOrCreateAsync(result.Asset, CancellationToken.None));
            Assert.NotNull(kit.Catalog.Get(result.Asset.Id));
        }
    }

    [FfmpegFact]
    public async Task AnUnsupportedFileIsRejected()
    {
        await using var kit = await PreviewKit.CreateAsync();
        var text = Path.Combine(kit.Harness.Root, "notes.mp4");
        await File.WriteAllTextAsync(text, "not media at all");

        await Assert.ThrowsAsync<FfmpegException>(() => kit.Importer.ImportAsync(text, CancellationToken.None));
        await Assert.ThrowsAsync<FileNotFoundException>(() => kit.Importer.ImportAsync(text + ".missing", CancellationToken.None));
        Assert.Empty(kit.Catalog.Assets);
    }

    [FfmpegFact]
    public async Task ACatalogRegistrationWithoutIndexIndexesInTheBackground()
    {
        await using var kit = await PreviewKit.CreateAsync();
        var imported = await kit.Importer.ImportAsync(_media.OracleVfr, CancellationToken.None);
        using var catalog = new MediaCatalog(kit.Probe, kit.FrameIndex);
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.IndexReady += id => ready.TrySetResult(id);

        catalog.Sync(new Cutlass.Core.Model.Project(1, [imported.Asset], EmptySequence()));
        Assert.Equal(imported.Asset.Id, await ready.Task.WaitAsync(TimeSpan.FromSeconds(60)));
        await catalog.WaitForIndexAsync(imported.Asset.Id, CancellationToken.None);

        var info = catalog.Get(imported.Asset.Id);
        Assert.NotNull(info);
        Assert.True(kit.Catalog.Get(imported.Asset.Id)!.Original.Index!.Pts.SequenceEqual(info.Original.Index!.Pts));

        // A missing file is offline: the compiler gets null instead of a path ffmpeg cannot open.
        var missing = imported.Asset with { Id = "m_missing", Path = imported.Asset.Path + ".gone" };
        catalog.Register(missing);
        Assert.False(catalog.IsOnline("m_missing"));
        Assert.Null(catalog.Get("m_missing"));
        Assert.True(catalog.Remove("m_missing"));
    }

    [FfmpegFact]
    public async Task KnownOfflineMediaRegisterOfflineWithoutAFileLookup()
    {
        await using var kit = await PreviewKit.CreateAsync();
        var imported = await kit.Importer.ImportAsync(_media.OracleVfr, CancellationToken.None);
        using var catalog = new MediaCatalog(kit.Probe, kit.FrameIndex);

        // DESIGN 11: media offline (or unconfirmed) when the project opened stay offline although the file exists.
        var untouched = imported.Asset with { Id = "m_untouched" };
        catalog.KnownOffline = id => id == "m_untouched";
        catalog.Register(untouched);
        Assert.False(catalog.IsOnline("m_untouched"));
        Assert.Null(catalog.Get("m_untouched"));

        // Once released (relinked or confirmed), a fresh registration looks the file up again.
        catalog.KnownOffline = null;
        Assert.True(catalog.Remove("m_untouched"));
        catalog.Register(untouched, null);
        Assert.True(catalog.IsOnline("m_untouched"));
    }

    private static Cutlass.Core.Model.Sequence EmptySequence() =>
        new(new Cutlass.Core.Model.SequenceSettings(320, 180, Cutlass.Core.Time.FrameRate.Fps25), [], [], null, null, false);

    private static async Task<(int Width, int Height)> ImageSizeAsync(string file)
    {
        var result = await TestProcess.RunAsync(
            TestFfmpeg.FfprobePath,
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", file],
            Path.GetTempPath(),
            TimeSpan.FromSeconds(30));
        Assert.True(result.ExitCode == 0, result.Stderr);
        var parts = result.Stdout.Trim().Split(',');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    [GeneratedRegex("^sha256:[0-9a-f]{64}$")]
    private static partial Regex FingerprintRegex();
}
