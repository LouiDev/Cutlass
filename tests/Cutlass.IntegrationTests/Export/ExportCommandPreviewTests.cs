using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Export;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// The export dialog's "Show ffmpeg command" (DESIGN 9) is built along the export's own path: user title fonts are
/// resolved into the work directory, so the preview names the same script and arguments the export then runs.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class ExportCommandPreviewTests
{
    [FfmpegFact]
    public async Task ThePreviewMatchesTheCommandTheExportRuns()
    {
        await using var kit = await ExportKit.CreateAsync();
        var fontDirectory = Path.Combine(kit.OutputDirectory, "user fonts");
        Directory.CreateDirectory(fontDirectory);
        var fontPath = Path.Combine(fontDirectory, "Brand Font.ttf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fonts", "Inter-Regular.ttf"), fontPath);
        var spec = TitleSpec.Default with { Text = "Preview", Font = fontPath, Size = 32 };
        var project = Project(320, 180, FrameRate.Fps30, [], Track("tr_v1", TrackKind.Video, [Title("c_t", 0, 15, spec)]));
        var request = kit.Request(project, new TestRenderCatalog(), ExportPresets.H264, "preview", 0, 15, quality: ExportQuality.Draft);

        var preview = await ExportCommandPreview.BuildAsync(request, kit.Harness.Toolset, kit.Capabilities, kit.Fonts, CancellationToken.None);
        var job = await kit.ExportAsync(request);

        ExportKit.AssertCompleted(job);
        Assert.Matches("fontfile=font_[0-9a-f]{8}\\.ttf", job.Render!.GraphText);
        Assert.Equal(job.DisplayCommand, preview);
        Assert.Contains("-crf 28", preview, StringComparison.Ordinal);
        Assert.Contains("preview.partial.mp4", preview, StringComparison.Ordinal);
    }

    [FfmpegFact]
    public async Task AnUnavailablePresetExplainsItself()
    {
        await using var kit = await ExportKit.CreateAsync();
        var caps = kit.Capabilities with { Encoders = kit.Capabilities.Encoders.Remove("libx265") };
        var project = Project(320, 180, FrameRate.Fps30, [], Track("tr_v1", TrackKind.Video, [Title("c_t", 0, 15, TitleSpec.Default)]));
        var request = kit.Request(project, new TestRenderCatalog(), ExportPresets.H265, "missing", 0, 15);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ExportCommandPreview.BuildAsync(request, kit.Harness.Toolset, caps, kit.Fonts, CancellationToken.None));
        Assert.Equal("libx265 is not in this ffmpeg build", ex.Message);
    }
}
