using Cutlass.Compiler;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// Range independence (TECH_BRIEF 7.5 and 11.2): a one-frame window is byte-identical to the same frame of the full
/// render, including frames inside a crossfade, inside title fades and on a transformed picture-in-picture clip.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class WindowEqualityTests
{
    private const int W = 320;
    private const int H = 180;
    private const long Length = 130;
    private const int FrameBytes = W * H * 4;

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public WindowEqualityTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegTheory]
    [InlineData(30, 1)]
    [InlineData(30000, 1001)]
    public async Task OneFrameWindowsEqualTheFullRender(long rateNum, long rateDen)
    {
        await using var kit = await ExportKit.CreateAsync();
        var rate = new FrameRate(new Rational(rateNum, rateDen));
        var cfr = await kit.AssetAsync("m_cfr", _media.Cfr25Av);
        var lgop = await kit.AssetAsync("m_lgop", _media.Oracle25LongGop);
        var intra = await kit.AssetAsync("m_intra", _media.Oracle25Intra);
        MediaAsset[] media = [cfr, lgop, intra];
        var catalog = await kit.CatalogAsync(media);
        var pip = VideoProps.Default with
        {
            Transform = Transform.Identity with { X = 60, Y = -30, Scale = 0.5, CropLeft = 0.1 },
            Opacity = 0.8,
        };
        var project = Project(
            W,
            H,
            rate,
            media,
            Track(
                "tr_v1",
                TrackKind.Video,
                [V("c_a", 0, 60, "m_cfr", Micros(400_000)), V("c_b", 60, 70, "m_lgop", Micros(3_000_000), speed: 150)],
                [new Transition("x_1", TransitionKind.Crossfade, "c_a", "c_b", 20)]),
            Track(
                "tr_v2",
                TrackKind.Video,
                [
                    Title("c_t", 30, 50, TitleSpec.Default with { Text = "Window", Size = 40, Box = true }, 10, 10),
                    V("c_p", 80, 40, "m_intra", Micros(1_234_567), video: pip),
                ]));

        var full = await kit.RenderBgraAsync(project, catalog, Preview(0, Length, W, H));

        long[] probes = [0, 35, 45, 52, 59, 60, 61, 65, 69, 70, 79, 80, 85, 100, 119, 129];
        foreach (var frame in probes)
        {
            var single = await kit.RenderBgraAsync(project, catalog, Preview(frame, frame + 1, W, H, RenderPurpose.PreviewFrame));
            Assert.True(
                single.AsSpan().SequenceEqual(full.AsSpan((int)(frame * FrameBytes), FrameBytes)),
                $"Frame {frame} of a one-frame window differs from the full render.");
        }
    }

    [FfmpegFact]
    public async Task ShortWindowsAcrossATransitionEqualTheFullRender()
    {
        await using var kit = await ExportKit.CreateAsync();
        var lgop = await kit.AssetAsync("m_lgop", _media.Oracle25LongGop);
        var cfr = await kit.AssetAsync("m_cfr", _media.Cfr25Av);
        MediaAsset[] media = [lgop, cfr];
        var catalog = await kit.CatalogAsync(media);
        var project = Project(
            W,
            H,
            FrameRate.Fps30,
            media,
            Track(
                "tr_v1",
                TrackKind.Video,
                [V("c_a", 0, 50, "m_lgop", Micros(5_000_000)), V("c_b", 50, 40, "m_cfr", Micros(500_000))],
                [new Transition("x_1", TransitionKind.WipeLeft, "c_a", "c_b", 16)]));

        var full = await kit.RenderBgraAsync(project, catalog, Preview(0, 90, W, H));

        (long Start, long End)[] windows = [(40, 46), (44, 60), (55, 70), (30, 42)];
        foreach (var (start, end) in windows)
        {
            var part = await kit.RenderBgraAsync(project, catalog, Preview(start, end, W, H));
            Assert.True(
                part.AsSpan().SequenceEqual(full.AsSpan((int)(start * FrameBytes), (int)((end - start) * FrameBytes))),
                $"Window [{start}, {end}) differs from the full render.");
        }
    }

    [FfmpegTheory]
    [InlineData("xfade then cut")]
    [InlineData("cut then xfade")]
    public async Task OneFrameWindowsInRunsMixingCutsAndTransitionsEqualTheFullRender(string shape)
    {
        // A concat after an xfade (or before one) joins yuva444p and yuva420p segments. Whatever format ffmpeg
        // negotiates there, a one-frame window of any clip of the run must take the same pixel path (TECH_BRIEF 7.5).
        await using var kit = await ExportKit.CreateAsync();
        var cfr = await kit.AssetAsync("m_cfr", _media.Cfr25Av);
        var lgop = await kit.AssetAsync("m_lgop", _media.Oracle25LongGop);
        var intra = await kit.AssetAsync("m_intra", _media.Oracle25Intra);
        MediaAsset[] media = [cfr, lgop, intra];
        var catalog = await kit.CatalogAsync(media);
        var pip = VideoProps.Default with
        {
            Transform = Transform.Identity with { X = 40, Y = 20, Scale = 0.6 },
            Opacity = 0.7,
        };
        var xfadeFirst = shape == "xfade then cut";
        var project = Project(
            W,
            H,
            FrameRate.Fps30,
            media,
            Track(
                "tr_v1",
                TrackKind.Video,
                [
                    V("c_1", 0, 30, xfadeFirst ? "m_cfr" : "m_intra", Micros(400_000), video: xfadeFirst ? null : pip),
                    V("c_2", 30, 30, xfadeFirst ? "m_lgop" : "m_cfr", Micros(1_000_000)),
                    V("c_3", 60, 30, xfadeFirst ? "m_intra" : "m_lgop", Micros(2_000_000), video: xfadeFirst ? pip : null),
                ],
                [
                    xfadeFirst
                        ? new Transition("x_1", TransitionKind.Crossfade, "c_1", "c_2", 10)
                        : new Transition("x_1", TransitionKind.WipeLeft, "c_2", "c_3", 10),
                ]));

        var full = await kit.RenderBgraAsync(project, catalog, Preview(0, 90, W, H));

        long[] probes = [0, 12, 24, 25, 29, 30, 34, 35, 47, 54, 55, 59, 60, 64, 65, 77, 89];
        foreach (var frame in probes)
        {
            var single = await kit.RenderBgraAsync(project, catalog, Preview(frame, frame + 1, W, H, RenderPurpose.PreviewFrame));
            Assert.True(
                single.AsSpan().SequenceEqual(full.AsSpan((int)(frame * FrameBytes), FrameBytes)),
                $"{shape}: frame {frame} of a one-frame window differs from the full render.");
        }
    }
}
