using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// Titles render through drawtext with a bare <c>fontfile</c> and a <c>textfile</c> (TECH_BRIEF 5.5 and 7.3): no
/// fontconfig lookup, and text or fonts with odd characters never reach the graph.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class TitleRenderTests
{
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public TitleRenderTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task BuiltinFontTitleRendersWithoutFontconfig()
    {
        await using var kit = await ExportKit.CreateAsync();
        Assert.True(kit.Capabilities.HasDrawtext);
        var spec = TitleSpec.Default with { Text = "Day One", Size = 64, Anchor = TitleAnchor.Center, OutlineWidth = 0 };
        var project = Project(320, 180, FrameRate.Fps30, [], Track("tr_v1", TrackKind.Video, [Title("c_t", 0, 30, spec)]));

        var job = await kit.ExportAsync(kit.Request(project, new TestRenderCatalog(), ExportPresets.H264, "title", 0, 30));

        ExportKit.AssertCompleted(job);
        Assert.Contains("fontfile=Inter-Bold.ttf", job.Render!.GraphText, StringComparison.Ordinal);
        Assert.DoesNotContain(job.Log, l => l.Contains("fontconfig", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(job.Warnings);
        Assert.True(File.Exists(Path.Combine(kit.Harness.Work.Path, "Inter-Bold.ttf")));
        Assert.True(File.Exists(Path.Combine(kit.Harness.Work.Path, job.Render.AuxFiles[0].FileName)));
        Assert.True(BrightPixels(await ExportKit.DecodeGrayAsync(job.OutputPath), frame: 15) > 200, "The title text is not visible.");
    }

    [FfmpegFact]
    public async Task AnInOutExportResolvesTheFontOfATitlePulledInAcrossATransition()
    {
        // The range starts after the first title, but the crossfade widens the window back into it (TECH_BRIEF 7.5),
        // so its user font must be resolved too, or the compiler falls back to Inter-Regular.
        await using var kit = await ExportKit.CreateAsync();
        var fontPath = Path.Combine(kit.OutputDirectory, "first title.ttf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fonts", "Inter-SemiBold.ttf"), fontPath);
        var first = TitleSpec.Default with { Text = "First", Font = fontPath, Size = 32 };
        var second = TitleSpec.Default with { Text = "Second", Size = 32 };
        var project = Project(
            320,
            180,
            FrameRate.Fps30,
            [],
            Track(
                "tr_v1",
                TrackKind.Video,
                [Title("c_1", 0, 30, first), Title("c_2", 30, 30, second)],
                [new Transition("x_1", TransitionKind.Crossfade, "c_1", "c_2", 10)]));

        var job = await kit.ExportAsync(kit.Request(project, new TestRenderCatalog(), ExportPresets.ProRes, "in_out_title", 30, 40));

        ExportKit.AssertCompleted(job);
        Assert.Matches("fontfile=font_[0-9a-f]{8}\\.ttf", job.Render!.GraphText);
        Assert.Contains("fontfile=Inter-Bold.ttf", job.Render.GraphText, StringComparison.Ordinal);
        Assert.Empty(job.Warnings);
    }

    [FfmpegFact]
    public async Task OddFontPathAndOddTextRender()
    {
        await using var kit = await ExportKit.CreateAsync();
        var fontDirectory = Path.Combine(kit.OutputDirectory, "it's, [odd] Ä 日本");
        Directory.CreateDirectory(fontDirectory);
        var fontPath = Path.Combine(fontDirectory, "my font [1] it's.ttf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fonts", "Inter-SemiBold.ttf"), fontPath);
        var spec = TitleSpec.Default with
        {
            Text = "it's: 50%, [odd] \\ {x} 'quoted'\r\nline: two;",
            Font = fontPath,
            Size = 24,
            Box = true,
            Anchor = TitleAnchor.Top,
            OffsetX = -10,
            OffsetY = 12,
        };
        var cfr = await kit.AssetAsync("m_cfr", _media.Cfr25Av);
        var catalog = await kit.CatalogAsync([cfr]);
        var project = Project(
            320,
            180,
            FrameRate.Fps30,
            [cfr],
            Track("tr_v1", TrackKind.Video, [V("c_v", 0, 30, "m_cfr", 0)]),
            Track("tr_v2", TrackKind.Video, [Title("c_t", 5, 20, spec, 5, 5)]));

        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.ProRes, "odd_title", 0, 30));

        ExportKit.AssertCompleted(job);
        var graph = job.Render!.GraphText;
        Assert.Matches("fontfile=font_[0-9a-f]{8}\\.ttf", graph);
        Assert.DoesNotContain("odd", graph, StringComparison.Ordinal);
        Assert.Empty(job.Warnings);
        var text = Assert.Single(job.Render.AuxFiles);
        Assert.Equal("it's: 50%, [odd] \\ {x} 'quoted'\nline: two;", text.Utf8Content);
        Assert.Equal(text.Utf8Content, await File.ReadAllTextAsync(Path.Combine(kit.Harness.Work.Path, text.FileName)));
        Assert.DoesNotContain(job.Log, l => l.Contains("fontconfig", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(30, await ExportKit.CountFramesAsync(job.OutputPath));
    }

    private static int BrightPixels(byte[] gray, int frame)
    {
        var start = frame * 320 * 180;
        var count = 0;
        for (var i = start; i < start + (320 * 180); i++)
        {
            if (gray[i] > 200)
            {
                count++;
            }
        }

        return count;
    }
}
