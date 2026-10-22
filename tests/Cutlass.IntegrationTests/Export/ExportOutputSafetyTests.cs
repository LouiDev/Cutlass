using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Export;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// An export never destroys a file it did not write and never loses a finished one (TECH_BRIEF 9): a target held open
/// keeps the output under a free name, a stale partial file survives, and media of the project are recognised by file
/// identity, not by spelling.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class ExportOutputSafetyTests
{
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public ExportOutputSafetyTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task ATargetHeldOpenKeepsTheFinishedExportUnderAFreeName()
    {
        await using var kit = await ExportKit.CreateAsync();
        var (project, catalog) = await ToneTimelineAsync(kit);
        var request = kit.Request(project, catalog, ExportPresets.Wav, "held", 0, 30);
        var target = request.FinalPath;
        await File.WriteAllTextAsync(target, "the old export, open in a player");
        File.WriteAllText(Path.Combine(kit.OutputDirectory, "held (1).wav"), "already taken");

        ExportJob job;
        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            job = await kit.ExportAsync(request);
        }

        Assert.Equal(ExportJobState.Completed, job.State);
        Assert.False(File.Exists(job.PartialPath));
        if (OperatingSystem.IsWindows())
        {
            // FileShare.None blocks the replace on Windows, so the export lands under the first free name.
            Assert.Equal(Path.Combine(kit.OutputDirectory, "held (2).wav"), job.OutputPath);
            Assert.Contains(job.Warnings, w => w.Contains("held (2).wav", StringComparison.Ordinal));
            Assert.Equal("the old export, open in a player", await File.ReadAllTextAsync(target));
        }
        else
        {
            // POSIX lets a rename replace a file another handle holds open, so the normal path is used.
            Assert.Equal(target, job.OutputPath);
            Assert.DoesNotContain(job.Warnings, w => w.Contains("could not be replaced", StringComparison.Ordinal));
        }

        var samples = await ExportKit.DecodeAudioAsync(job.OutputPath);
        Assert.Equal(48_000, samples.Length / 2);
    }

    [FfmpegFact]
    public async Task AStalePartialFileIsNeverDeleted()
    {
        await using var kit = await ExportKit.CreateAsync();
        var (project, catalog) = await ToneTimelineAsync(kit);
        var request = kit.Request(project, catalog, ExportPresets.Wav, "stale", 0, 30);
        await File.WriteAllTextAsync(request.PartialPath, "somebody else's file");

        var job = await kit.ExportAsync(request);

        ExportKit.AssertCompleted(job);
        Assert.NotEqual(request.PartialPath, job.PartialPath);
        Assert.Matches(@"stale\.partial-[0-9a-f]{8}\.wav$", job.PartialPath);
        Assert.Equal("somebody else's file", await File.ReadAllTextAsync(request.PartialPath));
    }

    [FfmpegFact]
    public async Task AnOutputSpelledDifferentlyFromAProjectMediaFileIsRefused()
    {
        await using var kit = await ExportKit.CreateAsync();
        var media = Path.Combine(kit.OutputDirectory, "Source.wav");
        File.Copy(_media.Tone, media);
        var asset = await kit.AssetAsync("m_t", media);
        var catalog = await kit.CatalogAsync([asset]);
        var project = Project(320, 180, FrameRate.Fps30, [asset], Track("tr_a1", TrackKind.Audio, [A("c_a", 0, 30, "m_t", 0)]));
        var before = await File.ReadAllBytesAsync(media);

        // Another spelling of the same file: a detour through the folder, and other letter case where that is the same.
        var detour = Path.Combine(kit.OutputDirectory, "sub", "..", OperatingSystem.IsWindows() ? "SOURCE.wav" : "Source.wav");
        Directory.CreateDirectory(Path.Combine(kit.OutputDirectory, "sub"));
        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.Wav, "unused", 0, 30) with { OutputPath = detour });

        Assert.Equal(ExportJobState.Failed, job.State);
        Assert.Contains("media file of this project", job.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(media));
    }

    private async Task<(Project Project, TestRenderCatalog Catalog)> ToneTimelineAsync(ExportKit kit)
    {
        var asset = await kit.AssetAsync("m_t", _media.Tone);
        var catalog = await kit.CatalogAsync([asset]);
        var project = Project(320, 180, FrameRate.Fps30, [asset], Track("tr_a1", TrackKind.Audio, [A("c_a", 0, 30, "m_t", 0)]));
        return (project, catalog);
    }
}
