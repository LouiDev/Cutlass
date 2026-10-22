using Cutlass.Compiler;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// Every filter pattern the compiler emits runs on real ffmpeg with exact frame and sample counts: letterbox pad,
/// autorotated sources, canvas overlays with crop and opacity, every transition kind, speed changes with atempo,
/// volume envelopes, fades in offset windows, stills, per-track audio and frame stepping (TECH_BRIEF 7 and 11.2).
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class FeatureCoverageTests
{
    private const int W = 320;
    private const int H = 180;

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public FeatureCoverageTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task EveryVideoPatternRendersWithExactFrameCounts()
    {
        await using var kit = await ExportKit.CreateAsync();
        var (project, catalog) = await RichTimelineAsync(kit);

        // Full render, a window inside the transitions run, and a window starting inside faded clips.
        var full = await kit.RenderBgraAsync(project, catalog, Preview(0, 300, W, H));
        var window = await kit.RenderBgraAsync(project, catalog, Preview(95, 135, W, H));
        var stepped = await kit.RenderBgraAsync(project, catalog, new RenderRequest(0, 300, W, H, RenderPurpose.PreviewVideo, true, false, false, AudioLayout.Mixed, 4, OutputTail.Preview, false));

        Assert.Equal(300L * W * H * 4, full.LongLength);
        Assert.Equal(75L * W * H * 4, stepped.LongLength);
        const int frameBytes = W * H * 4;
        Assert.True(window.AsSpan().SequenceEqual(full.AsSpan(95 * frameBytes, 40 * frameBytes)), "The window differs from the full render.");
        for (var k = 0; k < 75; k++)
        {
            Assert.True(stepped.AsSpan(k * frameBytes, frameBytes).SequenceEqual(full.AsSpan(k * 4 * frameBytes, frameBytes)), $"Stepped frame {k} is not timeline frame {k * 4}.");
        }

        // The red still is pillarboxed into the 16:9 canvas: red in the middle, black at the left edge.
        var still = full.AsSpan(20 * frameBytes, frameBytes);
        Assert.True(still[((90 * W) + 160) * 4 + 2] > 200 && still[((90 * W) + 160) * 4 + 1] < 60, "The still is not red in the middle.");
        Assert.True(still[((90 * W) + 10) * 4 + 2] < 30, "The pillarbox is not black.");
    }

    [FfmpegFact]
    public async Task EveryAudioPatternMixesToTheExactSampleCount()
    {
        await using var kit = await ExportKit.CreateAsync();
        var (project, catalog) = await RichTimelineAsync(kit);

        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.Wav, "rich_mix", 13, 287, limitPeaks: true));
        var perTrack = await kit.RenderPcmAsync(
            project,
            catalog,
            new RenderRequest(37, 263, W, H, RenderPurpose.PreviewAudio, false, true, false, AudioLayout.PerTrack, 1, OutputTail.Preview, false));

        ExportKit.AssertCompleted(job);
        var mixed = await ExportKit.DecodeAudioAsync(job.OutputPath);
        Assert.Equal(TimeMath.SamplesAt(287, FrameRate.Fps2997) - TimeMath.SamplesAt(13, FrameRate.Fps2997), mixed.Length / 2);

        // Three audio tracks (one empty) give six channels of exactly S samples.
        var samples = TimeMath.SamplesAt(263, FrameRate.Fps2997) - TimeMath.SamplesAt(37, FrameRate.Fps2997);
        Assert.Equal(samples * 6 * 2, perTrack.LongLength);
    }

    [FfmpegFact]
    public async Task StillAndGifExportsWork()
    {
        await using var kit = await ExportKit.CreateAsync();
        var red = await kit.AssetAsync("m_red", _media.RedPng);
        var project = Project(W, H, FrameRate.Fps30, [red], Track("tr_v1", TrackKind.Video, [V("c_r", 0, 45, "m_red", 0)]));
        var catalog = await kit.CatalogAsync([red]);

        var h264 = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.H264, "still", 0, 45));
        var (gw, gh) = ExportPresets.CanvasForWidth(project.Sequence.Settings, 480);
        var gif = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.Gif, "still", 0, 45, gw, gh));

        ExportKit.AssertCompleted(h264);
        ExportKit.AssertCompleted(gif);
        Assert.Equal(45, await ExportKit.CountFramesAsync(h264.OutputPath));
        Assert.Contains("-loop", h264.DisplayCommand!, StringComparison.Ordinal);
        var info = await ExportKit.StreamInfoAsync(gif.OutputPath, "v");
        Assert.Equal("480", info["width"]);
        Assert.Equal("270", info["height"]);
    }

    // 30000/1001, 320x180, 300 frames:
    // V1: rotated clip (pillarboxed, pad) | cut | 4:3 still (pad) | cut | testsrc; then a run with every transition kind.
    // V2: transformed, cropped, 70% opaque clips in one run (canvas overlays) with a wipe; a title with a box.
    // A1: speeds 25/50/200/300 with crossfades. A2: envelope plus fades. A3: empty.
    private async Task<(Project Project, TestRenderCatalog Catalog)> RichTimelineAsync(ExportKit kit)
    {
        var rotated = await kit.AssetAsync("m_rot", _media.Rotated90);
        var red = await kit.AssetAsync("m_red", _media.RedPng);
        var cfr = await kit.AssetAsync("m_cfr", _media.Cfr25Av);
        var intra = await kit.AssetAsync("m_intra", _media.Oracle25Intra);
        var tone = await kit.AssetAsync("m_tone", _media.Tone);
        var impulse = await kit.AssetAsync("m_imp", _media.Impulse);
        MediaAsset[] media = [rotated, red, cfr, intra, tone, impulse];

        TransitionKind[] kinds =
        [
            TransitionKind.Crossfade, TransitionKind.DipToBlack, TransitionKind.DipToWhite, TransitionKind.WipeLeft,
            TransitionKind.WipeRight, TransitionKind.SlideLeft, TransitionKind.SlideRight, TransitionKind.CircleOpen,
        ];
        var v1Clips = new List<Clip>
        {
            V("c_rot", 0, 15, "m_rot", Micros(200_000)),
            V("c_red", 15, 15, "m_red", 0),
            V("c_src", 30, 20, "m_cfr", Micros(100_000), video: VideoProps.Default with { FadeInFrames = 5 }),
        };
        var v1Transitions = new List<Transition>();
        for (var i = 0; i <= kinds.Length; i++)
        {
            v1Clips.Add(V($"c_k{i}", 60 + (i * 26), 26, i is 1 or 3 ? "m_cfr" : "m_intra", Micros(1_000_000 + (i * 300_000)), speed: i == 3 ? 150 : 100));
            if (i > 0)
            {
                v1Transitions.Add(new Transition($"x_k{i}", kinds[i - 1], $"c_k{i - 1}", $"c_k{i}", 8));
            }
        }

        var pip = VideoProps.Default with
        {
            Transform = new Transform(-60, 30, 0.6, 0.1, 0.05, 0.15, 0.1),
            Opacity = 0.7,
            FadeOutFrames = 6,
        };
        var title = TitleSpec.Default with { Text = "Coverage: 100%, it's \"fine\"", Size = 20, Box = true, Anchor = TitleAnchor.Top };
        var keys = new AudioProps(-2, 10, 10, [new VolumeKey(5, -18), new VolumeKey(40, 0), new VolumeKey(80, -9)]);

        var project = Project(
            W,
            H,
            FrameRate.Fps2997,
            media,
            Track("tr_v1", TrackKind.Video, v1Clips, v1Transitions),
            Track(
                "tr_v2",
                TrackKind.Video,
                [
                    V("c_p1", 100, 40, "m_intra", Micros(5_000_000), video: pip),
                    V("c_p2", 140, 40, "m_cfr", Micros(500_000), speed: 50, video: pip),
                    Title("c_t", 200, 60, title, 10, 10),
                ],
                [new Transition("x_p", TransitionKind.WipeLeft, "c_p1", "c_p2", 12)]),
            Track(
                "tr_a1",
                TrackKind.Audio,
                [
                    A("c_s1", 0, 40, "m_tone", Micros(1_000_000), speed: 25),
                    A("c_s2", 40, 40, "m_tone", Micros(3_000_000), speed: 50),
                    A("c_s3", 80, 40, "m_imp", Micros(2_000_000), speed: 200),
                    A("c_s4", 120, 40, "m_imp", Micros(9_000_000), speed: 300),
                ],
                [new Transition("x_a1", TransitionKind.AudioCrossfade, "c_s1", "c_s2", 10), new Transition("x_a2", TransitionKind.AudioCrossfade, "c_s3", "c_s4", 10)]),
            Track("tr_a2", TrackKind.Audio, [A("c_e", 150, 100, "m_cfr", 0, audio: keys)]),
            Track("tr_a3", TrackKind.Audio, []));
        return (project, await kit.CatalogAsync(media));
    }
}
