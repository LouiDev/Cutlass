using Cutlass.Core.Model;
using Cutlass.Core.Serialization;
using Cutlass.Media.Projects;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Media;

/// <summary>Relink matching and the media link check on open (DESIGN 11): pure parts, no ffmpeg.</summary>
public sealed class MediaRelinkerTests
{
    private static readonly DateTime Mtime = new(2026, 10, 1, 9, 12, 44, DateTimeKind.Utc);

    private static string Root => OperatingSystem.IsWindows() ? @"D:\" : "/data/";

    private static string P(params string[] parts) => Path.Combine([Root, .. parts]);

    private static MediaAsset Asset(string id, string path, long size = 1_000_000) =>
        TestMedia.Asset(id, path, 4_000_000, video: true, audio: true) with { SizeBytes = size, MtimeUtc = Mtime };

    private static Project ProjectWith(params MediaAsset[] media) => new ProjectBuilder().Build() with { Media = [.. media] };

    [Fact]
    public void ClassifySeparatesOnlineChangedAndOffline()
    {
        var asset = Asset("m_a", P("a.mp4"));

        Assert.Equal(MediaLinkState.Offline, MediaRelinker.Classify(asset, null));
        Assert.Equal(MediaLinkState.Online, MediaRelinker.Classify(asset, new MediaFileFacts(1_000_000, Mtime)));

        // FAT and exFAT copies keep only 2 s of mtime precision.
        Assert.Equal(MediaLinkState.Online, MediaRelinker.Classify(asset, new MediaFileFacts(1_000_000, Mtime.AddSeconds(2))));
        Assert.Equal(MediaLinkState.Changed, MediaRelinker.Classify(asset, new MediaFileFacts(1_000_000, Mtime.AddSeconds(3))));
        Assert.Equal(MediaLinkState.Changed, MediaRelinker.Classify(asset, new MediaFileFacts(999_999, Mtime)));
    }

    [Fact]
    public void ScanPrefersTheAbsolutePathThenTheProjectRelativePath()
    {
        var projectFile = P("Moved", "Project", "edit.cutlass");
        var atAbsolute = Asset("m_abs", P("Footage", "abs.mp4"));
        var moved = Asset("m_rel", P("Old", "Footage", "rel.mp4")) with { RelativePath = "../Footage/rel.mp4" };
        var missing = Asset("m_off", P("Gone", "off.mp4")) with { RelativePath = "../Nowhere/off.mp4" };
        var changed = Asset("m_chg", P("Footage", "chg.mp4"));
        var relocated = P("Moved", "Footage", "rel.mp4");
        var files = new Dictionary<string, MediaFileFacts>(StringComparer.Ordinal)
        {
            [atAbsolute.Path] = new(1_000_000, Mtime),
            [relocated] = new(1_000_000, Mtime),
            [changed.Path] = new(2_000_000, Mtime),
        };

        var scan = MediaRelinker.Scan(ProjectWith(atAbsolute, moved, missing, changed), projectFile, p => files.GetValueOrDefault(p));

        Assert.Equal(MediaLinkState.Online, scan.State("m_abs"));
        Assert.Equal(MediaLinkState.Online, scan.State("m_rel"));
        Assert.Equal(MediaLinkState.Offline, scan.State("m_off"));
        Assert.Equal(MediaLinkState.Changed, scan.State("m_chg"));
        Assert.Equal(["m_rel"], scan.MovedIds);
        Assert.Equal(["m_off"], scan.OfflineIds);
        Assert.Equal(["m_chg"], scan.ChangedIds);
        Assert.Equal(relocated, scan.Project.Media.Single(m => m.Id == "m_rel").Path);
        Assert.Equal(atAbsolute, scan.Project.Media.Single(m => m.Id == "m_abs"));
    }

    [Fact]
    public void ScanWithoutChangesKeepsTheProjectReference()
    {
        var asset = Asset("m_a", P("a.mp4"));
        var project = ProjectWith(asset);

        var scan = MediaRelinker.Scan(project, null, _ => new MediaFileFacts(1_000_000, Mtime));

        Assert.Same(project, scan.Project);
        Assert.Empty(scan.MovedIds);
    }

    [Fact]
    public void MatchByNameUsesTheNameThenTheNameWithoutExtension()
    {
        var byName = Asset("m_1", P("Old", "beach.mp4"));
        var byStem = Asset("m_2", P("Old", "sunset.mov"));
        var none = Asset("m_3", P("Old", "nothing.mp4"));
        string[] files = [P("New", "beach.mp4"), P("New", "sub", "sunset.mkv"), P("New", "other.mp4")];

        var matches = MediaRelinker.MatchByName([byName, byStem, none], files, null, ignoreCase: false);

        Assert.Equal(P("New", "beach.mp4"), matches["m_1"]);
        Assert.Equal(P("New", "sub", "sunset.mkv"), matches["m_2"]);
        Assert.False(matches.ContainsKey("m_3"));
    }

    [Fact]
    public void AnExactNameWinsOverAStemMatchAndTheRecordedSizeBreaksTies()
    {
        var asset = Asset("m_1", P("Old", "clip.mp4"), size: 500);
        string[] files = [P("A", "clip.mov"), P("B", "long", "path", "clip.mp4"), P("C", "clip.mp4")];
        var sizes = new Dictionary<string, long> { [P("B", "long", "path", "clip.mp4")] = 500, [P("C", "clip.mp4")] = 400 };

        var matches = MediaRelinker.MatchByName([asset], files, f => sizes.TryGetValue(f, out var s) ? s : null, ignoreCase: false);

        Assert.Equal(P("B", "long", "path", "clip.mp4"), matches["m_1"]);
    }

    [Fact]
    public void EachFileIsUsedOnce()
    {
        var first = Asset("m_1", P("Card1", "C0001.MP4"));
        var second = Asset("m_2", P("Card2", "C0001.MP4"));
        string[] files = [P("New", "C0001.MP4")];

        var matches = MediaRelinker.MatchByName([first, second], files, null, ignoreCase: false);

        Assert.Single(matches);
        Assert.Equal(P("New", "C0001.MP4"), matches["m_1"]);
    }

    [Fact]
    public void NamesCompareCaseInsensitivelyOnlyWhenAsked()
    {
        var asset = Asset("m_1", P("Old", "Beach.MP4"));
        string[] files = [P("New", "beach.mp4")];

        Assert.Empty(MediaRelinker.MatchByName([asset], files, null, ignoreCase: false));
        Assert.Equal(P("New", "beach.mp4"), MediaRelinker.MatchByName([asset], files, null, ignoreCase: true)["m_1"]);
    }

    [Fact]
    public void FileNameOfReadsForwardSlashPathsFromAnyOs()
    {
        Assert.Equal("beach.mp4", MediaRelinker.FileNameOf("D:/Footage/beach.mp4"));
        Assert.Equal("beach.mp4", MediaRelinker.FileNameOf("/home/me/beach.mp4"));
        Assert.Equal("beach.mp4", MediaRelinker.FileNameOf("beach.mp4"));
    }

    [Fact]
    public void ProjectFilesStoreForwardSlashesAndLoadingRestoresNativeSeparators()
    {
        var projectFile = P("Projects", "edit.cutlass");
        var media = Asset("m_1", P("Footage", "Day 1", "beach.mp4"));
        var json = ProjectSerializer.Serialize(ProjectWith(media), projectFile);

        Assert.DoesNotContain("\\\\", json, StringComparison.Ordinal);
        Assert.Contains("\"relativePath\": \"../Footage/Day 1/beach.mp4\"", json, StringComparison.Ordinal);

        var loaded = ProjectStore.WithNativePaths(ProjectSerializer.Deserialize(json));
        Assert.Equal(media.Path, loaded.Media[0].Path);
        Assert.Equal("../Footage/Day 1/beach.mp4", loaded.Media[0].RelativePath);
        // Both lookups name the same file here, so it is listed once.
        Assert.Equal([media.Path], ProjectSerializer.CandidateMediaPaths(loaded.Media[0], projectFile).ToArray());
    }

    [Fact]
    public void NativePathsAreLeftAloneWhenNothingNeedsConverting()
    {
        var project = ProjectWith(Asset("m_1", P("a.mp4")));

        Assert.Same(project, ProjectStore.WithNativePaths(project));
    }

    [Fact]
    public void ARelativePathResolvesAgainstTheProjectFolder()
    {
        var asset = Asset("m_1", P("Gone", "beach.mp4")) with { RelativePath = "media/beach.mp4" };

        var candidates = ProjectSerializer.CandidateMediaPaths(asset, P("Projects", "edit.cutlass"));

        Assert.Equal([asset.Path, P("Projects", "media", "beach.mp4")], candidates.ToArray());
    }
}
