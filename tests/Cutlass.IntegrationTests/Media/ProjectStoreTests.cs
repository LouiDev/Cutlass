using Cutlass.Core.Editing;
using Cutlass.Core.Serialization;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Projects;

namespace Cutlass.IntegrationTests.Media;

/// <summary>ProjectStore: atomic project, sidecar and autosave files on disk (TECH_BRIEF 3.5).</summary>
public sealed class ProjectStoreTests
{
    [Fact]
    public async Task ProjectRoundTripsThroughAnOddPath()
    {
        using var temp = new TempDirectory("cutlass-projects");
        var path = temp.Combine("it's, [odd] Ä", "My Project.cutlass");
        var store = new ProjectStore();
        var project = TimelineEdits.NewProject(RandomIdGenerator.Shared);

        await store.SaveAsync(project, path);
        var loaded = await store.LoadAsync(path);

        Assert.Equal(ProjectSerializer.Serialize(project, path), ProjectSerializer.Serialize(loaded, path));
        Assert.False(File.Exists(path + ".tmp"));
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.NotEqual(0xEF, bytes[0]); // no BOM
    }

    [Fact]
    public async Task SaveReplacesTheOldFile()
    {
        using var temp = new TempDirectory("cutlass-projects");
        var path = temp.Combine("p.cutlass");
        var store = new ProjectStore();
        await File.WriteAllTextAsync(path, "old content");

        await store.SaveAsync(TimelineEdits.NewProject(RandomIdGenerator.Shared), path);

        Assert.Contains("cutlass-project", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DamagedProjectIsInvalidData()
    {
        using var temp = new TempDirectory("cutlass-projects");
        var path = temp.Combine("broken.cutlass");
        await File.WriteAllTextAsync(path, "{ \"format\": \"something-else\" }");

        await Assert.ThrowsAsync<InvalidDataException>(() => new ProjectStore().LoadAsync(path));
    }

    [Fact]
    public async Task SidecarDefaultsWhenMissingAndRoundTrips()
    {
        using var temp = new TempDirectory("cutlass-projects");
        var path = temp.Combine("p.cutlass");
        var store = new ProjectStore();

        Assert.Equal(
            ProjectSerializer.SerializeViewState(ViewState.Default),
            ProjectSerializer.SerializeViewState(await store.LoadViewStateAsync(path)));

        var state = ViewState.Default with { ScrollFrame = 120 };
        await store.SaveViewStateAsync(state, path);

        Assert.True(File.Exists(ProjectStore.SidecarPath(path)));
        Assert.Equal(
            ProjectSerializer.SerializeViewState(state),
            ProjectSerializer.SerializeViewState(await store.LoadViewStateAsync(path)));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task AutosaveIsWrittenAndDeleted()
    {
        using var temp = new TempDirectory("cutlass-projects");
        var path = temp.Combine("p.cutlass");
        var autosave = ProjectStore.AutosavePath(path);
        var store = new ProjectStore();

        await store.SaveAutosaveAsync(TimelineEdits.NewProject(RandomIdGenerator.Shared), autosave, path);
        Assert.True(File.Exists(autosave));
        Assert.EndsWith(".cutlass.autosave", autosave, StringComparison.Ordinal);
        await store.LoadAsync(autosave);

        store.DeleteAutosave(autosave);
        Assert.False(File.Exists(autosave));
    }
}
