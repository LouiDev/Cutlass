using System.Collections.Immutable;
using Cutlass.Compiler;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using static Cutlass.Tests.Compiler.CompilerKit;

namespace Cutlass.Tests.Compiler;

/// <summary>The compiler golden cases (TECH_BRIEF 11.1): fixed timelines and requests with known output.</summary>
internal static class GoldenCases
{
    /// <summary>One golden case.</summary>
    public sealed record Case(string Name, Project Project, RenderRequest Request, TestCatalog Catalog);

    private static readonly SequenceSettings Hd30 = new(1920, 1080, FrameRate.Fps30);

    /// <summary>Every case by name.</summary>
    public static ImmutableArray<Case> All { get; } =
    [
        SingleClip(),
        GapFirst(),
        TwoClipsWithGap(),
        Cuts(),
        TransitionKinds(),
        WorkedExample(),
        WindowMidCrossfade(),
        WindowOutsideTransition(),
        PipNegativePosition(),
        CropOpacityInRun(),
        LetterboxedFit(),
        Speeds(),
        Envelope(),
        FadesWithWindowOffset(),
        Still(),
        ColorAdjust(),
        MuteSolo(),
        PerTrackAudio(),
        EmptyAudio(),
        AudioOnly(),
        Gif(),
        FrameStep(),
        TitleBoxScaled(),
        Proxy(),
        IndexSeek(),
        Offline(),
        HiddenAndDisabled(),
    ];

    /// <summary>Finds a case.</summary>
    public static Case Get(string name) => All.Single(c => c.Name == name);

    private static MediaAsset[] Hd() =>
    [
        VideoAsset("m_a", "D:/Clips/a.mp4"),
        VideoAsset("m_b", "D:/Clips/b.mp4"),
        VideoAsset("m_c", "D:/Clips/c.mp4"),
    ];

    private static Case Make(string name, Project project, RenderRequest request, Func<Project, TestCatalog>? catalog = null) =>
        new(name, project, request, catalog?.Invoke(project) ?? TestCatalog.For(project));

    private static Case SingleClip()
    {
        var p = Project(Hd30, Hd(), VideoTrack("tr_v1", [V("c_v", 0, 90, "m_a", 2 * F, link: "l_1")]), AudioTrack("tr_a1", [A("c_s", 0, 90, "m_a", 2 * F, link: "l_1")]));
        return Make("single_clip", p, Export(0, 90));
    }

    private static Case GapFirst()
    {
        var p = Project(Hd30, Hd(), VideoTrack("tr_v1", [V("c_v", 30, 60, "m_a", 0)]), AudioTrack("tr_a1", [A("c_s", 30, 60, "m_a", 0)]));
        return Make("gap_first", p, Export(0, 120));
    }

    private static Case TwoClipsWithGap()
    {
        var p = Project(
            Hd30,
            Hd(),
            VideoTrack("tr_v1", [V("c_1", 0, 60, "m_a", F), V("c_2", 90, 60, "m_b", 5 * F)]),
            AudioTrack("tr_a1", [A("c_3", 0, 60, "m_a", F), A("c_4", 90, 60, "m_b", 5 * F)]));
        return Make("two_clips_with_gap", p, Export(0, 150));
    }

    private static Case Cuts()
    {
        var p = Project(Hd30, Hd(), VideoTrack("tr_v1", [V("c_1", 0, 40, "m_a", 0), V("c_2", 40, 40, "m_b", 3 * F), V("c_3", 80, 40, "m_c", F)]));
        return Make("cuts", p, Export(0, 120, audio: false));
    }

    private static Case TransitionKinds()
    {
        TransitionKind?[] joins =
        [
            TransitionKind.Crossfade, TransitionKind.DipToBlack, TransitionKind.DipToWhite, TransitionKind.WipeLeft, null,
            TransitionKind.WipeRight, TransitionKind.SlideLeft, TransitionKind.SlideRight, TransitionKind.CircleOpen,
        ];
        var clips = Enumerable.Range(0, joins.Length + 1).Select(i => V($"c_{i}", i * 30, 30, i % 2 == 0 ? "m_a" : "m_b", (i + 1) * F)).ToList();
        var transitions = joins
            .Select((k, i) => k is { } kind ? new Transition($"x_{i}", kind, $"c_{i}", $"c_{i + 1}", 10) : null)
            .OfType<Transition>()
            .ToList();
        var p = Project(Hd30, Hd(), VideoTrack("tr_v1", clips, transitions));
        return Make("transition_kinds", p, Export(0, 300, audio: false));
    }

    private static Case WorkedExample()
    {
        var p = CompilerKit.WorkedExample();
        return Make("worked_example_7_8", p, Export(0, 270));
    }

    private static Case WindowMidCrossfade()
    {
        var p = CompilerKit.WorkedExample();
        return Make("window_mid_crossfade", p, PreviewFrame(150, 151, 960, 540));
    }

    // A window inside a run with a transition that needs no xfade: the canvas step still applies and the frames are
    // converted to yuva444p as the full render's xfade does.
    private static Case WindowOutsideTransition()
    {
        var pip = VideoProps.Default with { Transform = Transform.Identity with { Scale = 0.5, X = 100 } };
        var p = Project(
            Hd30,
            Hd(),
            VideoTrack(
                "tr_v1",
                [V("c_1", 0, 60, "m_a", F, video: pip), V("c_2", 60, 60, "m_b", F), V("c_3", 120, 60, "m_c", F)],
                [new Transition("x_1", TransitionKind.Crossfade, "c_2", "c_3", 20)]));
        return Make("window_outside_transition", p, PreviewFrame(30, 31, 960, 540));
    }

    private static Case PipNegativePosition()
    {
        var pip = VideoProps.Default with { Transform = Transform.Identity with { X = -1200, Y = 600, Scale = 0.5 } };
        var p = Project(Hd30, Hd(), VideoTrack("tr_v1", [V("c_1", 0, 60, "m_a", 0)]), VideoTrack("tr_v2", [V("c_2", 10, 40, "m_b", 2 * F, video: pip)]));
        return Make("pip_negative_position", p, Export(0, 60, audio: false));
    }

    private static Case CropOpacityInRun()
    {
        var cropped = VideoProps.Default with
        {
            Transform = new Transform(200, -100, 0.6, 0.1, 0.05, 0.2, 0),
            Opacity = 0.5,
        };
        var p = Project(
            Hd30,
            Hd(),
            VideoTrack("tr_v1", [V("c_1", 0, 60, "m_a", 0)]),
            VideoTrack("tr_v2", [V("c_2", 0, 30, "m_b", 0), V("c_3", 30, 30, "m_c", F, video: cropped)]));
        return Make("crop_opacity_in_run", p, Export(0, 60, audio: false));
    }

    private static Case LetterboxedFit()
    {
        MediaAsset[] media = [VideoAsset("m_43", "D:/Clips/four_three.mp4", 1440, 1080), VideoAsset("m_a", "D:/Clips/a.mp4")];
        var p = Project(
            Hd30,
            media,
            VideoTrack("tr_v1", [V("c_1", 0, 30, "m_43", 0), V("c_2", 30, 30, "m_a", 0)]),
            VideoTrack("tr_v2", [V("c_3", 70, 20, "m_43", F)]));
        return Make("letterboxed_fit", p, Export(0, 90, audio: false));
    }

    private static Case Speeds()
    {
        MediaAsset[] media = [VideoAsset("m_25", "D:/Clips/pal.mp4", rate: new Rational(25, 1), timeBase: new Rational(1, 12800))];
        var offGrid = (F / 25 * 50) + (F / 1000);
        var p = Project(
            Hd30,
            media,
            VideoTrack("tr_v1", [V("c_1", 0, 30, "m_25", offGrid, speed: 25), V("c_2", 30, 30, "m_25", offGrid, speed: 50), V("c_3", 60, 30, "m_25", offGrid, speed: 200)]),
            AudioTrack("tr_a1", [A("c_4", 0, 30, "m_25", offGrid, speed: 25), A("c_5", 30, 30, "m_25", offGrid, speed: 50), A("c_6", 60, 30, "m_25", offGrid, speed: 200)]));
        return Make("speeds", p, Export(0, 90));
    }

    private static Case Envelope()
    {
        var audio = new AudioProps(3, 0, 0, [new VolumeKey(10, -6), new VolumeKey(40, 0), new VolumeKey(60, -12)]);
        MediaAsset[] media = [AudioAsset("m_music", "D:/Audio/music.wav")];
        var p = Project(Hd30, media, AudioTrack("tr_a1", [A("c_1", 15, 90, "m_music", 2 * F, audio: audio)]));
        return Make("envelope", p, Export(0, 120, video: false));
    }

    private static Case FadesWithWindowOffset()
    {
        var video = VideoProps.Default with { FadeInFrames = 15, FadeOutFrames = 15 };
        var audio = AudioProps.Default with { FadeInFrames = 15, FadeOutFrames = 15, GainDb = -6 };
        var p = Project(
            Hd30,
            Hd(),
            VideoTrack("tr_v1", [V("c_1", 0, 90, "m_a", F, video: video)]),
            AudioTrack("tr_a1", [A("c_2", 0, 90, "m_a", F, audio: audio)]));
        return Make("fades_window_offset", p, Export(20, 80));
    }

    private static Case Still()
    {
        MediaAsset[] media = [StillAsset("m_png", "D:/Images/logo.png", 1280, 720)];
        var p = Project(Hd30, media, VideoTrack("tr_v1", [V("c_1", 0, 75, "m_png", 0)]));
        return Make("still", p, Export(0, 75, audio: false));
    }

    private static Case ColorAdjust()
    {
        var video = VideoProps.Default with { Color = new Cutlass.Core.Model.ColorAdjust(0.1, 1.2, 0.8, 1.1) };
        var p = Project(Hd30, Hd(), VideoTrack("tr_v1", [V("c_1", 0, 30, "m_a", 0, video: video)]));
        return Make("color_adjust", p, Export(0, 30, audio: false));
    }

    private static Case MuteSolo()
    {
        var p = Project(
            Hd30,
            Hd(),
            AudioTrack("tr_a1", [A("c_1", 0, 30, "m_a", 0)], muted: true),
            AudioTrack("tr_a2", [A("c_2", 0, 30, "m_b", 0)], solo: true, gainDb: -3),
            AudioTrack("tr_a3", [A("c_3", 0, 30, "m_c", 0)]),
            AudioTrack("tr_a4", [A("c_4", 0, 30, "m_c", F)], solo: true));
        return Make("mute_solo", p, Export(0, 30, video: false));
    }

    private static Case PerTrackAudio()
    {
        var p = Project(
            Hd30,
            Hd(),
            AudioTrack("tr_a1", [A("c_1", 0, 30, "m_a", 0)], muted: true, gainDb: -10),
            AudioTrack("tr_a2", []),
            AudioTrack("tr_a3", [A("c_2", 10, 30, "m_b", 0)]));
        var request = new RenderRequest(0, 30, 960, 540, RenderPurpose.PreviewAudio, false, true, false, AudioLayout.PerTrack, 1, OutputTail.Preview, false);
        return Make("per_track_audio", p, request);
    }

    private static Case EmptyAudio()
    {
        var p = Project(Hd30, Hd(), VideoTrack("tr_v1", [V("c_1", 0, 30, "m_a", 0)]), AudioTrack("tr_a1", []));
        return Make("empty_audio", p, Export(0, 30));
    }

    private static Case AudioOnly()
    {
        var p = Project(
            Hd30,
            Hd(),
            VideoTrack("tr_v1", [V("c_1", 0, 60, "m_a", 0)]),
            AudioTrack("tr_a1", [A("c_2", 0, 30, "m_a", 0), A("c_3", 30, 30, "m_b", F)], [new Transition("x_1", TransitionKind.AudioCrossfade, "c_2", "c_3", 10)]));
        var request = new RenderRequest(0, 60, 1920, 1080, RenderPurpose.Export, false, true, false, AudioLayout.Mixed, 1, OutputTail.ForExport("yuv420p"), true);
        return Make("audio_only", p, request);
    }

    private static Case Gif()
    {
        var p = Project(Hd30, Hd(), VideoTrack("tr_v1", [V("c_1", 0, 30, "m_a", 0)]));
        var (w, h) = ExportPresets.CanvasForWidth(Hd30, 480);
        var request = new RenderRequest(0, 30, w, h, RenderPurpose.Export, true, false, false, AudioLayout.Mixed, 1, OutputTail.Gif, false);
        return Make("gif", p, request);
    }

    private static Case FrameStep()
    {
        var p = Project(Hd30, Hd(), VideoTrack("tr_v1", [V("c_1", 0, 90, "m_a", 0)]));
        var request = new RenderRequest(0, 90, 480, 270, RenderPurpose.PreviewVideo, true, false, false, AudioLayout.Mixed, 2, OutputTail.Preview, false);
        return Make("framestep", p, request);
    }

    private static Case TitleBoxScaled()
    {
        var center = TitleSpec.Default with { Text = "Centre\r\nTwo lines\n", Anchor = TitleAnchor.Center, Box = true, OffsetX = 21, OffsetY = -40, Color = "#ffcc00ff" };
        var top = TitleSpec.Default with { Text = "Top", Font = "builtin:Inter-Regular", Anchor = TitleAnchor.Top, OutlineWidth = 0, Color = "#FFFFFF80" };
        var p = Project(
            Hd30,
            Hd(),
            VideoTrack("tr_v1", [Title("c_1", 0, 30, center, opacity: 0.75, fadeIn: 5)]),
            VideoTrack("tr_v2", [Title("c_2", 10, 20, top)]));
        return Make("title_box_scaled", p, PreviewFrame(0, 30, 960, 540));
    }

    private static Case Proxy()
    {
        MediaAsset[] media = [VideoAsset("m_uhd", "D:/Clips/uhd.mov", 3840, 2160, timeBase: new Rational(1, 30000), formatStartMicros: 0)];
        var p = Project(Hd30, media, VideoTrack("tr_v1", [V("c_1", 0, 30, "m_uhd", 2 * F)]));
        return Make("proxy", p, new RenderRequest(0, 30, 960, 540, RenderPurpose.PreviewVideo, true, false, true, AudioLayout.Mixed, 1, OutputTail.Preview, false), project =>
        {
            var asset = project.Media[0];
            var original = new FrameIndex(new Rational(1, 30000), Enumerable.Range(0, 300).Select(i => (long)i * 1000));
            var proxy = new FrameIndex(new Rational(1, 30000), Enumerable.Range(0, 300).Select(i => ((long)i * 1000) + 7));
            return new TestCatalog().Add(new MediaRenderInfo(
                asset,
                new RenderSource(asset.Path, 0, original),
                new RenderSource("C:/Cache/v1/media/ab/abcd/proxy540.mov", 0, proxy)));
        });
    }

    // An index with a start offset (format.start_time 1.4 s) and an off-grid source in point: setpts with an exact d0.
    private static Case IndexSeek()
    {
        MediaAsset[] media = [VideoAsset("m_ts", "D:/Clips/broadcast.ts", rate: new Rational(25, 1), timeBase: new Rational(1, 90000), formatStartMicros: 1_400_000, startPts: 126_000)];
        var p = Project(
            Hd30,
            media,
            VideoTrack("tr_v1", [V("c_1", 5, 40, "m_ts", (F / 3) + 12345)]),
            AudioTrack("tr_a1", [A("c_2", 5, 40, "m_ts", (F / 3) + 12345)]));
        return Make("index_seek", p, Export(0, 50), project =>
        {
            var asset = project.Media[0];
            var index = new FrameIndex(new Rational(1, 90000), Enumerable.Range(0, 500).Select(i => 126_000 + ((long)i * 3600)));
            return new TestCatalog().Add(new MediaRenderInfo(asset, new RenderSource(asset.Path, 1_400_000, index), null));
        });
    }

    private static Case Offline()
    {
        var p = Project(
            Hd30,
            Hd(),
            VideoTrack("tr_v1", [V("c_1", 0, 30, "m_a", 0), V("c_2", 30, 30, "m_b", F)], [new Transition("x_1", TransitionKind.Crossfade, "c_1", "c_2", 10)]),
            AudioTrack("tr_a1", [A("c_3", 0, 30, "m_b", 0)]));
        return Make("offline", p, Export(0, 60), project => TestCatalog.For(project).Remove("m_b"));
    }

    private static Case HiddenAndDisabled()
    {
        var p = Project(
            Hd30,
            Hd(),
            VideoTrack(
                "tr_v1",
                [V("c_1", 0, 30, "m_a", 0), V("c_2", 30, 30, "m_b", F, enabled: false), V("c_3", 60, 30, "m_c", 0)],
                [new Transition("x_1", TransitionKind.Crossfade, "c_1", "c_2", 10)]),
            VideoTrack("tr_v2", [V("c_4", 0, 90, "m_c", 0)], hidden: true));
        return Make("hidden_and_disabled", p, Export(0, 90, audio: false));
    }
}
