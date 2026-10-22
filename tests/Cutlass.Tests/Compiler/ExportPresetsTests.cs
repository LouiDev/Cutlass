using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Tests.Compiler;

/// <summary>The presets of DESIGN 9: their table, quality values, availability (greying) and resolution helpers.</summary>
public sealed class ExportPresetsTests
{
    [Fact]
    public void EveryDesignPresetExistsInDialogOrder()
    {
        Assert.Equal(
            ["h264", "h265", "vp9", "av1", "prores", "gif", "mp3", "aac", "wav", "flac"],
            ExportPresets.All.Select(p => p.Id));
        Assert.Same(ExportPresets.H264, ExportPresets.Default);
        Assert.Equal(ExportQuality.High, ExportPresets.DefaultQuality);
        Assert.Same(ExportPresets.Vp9, ExportPresets.Find("VP9"));
        Assert.Null(ExportPresets.Find("divx"));
    }

    [Theory]
    [InlineData("h264", ".mp4", "yuv420p", ExportPresetKind.Video)]
    [InlineData("h265", ".mp4", "yuv420p", ExportPresetKind.Video)]
    [InlineData("vp9", ".webm", "yuv420p", ExportPresetKind.Video)]
    [InlineData("av1", ".mp4", "yuv420p", ExportPresetKind.Video)]
    [InlineData("prores", ".mov", "yuv422p10le", ExportPresetKind.Video)]
    [InlineData("gif", ".gif", null, ExportPresetKind.Gif)]
    [InlineData("mp3", ".mp3", null, ExportPresetKind.Audio)]
    [InlineData("aac", ".m4a", null, ExportPresetKind.Audio)]
    [InlineData("wav", ".wav", null, ExportPresetKind.Audio)]
    [InlineData("flac", ".flac", null, ExportPresetKind.Audio)]
    public void ContainerAndPixelFormat(string id, string extension, string? pixelFormat, ExportPresetKind kind)
    {
        var preset = ExportPresets.Find(id)!;

        Assert.Equal(extension, preset.Extension);
        Assert.Equal(pixelFormat, preset.PixelFormat);
        Assert.Equal(kind, preset.Kind);
        Assert.Equal(kind != ExportPresetKind.Audio, preset.HasVideo);
        Assert.Equal(kind != ExportPresetKind.Gif, preset.HasAudio);
        if (kind == ExportPresetKind.Video)
        {
            // The graph tail's format= matches the preset's -pix_fmt.
            Assert.Equal(OutputTail.ForExport(pixelFormat!), preset.Tail);
            Assert.All(preset.VideoEncoders, e => Assert.Contains("-pix_fmt " + pixelFormat, e.Arguments, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("h264", 28, 23, 20, 17)]
    [InlineData("h265", 30, 26, 23, 20)]
    [InlineData("vp9", 40, 34, 31, 26)]
    [InlineData("av1", 42, 36, 32, 28)]
    [InlineData("mp3", 6, 4, 2, 0)]
    public void QualityTable(string id, int draft, int good, int high, int best)
    {
        var preset = ExportPresets.Find(id)!;

        Assert.True(preset.HasQuality);
        Assert.Equal(draft, preset.QualityValue(ExportQuality.Draft));
        Assert.Equal(good, preset.QualityValue(ExportQuality.Good));
        Assert.Equal(high, preset.QualityValue(ExportQuality.High));
        Assert.Equal(best, preset.QualityValue(ExportQuality.Best));
    }

    [Theory]
    [InlineData("prores")]
    [InlineData("gif")]
    [InlineData("aac")]
    [InlineData("wav")]
    [InlineData("flac")]
    public void FixedQualityPresetsIgnoreTheChoice(string id)
    {
        var preset = ExportPresets.Find(id)!;

        Assert.False(preset.HasQuality);
        Assert.Null(preset.QualityValue(ExportQuality.Best));
    }

    [Fact]
    public void EverythingIsAvailableOnAFullBuild()
    {
        var caps = WorkedExampleTests.MasterCapabilities();

        Assert.All(ExportPresets.All, p => Assert.Null(p.UnavailableReason(caps)));
        Assert.Equal("libx264", ExportPresets.H264.Resolve(caps)!.Video!.Encoder);
        Assert.Equal("aac", ExportPresets.H264.Resolve(caps)!.Audio!.Encoder);
        Assert.Null(ExportPresets.Gif.Resolve(caps)!.Audio);
        Assert.Null(ExportPresets.Wav.Resolve(caps)!.Video);
    }

    [Fact]
    public void PresetsGreyOutWithAReason()
    {
        var caps = WorkedExampleTests.MasterCapabilities(
            FilterScriptSyntax.LegacyScript, "libx265", "libsvtav1", "libaom-av1", "libx264", "libopenh264", "libopus", "libvorbis", "libmp3lame", "aac", "pcm_s16le", "flac", "gif", "prores_ks");

        Assert.Equal("libx265 is not in this ffmpeg build", ExportPresets.H265.UnavailableReason(caps));
        Assert.Equal("none of libx264, libopenh264 is in this ffmpeg build", ExportPresets.H264.UnavailableReason(caps));
        Assert.Equal("none of libsvtav1, libaom-av1 is in this ffmpeg build", ExportPresets.Av1.UnavailableReason(caps));
        Assert.Equal("libmp3lame is not in this ffmpeg build", ExportPresets.Mp3.UnavailableReason(caps));
        Assert.Equal("aac is not in this ffmpeg build", ExportPresets.Aac.UnavailableReason(caps));
        Assert.Equal("prores_ks is not in this ffmpeg build", ExportPresets.ProRes.UnavailableReason(caps));
        Assert.Contains("libopus", ExportPresets.Vp9.UnavailableReason(caps), StringComparison.Ordinal);
        Assert.Null(ExportPresets.H265.Resolve(caps));

        // "always": WAV and FLAC need no encoder check, GIF only its palette filters.
        Assert.True(ExportPresets.Wav.IsAvailable(caps));
        Assert.True(ExportPresets.Flac.IsAvailable(caps));
        Assert.True(ExportPresets.Gif.IsAvailable(caps));
    }

    [Fact]
    public void GifNeedsThePaletteFilters()
    {
        var full = WorkedExampleTests.MasterCapabilities();
        var withoutPalette = full with { Filters = full.Filters.Remove("palettegen") };

        Assert.Equal("palettegen is not in this ffmpeg build", ExportPresets.Gif.UnavailableReason(withoutPalette));
        Assert.True(ExportPresets.H264.IsAvailable(withoutPalette));
    }

    [Theory]
    [InlineData(1920, 1080, 720, 1280, 720)]
    [InlineData(1920, 1080, 2160, 3840, 2160)]
    [InlineData(1440, 1080, 720, 960, 720)]
    [InlineData(1080, 1920, 1080, 608, 1080)]
    [InlineData(1920, 1080, 721, 1280, 720)]
    public void CanvasForHeightKeepsTheAspectWithEvenSizes(int seqW, int seqH, int height, int expectedW, int expectedH)
    {
        var settings = new SequenceSettings(seqW, seqH, FrameRate.Fps30);

        Assert.Equal((expectedW, expectedH), ExportPresets.CanvasForHeight(settings, height));
    }

    [Theory]
    [InlineData(320, 320, 180)]
    [InlineData(480, 480, 270)]
    [InlineData(640, 640, 360)]
    [InlineData(800, 800, 450)]
    public void GifWidths(int width, int expectedW, int expectedH)
    {
        var settings = new SequenceSettings(1920, 1080, FrameRate.Fps30);

        Assert.Contains(width, ExportPresets.GifWidths);
        Assert.Equal((expectedW, expectedH), ExportPresets.CanvasForWidth(settings, width));
    }

    [Fact]
    public void TemplatesExpandPlaceholders()
    {
        var template = new EncoderTemplate("x", "-crf {q} -b:v {bitrate} -tag {qq}");

        Assert.Equal(new[] { "-crf", "20", "-b:v", "5000", "-tag", "{qq}" }, template.Expand(20, 5000).ToArray());
        Assert.Throws<InvalidOperationException>(() => template.Expand(null, 5000));
        Assert.Throws<InvalidOperationException>(() => template.Expand(20, null));
        Assert.Empty(new EncoderTemplate("gif", string.Empty).Expand(null, null));
    }
}
