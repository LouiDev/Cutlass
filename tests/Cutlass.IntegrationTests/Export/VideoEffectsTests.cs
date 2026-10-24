using System.Text;
using Cutlass.Compiler;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// The static video effects of effects batch 2 on real ffmpeg (TECH_BRIEF 7.3 and 11.2): the flips and a 180 degree
/// rotation move the oracle blocks exactly where the bit layout says, the chroma key lets the layer below show
/// through, a colour temperature change keeps the BT.709 tags and the frame count, and blur plus vignette still decode.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class VideoEffectsTests
{
    private const int W = TestMediaFixture.OracleWidth;
    private const int H = TestMediaFixture.OracleHeight;
    private const int FrameBytes = W * H * 4;
    private const long Frames = 20;

    private static readonly FrameRate Fps25 = new(new Rational(25, 1));

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public VideoEffectsTests(TestMediaFixture media)
    {
        _media = media;
    }

    /// <summary>The oracle value after a horizontal flip: column <c>c</c> becomes <c>7 - c</c> in both rows.</summary>
    /// <param name="value">The 16-bit oracle value.</param>
    /// <returns>The flipped value.</returns>
    public static int FlipHorizontal(int value)
    {
        var result = 0;
        for (var row = 0; row < 2; row++)
        {
            for (var col = 0; col < 8; col++)
            {
                if ((value & (1 << (col + (8 * row)))) != 0)
                {
                    result |= 1 << ((7 - col) + (8 * row));
                }
            }
        }

        return result;
    }

    /// <summary>The oracle value after a vertical flip: bits 0 to 7 swap with bits 8 to 15.</summary>
    /// <param name="value">The 16-bit oracle value.</param>
    /// <returns>The flipped value.</returns>
    public static int FlipVertical(int value) => ((value & 0xFF) << 8) | ((value >> 8) & 0xFF);

    [FfmpegTheory]
    [InlineData("flipH")]
    [InlineData("flipV")]
    [InlineData("rotate180")]
    [InlineData("both")]
    public async Task FlipsAndAHalfTurnMoveTheOracleBlocksExactly(string effect)
    {
        var effects = effect switch
        {
            "flipH" => VideoEffects.None with { FlipH = true },
            "flipV" => VideoEffects.None with { FlipV = true },
            "rotate180" => VideoEffects.None with { RotationDegrees = 180 },
            _ => VideoEffects.None with { FlipH = true, FlipV = true },
        };
        Func<int, int> expected = effect switch
        {
            "flipH" => FlipHorizontal,
            "flipV" => FlipVertical,
            _ => v => FlipVertical(FlipHorizontal(v)),
        };

        var frames = await RenderOracleAsync(effects);

        AssertOracle(frames, f => expected(TestMediaFixture.ExpectedOracleValue(f)), effect);
    }

    [FfmpegFact]
    public async Task BlurAndVignetteStillDecodeTheOracle()
    {
        var frames = await RenderOracleAsync(VideoEffects.None with { Blur = 2, Vignette = 0.3 });

        AssertOracle(frames, TestMediaFixture.ExpectedOracleValue, "blur and vignette");
    }

    [FfmpegFact]
    public async Task ChromaKeyLetsTheTrackBelowShowThrough()
    {
        await using var kit = await ExportKit.CreateAsync();
        var red = await kit.AssetAsync("m_red", _media.RedPng);
        var green = await kit.AssetAsync("m_green", _media.GreenPng);
        MediaAsset[] media = [red, green];
        var catalog = await kit.CatalogAsync(media);
        var keyed = VideoProps.Default with { Effects = VideoEffects.None with { KeyEnabled = true, KeyColor = "#00FF00", KeySimilarity = 0.2 } };
        var project = Project(
            W,
            H,
            FrameRate.Fps30,
            media,
            Track("tr_v1", TrackKind.Video, [V("c_red", 0, 10, "m_red", 0)]),
            Track("tr_v2", TrackKind.Video, [V("c_green", 0, 10, "m_green", 0, video: keyed)]));

        var frame = await kit.RenderBgraAsync(project, catalog, Preview(3, 4, W, H, RenderPurpose.PreviewFrame));
        var unkeyed = await kit.RenderBgraAsync(
            project with { Sequence = project.Sequence with { Tracks = [project.Sequence.Tracks[0], Track("tr_v2", TrackKind.Video, [V("c_green", 0, 10, "m_green", 0)])] } },
            catalog,
            Preview(3, 4, W, H, RenderPurpose.PreviewFrame));

        Assert.Equal(FrameBytes, frame.Length);
        var centre = ((90 * W) + 160) * 4;
        Assert.True(frame[centre + 2] > 200 && frame[centre + 1] < 60 && frame[centre] < 60, $"The keyed centre pixel is not red: B={frame[centre]} G={frame[centre + 1]} R={frame[centre + 2]}.");
        Assert.True(unkeyed[centre + 1] > 100 && unkeyed[centre + 2] < 60, "Without the key the green layer should cover the red still.");
    }

    [FfmpegFact]
    public async Task ColourTemperatureKeepsTheBt709TagsAndTheFrameCount()
    {
        await using var kit = await ExportKit.CreateAsync();
        var cfr = await kit.AssetAsync("m_cfr", _media.Cfr25Av);
        var catalog = await kit.CatalogAsync([cfr]);
        var warm = VideoProps.Default with { Effects = VideoEffects.None with { TemperatureKelvin = 4000, Tint = 0.1 } };
        var project = Project(W, H, FrameRate.Fps30, [cfr], Track("tr_v1", TrackKind.Video, [V("c_w", 0, 30, "m_cfr", Micros(500_000), video: warm)]));

        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.H264, "warm", 0, 30));

        ExportKit.AssertCompleted(job);
        Assert.Equal(30, await ExportKit.CountFramesAsync(job.OutputPath));
        var video = await ExportKit.StreamInfoAsync(job.OutputPath, "v");
        Assert.Equal("bt709", video["color_space"]);
        Assert.Equal("bt709", video["color_transfer"]);
        Assert.Equal("bt709", video["color_primaries"]);
    }

    // The 25 fps oracle on a 25 fps timeline from source 0: timeline frame f shows oracle frame f.
    private async Task<byte[]> RenderOracleAsync(VideoEffects effects)
    {
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_o", _media.Oracle25Intra);
        var catalog = await kit.CatalogAsync([asset]);
        var clip = V("c_o", 0, Frames, "m_o", 0, video: VideoProps.Default with { Effects = effects });
        var project = Project(W, H, Fps25, [asset], Track("tr_v1", TrackKind.Video, [clip]));
        return await kit.RenderBgraAsync(project, catalog, Preview(0, Frames, W, H));
    }

    private static void AssertOracle(byte[] frames, Func<long, int> expected, string what)
    {
        Assert.Equal(Frames * FrameBytes, frames.LongLength);
        var failures = new StringBuilder();
        for (long f = 0; f < Frames; f++)
        {
            var actual = OracleReader.DecodeBgra(frames.AsSpan((int)(f * FrameBytes), FrameBytes), W, H);
            if (actual != expected(f))
            {
                failures.Append(System.Globalization.CultureInfo.InvariantCulture, $"frame {f}: expected {expected(f)}, got {actual}; ");
            }
        }

        Assert.True(failures.Length == 0, $"{what}: {failures}");
    }
}
