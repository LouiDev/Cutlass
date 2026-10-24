using Cutlass.Compiler;
using Cutlass.Compiler.Emit;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// Every video <see cref="TransitionKind"/> renders on real ffmpeg: a V1 run of 12-frame clips alternating two media
/// with one 8-frame transition of each kind on every cut, rendered whole and in windows that must be byte-identical to
/// the full render (TECH_BRIEF 7.4, 7.5 and 11.2). A kind whose xfade name the build lacks fails ffmpeg here.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class TransitionKindsTests
{
    private const int W = 320;
    private const int H = 180;
    private const int ClipFrames = 12;
    private const int TransitionFrames = 8;
    private const int FrameBytes = W * H * 4;

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public TransitionKindsTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task EveryTransitionKindRendersWindowEqualToFull()
    {
        await using var kit = await ExportKit.CreateAsync();
        var cfr = await kit.AssetAsync("m_cfr", _media.Cfr25Av);
        var intra = await kit.AssetAsync("m_intra", _media.Oracle25Intra);
        MediaAsset[] media = [cfr, intra];
        var catalog = await kit.CatalogAsync(media);

        var kinds = Enum.GetValues<TransitionKind>().Where(k => k != TransitionKind.AudioCrossfade).ToArray();
        var clips = new List<Clip>();
        var transitions = new List<Transition>();
        for (var i = 0; i <= kinds.Length; i++)
        {
            // Every clip keeps 4 frames of source on both sides for the transition halves (cfr25_av is only 4 s long).
            var source = i % 2 == 0 ? Micros(1_000_000 + (i * 300_000)) : Micros(200_000 + ((i % 5) * 500_000));
            clips.Add(V($"c_{i}", i * ClipFrames, ClipFrames, i % 2 == 0 ? "m_intra" : "m_cfr", source));
            if (i > 0)
            {
                transitions.Add(new Transition($"x_{i}", kinds[i - 1], $"c_{i - 1}", $"c_{i}", TransitionFrames));
            }
        }

        var length = (kinds.Length + 1) * ClipFrames;
        var project = Project(W, H, FrameRate.Fps25, media, Track("tr_v1", TrackKind.Video, clips, transitions));

        var request = Preview(0, length, W, H);
        var graph = TimelineCompiler.Compile(project, catalog, request).GraphText;
        foreach (var kind in kinds)
        {
            Assert.Contains("xfade=transition=" + VideoEmitter.XfadeName(kind) + ":", graph, StringComparison.Ordinal);
        }

        var full = await kit.RenderBgraAsync(project, catalog, request);
        Assert.Equal((long)length * FrameBytes, full.LongLength);

        // Windows: across the first transition, inside the run, one frame in the middle of a transition, and the tail.
        (long Start, long End)[] windows = [(5, 25), (200, 213), (ClipFrames * 25, (ClipFrames * 25) + 1), (length - 7, length)];
        foreach (var (start, end) in windows)
        {
            var window = await kit.RenderBgraAsync(project, catalog, Preview(start, end, W, H));
            Assert.Equal((end - start) * FrameBytes, window.LongLength);
            Assert.True(
                window.AsSpan().SequenceEqual(full.AsSpan((int)(start * FrameBytes), (int)((end - start) * FrameBytes))),
                $"The window [{start}, {end}) differs from the full render.");
        }
    }
}
