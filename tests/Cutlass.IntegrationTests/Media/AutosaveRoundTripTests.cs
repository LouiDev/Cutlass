using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Serialization;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Projects;

namespace Cutlass.IntegrationTests.Media;

/// <summary>Autosave files on disk and their recovery (DESIGN 11): round trip, newer-than rule, untitled autosaves.</summary>
public sealed class AutosaveRoundTripTests
{
    [Fact]
    public async Task AProjectAutosaveRoundTripsAndIsOfferedWhileNewerThanTheProject()
    {
        using var temp = new TempDirectory("cutlass-autosave");
        var projectPath = temp.Combine("it's, [odd] Ä", "edit.cutlass");
        var media = temp.Combine("it's, [odd] Ä", "media", "clip.mp4");
        var store = new ProjectStore();
        var saved = WithMedia(TimelineEdits.NewProject(RandomIdGenerator.Shared), media);
        await store.SaveAsync(saved, projectPath);
        File.SetLastWriteTimeUtc(projectPath, DateTime.UtcNow.AddMinutes(-10));

        var edited = TimelineEdits.SetInOut(saved, 10, 40).Project;
        var autosave = ProjectStore.AutosavePath(projectPath);
        await store.SaveAutosaveAsync(edited, autosave, projectPath);

        var candidate = AutosaveRecovery.ForProject(projectPath);
        Assert.NotNull(candidate);
        Assert.Equal(Path.GetFullPath(autosave), candidate.AutosavePath);
        Assert.Equal("edit", candidate.Name);

        var recovered = await store.LoadAsync(candidate.AutosavePath);
        Assert.Equal(ProjectSerializer.Serialize(edited, projectPath), ProjectSerializer.Serialize(recovered, projectPath));
        Assert.Equal(10, recovered.Sequence.InFrame);
        Assert.Equal("media/clip.mp4", recovered.Media[0].RelativePath);
        Assert.Equal(Path.GetFullPath(media), recovered.Media[0].Path);
        Assert.False(File.Exists(autosave + ".tmp"));

        // A later save of the project makes the autosave obsolete.
        await store.SaveAsync(recovered, projectPath);
        File.SetLastWriteTimeUtc(autosave, DateTime.UtcNow.AddMinutes(-1));
        Assert.Null(AutosaveRecovery.ForProject(projectPath));
    }

    [Fact]
    public async Task AnUntitledAutosaveKeepsAbsolutePathsAndIsListed()
    {
        using var temp = new TempDirectory("cutlass-autosave");
        var folder = temp.Combine("data", "autosave");
        var media = temp.Combine("footage", "clip.mp4");
        var store = new ProjectStore();
        var project = WithMedia(TimelineEdits.NewProject(RandomIdGenerator.Shared), media);
        var path = AutosaveRecovery.UntitledPath(folder, "abc123");

        await store.SaveAutosaveAsync(project, path, relativeTo: null);

        var listed = Assert.Single(AutosaveRecovery.FindUntitled(folder));
        Assert.Equal(path, listed.AutosavePath);
        Assert.True(listed.IsUntitled);
        var loaded = await store.LoadAsync(path);
        Assert.Equal(Path.GetFullPath(media), loaded.Media[0].Path);
        Assert.Null(loaded.Media[0].RelativePath);

        store.DeleteAutosave(path);
        Assert.Empty(AutosaveRecovery.FindUntitled(folder));
    }

    [Fact]
    public async Task AProjectFromANewerCutlassIsRefusedWithAClearMessage()
    {
        using var temp = new TempDirectory("cutlass-autosave");
        var path = temp.Combine("future.cutlass");
        await File.WriteAllTextAsync(path, "{ \"format\": \"cutlass-project\", \"schemaVersion\": 99, \"media\": [] }");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new ProjectStore().LoadAsync(path));

        Assert.Contains("newer version of Cutlass", error.Message, StringComparison.Ordinal);
    }

    private static Project WithMedia(Project project, string mediaPath) =>
        project with
        {
            Media =
            [
                new MediaAsset(
                    "m_1",
                    Path.GetFullPath(mediaPath),
                    null,
                    "sha256:" + new string('a', 64),
                    1234,
                    new DateTime(2026, 10, 1, 9, 12, 44, DateTimeKind.Utc),
                    new MediaInfo(4_000_000, 0, "wav", false, null, new AudioStreamInfo(0, "pcm_s16le", 48_000, 2))),
            ],
        };
}
