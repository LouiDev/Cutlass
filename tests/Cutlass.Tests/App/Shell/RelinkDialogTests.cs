using Cutlass.App.ViewModels;
using Cutlass.Core.Model;
using Cutlass.Media.Projects;
using Cutlass.Media.Services;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.App.Shell;

/// <summary>The Relink dialog (DESIGN 11) against real files in a temp folder, without ffprobe.</summary>
public sealed class RelinkDialogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cutlass-relink-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly List<IReadOnlyList<MediaAsset>> _applied = [];

    public RelinkDialogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public async Task SearchFolderMatchesByNameThenByNameWithoutExtension()
    {
        var clip = Write("New", "deep", "clip.mp4", "clip content");
        var other = Write("New", "other.mov", "other content");
        var dialog = Dialog(Offline("m_clip", "clip.mp4"), Offline("m_other", "other.mp4"), Offline("m_none", "none.mp4"));

        var found = await dialog.SearchInFolderAsync(Path.Combine(_root, "New"));

        Assert.Equal(2, found);
        Assert.Equal(clip, Item(dialog, "m_clip").NewPath);
        Assert.Equal(other, Item(dialog, "m_other").NewPath);
        Assert.Null(Item(dialog, "m_none").NewPath);
        Assert.Equal("2 of 3 files found", dialog.StatusText);
        Assert.True(dialog.RelinkCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARenamedFileIsFoundBySizeAndFingerprint()
    {
        var renamed = Write("New", "renamed by the camera app.mp4", "the very same bytes");
        var decoy = Write("New", "same size other bytes.mp4", "the very same BYTES");
        var asset = Offline("m_a", "original.mp4") with
        {
            SizeBytes = new FileInfo(renamed).Length,
            Fingerprint = await MediaImporter.ComputeFingerprintAsync(renamed, CancellationToken.None),
        };
        Assert.Equal(new FileInfo(decoy).Length, asset.SizeBytes);
        var dialog = Dialog(asset);

        Assert.Equal(1, await dialog.SearchInFolderAsync(Path.Combine(_root, "New")));
        Assert.Equal(renamed, Item(dialog, "m_a").NewPath);
    }

    [Fact]
    public async Task LocatingOneFileResolvesTheOthersInItsFolder()
    {
        var a = Write("Card", "a.mp4", "a");
        var b = Write("Card", "b.mp4", "b");
        var dialog = Dialog(Offline("m_a", "a.mp4"), Offline("m_b", "b.mp4"), Offline("m_c", "c.mp4"));

        await dialog.LocateFileAsync(Item(dialog, "m_a"), a);

        Assert.Equal(a, Item(dialog, "m_a").NewPath);
        Assert.Equal(b, Item(dialog, "m_b").NewPath);
        Assert.Null(Item(dialog, "m_c").NewPath);
        Assert.Equal("2 of 3 files found", dialog.StatusText);
    }

    [Fact]
    public async Task RelinkAppliesEveryReadableFileInOneCallAndKeepsFailures()
    {
        var good = Write("New", "good.mp4", "good bytes");
        var gone = Write("New", "gone.mp4", "deleted before relink");
        var dialog = Dialog(Offline("m_good", "good.mp4"), Offline("m_gone", "gone.mp4"));
        var closed = false;
        dialog.CloseRequested += (_, _) => closed = true;
        await dialog.SearchInFolderAsync(Path.Combine(_root, "New"));
        File.Delete(gone);

        var updated = await dialog.ApplyResolvedAsync();

        var asset = Assert.Single(Assert.Single(_applied));
        Assert.Same(asset, Assert.Single(updated));
        Assert.Equal("m_good", asset.Id);
        Assert.Equal(good, asset.Path);
        Assert.Null(asset.RelativePath);
        Assert.Equal(new FileInfo(good).Length, asset.SizeBytes);
        Assert.Equal(await MediaImporter.ComputeFingerprintAsync(good, CancellationToken.None), asset.Fingerprint);
        Assert.True(dialog.Applied);

        // The failed row stays with its reason, so the dialog stays open.
        var failed = Assert.Single(dialog.Items);
        Assert.Equal("m_gone", failed.Id);
        Assert.True(failed.HasError);
        Assert.False(closed);
    }

    [Fact]
    public async Task RelinkClosesTheDialogWhenEverythingApplied()
    {
        var file = Write("New", "clip.mp4", "bytes");
        var dialog = Dialog(Offline("m_clip", "clip.mp4"));
        var closed = false;
        dialog.CloseRequested += (_, _) => closed = true;

        await dialog.LocateFileAsync(Item(dialog, "m_clip"), file);
        await dialog.RelinkCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.Empty(dialog.Items);
        Assert.Equal("Every media file is linked.", dialog.StatusText);
    }

    [Fact]
    public void RowsDescribeTheirState()
    {
        var row = new RelinkItemViewModel(Offline("m_a", "a.mp4"), MediaLinkState.Offline);

        Assert.Equal("a.mp4", row.Name);
        Assert.True(row.IsOffline);
        Assert.Contains("Offline", row.StatusText, StringComparison.Ordinal);
        row.NewPath = "x";
        Assert.Equal("Found: x", row.StatusText);
        row.Error = "bad";
        Assert.Equal("bad", row.StatusText);
    }

    [Fact]
    public async Task UnconfirmedRowsAreLookedUpOnlyWhenConfirmed()
    {
        var there = Offline("m_there", "there.mp4");
        var gone = Offline("m_gone", "gone.mp4");
        var local = Offline("m_local", "local.mp4");
        Write("Old", "there.mp4", "remote bytes");
        var confirmed = new List<IReadOnlyCollection<string>>();
        var dialog = new RelinkDialogViewModel(
            [(there, MediaLinkState.Offline), (gone, MediaLinkState.Offline), (local, MediaLinkState.Offline)],
            new MediaRelinker(probe: null, fingerprints: null),
            new FakeDialogService(),
            updated => _applied.Add(updated),
            _root,
            ["m_there", "m_gone"],
            ids =>
            {
                // The real flow scans the project with the confirmed ids (MediaRelinker.ScanAsync).
                confirmed.Add(ids);
                var project = new ProjectBuilder().Build() with { Media = [there, gone, local] };
                return Task.FromResult(MediaRelinker.Scan(project, null, MediaFileFacts.Of, _ => true, ids));
            });

        Assert.True(dialog.HasUnconfirmed);
        Assert.True(Item(dialog, "m_there").IsUnconfirmed);
        Assert.False(Item(dialog, "m_local").IsUnconfirmed);
        Assert.StartsWith("Not checked", Item(dialog, "m_there").StatusText, StringComparison.Ordinal);
        Assert.True(dialog.ConfirmLocationCommand.CanExecute(Item(dialog, "m_there")));
        Assert.False(dialog.ConfirmLocationCommand.CanExecute(Item(dialog, "m_local")));
        Assert.Empty(confirmed);

        await dialog.ConfirmLocationCommand.ExecuteAsync(Item(dialog, "m_there"));
        Assert.Equal(["m_there"], confirmed.Single());
        Assert.Equal(there.Path, Item(dialog, "m_there").NewPath);
        Assert.False(Item(dialog, "m_there").IsUnconfirmed);
        Assert.True(Item(dialog, "m_gone").IsUnconfirmed);

        Assert.Equal(0, await dialog.ConfirmLocationsAsync([Item(dialog, "m_gone")]));
        Assert.False(dialog.HasUnconfirmed);
        Assert.Null(Item(dialog, "m_gone").NewPath);
        Assert.Equal("Offline: the file is not at this location", Item(dialog, "m_gone").StatusText);
        Assert.Equal("1 of 3 files found", dialog.StatusText);
    }

    [Fact]
    public void WithoutAConfirmCallbackNothingCanBeConfirmed()
    {
        var asset = Offline("m_a", "a.mp4");
        var dialog = new RelinkDialogViewModel(
            [(asset, MediaLinkState.Offline)],
            new MediaRelinker(probe: null, fingerprints: null),
            new FakeDialogService(),
            updated => _applied.Add(updated),
            _root,
            ["m_a"]);
        Assert.False(dialog.ConfirmLocationCommand.CanExecute(Item(dialog, "m_a")));
    }

    private static RelinkItemViewModel Item(RelinkDialogViewModel dialog, string id) => dialog.Items.Single(i => i.Id == id);

    private RelinkDialogViewModel Dialog(params MediaAsset[] assets) =>
        new(
            assets.Select(a => (a, MediaLinkState.Offline)),
            new MediaRelinker(probe: null, fingerprints: null),
            new FakeDialogService(),
            updated => _applied.Add(updated),
            _root);

    private MediaAsset Offline(string id, string name) =>
        TestMedia.Asset(id, Path.Combine(_root, "Old", name), 4_000_000, video: true, audio: true);

    private string Write(params string[] partsAndContent)
    {
        var path = Path.Combine([_root, .. partsAndContent[..^1]]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, partsAndContent[^1]);
        return path;
    }
}
