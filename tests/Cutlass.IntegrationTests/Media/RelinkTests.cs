using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.IntegrationTests.PreviewTests;
using Cutlass.Media.Cache;
using Cutlass.Media.Projects;
using Cutlass.Media.Services;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Media;

/// <summary>
/// Relink and the media link check on open against real files and ffprobe (DESIGN 11): a folder search finds a moved
/// clip by name without extension, the re-probe reads the new source and <c>TimelineEdits.ImportMedia</c> trims the
/// clips to its shorter length in one undo step.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class RelinkTests
{
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public RelinkTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task FolderSearchRelinksAndReprobesAndTrimsClipsToTheShorterSource()
    {
        await using var kit = await PreviewKit.CreateAsync();
        using var temp = new TempDirectory("cutlass-relink");
        var oldClip = Copy(_media.Cfr25Av, temp.Combine("Shoot", "clip.mp4"));
        var asset = (await kit.Importer.ImportAsync(oldClip, CancellationToken.None)).Asset;

        // 4 s at 25 fps: the clips use the whole source.
        var project = Project(
            320,
            180,
            FrameRate.Fps25,
            [asset],
            Track("tr_v1", TrackKind.Video, [V("c_v", 0, 100, asset.Id, 0)]),
            Track("tr_a1", TrackKind.Audio, [A("c_a", 0, 100, asset.Id, 0)]));
        var projectPath = temp.Combine("Projects", "edit.cutlass");
        var store = new ProjectStore();
        await store.SaveAsync(project, projectPath);

        // The shoot folder is gone; a 3 s re-encode with another extension sits in a backup tree.
        File.Delete(oldClip);
        var newClip = Copy(_media.Cfr2997Av, temp.Combine("Backup", "day 1", "clip.mkv"));

        var scan = await MediaRelinker.ScanAsync(await store.LoadAsync(projectPath), projectPath);
        Assert.Equal(MediaLinkState.Offline, scan.State(asset.Id));
        Assert.Equal([asset.Id], scan.OfflineIds);

        var relinker = new MediaRelinker(kit.Probe, new Fingerprinter(null));
        var matches = await relinker.SearchFolderAsync([.. scan.Project.Media], temp.Combine("Backup"));
        Assert.Equal(newClip, matches[asset.Id]);

        var updated = await relinker.RefreshAsync(scan.Project.Media[0], matches[asset.Id]);
        Assert.Equal(asset.Id, updated.Id);
        Assert.Equal(newClip, updated.Path);
        Assert.InRange(updated.Info.DurationMicros, 2_900_000, 3_100_000);
        Assert.Equal(new FileInfo(newClip).Length, updated.SizeBytes);
        Assert.NotEqual(asset.Fingerprint, updated.Fingerprint);

        var session = new ProjectSession(scan.Project, () => 0);
        session.Execute(p => TimelineEdits.ImportMedia(p, [updated]));

        Assert.Equal(1, session.UndoCount);
        Assert.True(TimelineValidator.Check(session.Current).IsEmpty);
        Assert.Equal(newClip, session.Current.Media.Single().Path);
        // About 3 s of source (the container may run a little longer) fill about 75 frames at 25 fps, not 100.
        var sourceFrames = (long)Math.Ceiling(updated.Info.DurationMicros / 40_000.0);
        foreach (var clip in session.Current.Sequence.Tracks.SelectMany(t => t.Clips))
        {
            Assert.InRange(clip.Duration, 70, sourceFrames);
        }

        Assert.True(session.Undo());
        Assert.Equal(100, session.Current.Sequence.Tracks.SelectMany(t => t.Clips).Max(c => c.Duration));
    }

    [FfmpegFact]
    public async Task AChangedFileIsDetectedAndReprobed()
    {
        await using var kit = await PreviewKit.CreateAsync();
        using var temp = new TempDirectory("cutlass-relink");
        var path = Copy(_media.Cfr25Av, temp.Combine("clip.mp4"));
        var asset = (await kit.Importer.ImportAsync(path, CancellationToken.None)).Asset;

        File.Copy(_media.Cfr2997Av, path, overwrite: true);
        File.SetLastWriteTimeUtc(path, asset.MtimeUtc.AddMinutes(5));

        Assert.Equal(MediaLinkState.Changed, MediaRelinker.Classify(asset, MediaFileFacts.Of(path)));
        var fresh = await new MediaRelinker(kit.Probe, null).RefreshAsync(asset, path);
        Assert.NotEqual(asset.Info, fresh.Info);
        Assert.NotEqual(asset.Fingerprint, fresh.Fingerprint);
        Assert.Equal(MediaLinkState.Online, MediaRelinker.Classify(fresh, MediaFileFacts.Of(path)));
    }

    [Fact]
    public async Task AProjectMovedTogetherWithItsMediaRelinksThroughTheRelativePath()
    {
        using var temp = new TempDirectory("cutlass-relink");
        var original = temp.Combine("Work", "Edit");
        var media = Path.Combine(original, "media", "clip ü [1].mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(media)!);
        await File.WriteAllTextAsync(media, "not really a movie");
        var info = new FileInfo(media);
        var asset = new MediaAsset(
            "m_1",
            media,
            null,
            await MediaImporter.ComputeFingerprintAsync(media, CancellationToken.None),
            info.Length,
            info.LastWriteTimeUtc,
            new MediaInfo(4_000_000, 0, "mov,mp4,m4a,3gp,3g2,mj2", false, null, new AudioStreamInfo(0, "aac", 48_000, 2)));
        var project = TimelineEdits.NewProject(RandomIdGenerator.Shared) with { Media = [asset] };
        var store = new ProjectStore();
        await store.SaveAsync(project, Path.Combine(original, "edit.cutlass"));
        Assert.Contains("\"relativePath\": \"media/clip ü [1].mp4\"", await File.ReadAllTextAsync(Path.Combine(original, "edit.cutlass")), StringComparison.Ordinal);

        var moved = temp.Combine("Archive", "Edit 2026");
        Directory.CreateDirectory(temp.Combine("Archive"));
        Directory.Move(original, moved);
        var movedProject = Path.Combine(moved, "edit.cutlass");

        var scan = await MediaRelinker.ScanAsync(await store.LoadAsync(movedProject), movedProject);

        Assert.Equal(MediaLinkState.Online, scan.State("m_1"));
        Assert.Equal(["m_1"], scan.MovedIds);
        Assert.Equal(Path.Combine(moved, "media", "clip ü [1].mp4"), scan.Project.Media[0].Path);
    }

    private static string Copy(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        return target;
    }
}
