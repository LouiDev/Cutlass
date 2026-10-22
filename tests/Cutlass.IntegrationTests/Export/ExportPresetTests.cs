using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// Every available preset produces a file ffprobe can read with the expected codec (TECH_BRIEF 9 and 11.2), with
/// BT.709 tags on video and exact frame and sample counts where the format keeps them.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class ExportPresetTests
{
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public ExportPresetTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegTheory]
    [InlineData("h264", "h264", "aac")]
    [InlineData("h265", "hevc", "aac")]
    [InlineData("vp9", "vp9", "opus")]
    [InlineData("av1", "av1", "aac")]
    [InlineData("prores", "prores", "pcm_s16le")]
    [InlineData("gif", "gif", null)]
    [InlineData("mp3", null, "mp3")]
    [InlineData("aac", null, "aac")]
    [InlineData("wav", null, "pcm_s16le")]
    [InlineData("flac", null, "flac")]
    public async Task PresetProducesAReadableFile(string presetId, string? videoCodec, string? audioCodec)
    {
        await using var kit = await ExportKit.CreateAsync();
        var preset = ExportPresets.Find(presetId)!;
        if (!preset.IsAvailable(kit.Capabilities))
        {
            // Greyed out on this build (for example a distro ffmpeg without libsvtav1 or libaom); nothing to export.
            Assert.NotNull(preset.UnavailableReason(kit.Capabilities));
            return;
        }

        var asset = await kit.AssetAsync("m_cfr", _media.Cfr25Av);
        var catalog = await kit.CatalogAsync([asset]);
        var project = Project(
            320,
            180,
            FrameRate.Fps30,
            [asset],
            Track("tr_v1", TrackKind.Video, [V("c_v", 0, 30, "m_cfr", Micros(500_000))]),
            Track("tr_a1", TrackKind.Audio, [A("c_a", 0, 30, "m_cfr", Micros(500_000))]));
        var (width, height) = preset.Kind == ExportPresetKind.Gif
            ? ExportPresets.CanvasForWidth(project.Sequence.Settings, ExportPresets.GifWidths[0])
            : (320, 180);

        var job = await kit.ExportAsync(kit.Request(project, catalog, preset, "preset_" + presetId, 0, 30, width, height, ExportQuality.Draft));

        ExportKit.AssertCompleted(job);
        Assert.EndsWith(preset.Extension, job.OutputPath, StringComparison.Ordinal);
        if (videoCodec is not null)
        {
            var video = await ExportKit.StreamInfoAsync(job.OutputPath, "v");
            Assert.Equal(videoCodec, video["codec_name"]);
            Assert.Equal(width.ToString(System.Globalization.CultureInfo.InvariantCulture), video["width"]);
            if (preset.Kind == ExportPresetKind.Video)
            {
                Assert.Equal(30, await ExportKit.CountFramesAsync(job.OutputPath));
                Assert.Equal("bt709", video["color_space"]);
                Assert.Equal("bt709", video["color_transfer"]);
                Assert.Equal("bt709", video["color_primaries"]);
            }
            else
            {
                // GIF runs at 15 fps: 1 s of timeline gives 15 frames.
                Assert.Equal(job.Render!.FrameCount, await ExportKit.CountFramesAsync(job.OutputPath));
                Assert.Equal(15, job.Render.FrameCount);
            }
        }

        if (audioCodec is not null)
        {
            var audio = await ExportKit.StreamInfoAsync(job.OutputPath, "a");
            Assert.Equal(audioCodec, audio["codec_name"]);
            Assert.Equal("48000", audio["sample_rate"]);
            Assert.Equal("2", audio["channels"]);
            if (presetId is "wav" or "flac" or "prores")
            {
                Assert.Equal(48_000, (await ExportKit.DecodeAudioAsync(job.OutputPath)).Length / 2);
            }
        }
        else
        {
            Assert.Null(job.Render!.AudioLabel);
        }
    }

    [FfmpegFact]
    public async Task EveryPresetIsAvailableOnTheReferenceBuild()
    {
        // The Windows reference build (gpl master) has every encoder; a missing one would grey out a DESIGN 9 preset.
        await using var kit = await ExportKit.CreateAsync();
        if (!kit.Capabilities.BuildId.StartsWith("N-", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var preset in ExportPresets.All)
        {
            Assert.True(preset.IsAvailable(kit.Capabilities), $"{preset.DisplayName}: {preset.UnavailableReason(kit.Capabilities)}");
        }

        Assert.Equal("libx264", ExportPresets.H264.Resolve(kit.Capabilities)!.Video!.Encoder);
        Assert.Equal("libsvtav1", ExportPresets.Av1.Resolve(kit.Capabilities)!.Video!.Encoder);
        Assert.Equal("libopus", ExportPresets.Vp9.Resolve(kit.Capabilities)!.Audio!.Encoder);
    }
}
