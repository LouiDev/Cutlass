using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Serialization;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Serialization;

public sealed class ProjectSerializerTests
{
    private static string GoldenPath(string name, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Golden", name);

    /// <summary>A project that uses every field: media of each kind, both clip types, transitions, fades, keys, flags.</summary>
    internal static Project Rich()
    {
        var c1 = V("c1", 0, 50, link: "l1", sourceFrame: 100) with
        {
            Video = new VideoProps(new Transform(-120.5, 64.25, 0.5, 0.1, 0.05, 0, 0.2), 0.75, new ColorAdjust(0.1, 1.2, 0.8, 1.5), 5, 0),
        };
        var c2 = V("c2", 50, 40, media: Short, sourceFrame: 20) with { Enabled = false };
        var t1 = Title("t1", 120, 60, "Day One\n\u00c4 \"quoted\" \\ end") with
        {
            Opacity = 0.9,
            FadeInFrames = 15,
            FadeOutFrames = 15,
            Title = TitleSpec.Default with { Text = "Day One\n\u00c4 \"quoted\" \\ end", Box = true, Anchor = TitleAnchor.Top, OffsetX = 12.5, OffsetY = -8, Font = "C:/Fonts/odd [name].ttf" },
        };
        var a1 = A("a1", 0, 50, link: "l1", sourceFrame: 100) with
        {
            Audio = new AudioProps(-3.25, 0, 12, [new VolumeKey(10, 0), new VolumeKey(30, -12.5)]),
        };
        var p = new ProjectBuilder()
            .With(V1, c1, c2, t1)
            .With(V2, V("s1", 0, 125, media: Still))
            .With(A1, a1, A("a2", 50, 40, media: Short, sourceFrame: 20, speed: 150))
            .With(A2, A("m1", 10, 200, media: Music))
            .Transition(V1, "x1", "c1", "c2", 10, TransitionKind.DipToWhite)
            .Transition(A1, "x2", "a1", "a2", 8)
            .Marker("k1", 30, "Intro")
            .Marker("k2", 75, "Drop")
            .InOut(10, 150)
            .Build();
        p = TimelineEdits.SetTrack(p, V2, t => t with { Hidden = true, Name = "B-roll" }, "Track").Project;
        p = TimelineEdits.SetTrack(p, A1, t => t with { Muted = true, GainDb = -2.5 }, "Track").Project;
        p = TimelineEdits.SetTrack(p, A2, t => t with { Solo = true, Locked = true }, "Track").Project;
        ModelAssert.Valid(p);
        return p;
    }

    [Fact]
    [UseCulture("de-DE")]
    public void RoundTripPreservesEverything()
    {
        var p = Rich();
        var json = ProjectSerializer.Serialize(p);
        var q = ProjectSerializer.Deserialize(json);
        ModelAssert.Equivalent(p, q);
        Assert.Equal(json, ProjectSerializer.Serialize(q));
        Assert.Equal(DateTimeKind.Utc, q.Media[0].MtimeUtc.Kind);
    }

    [Fact]
    [UseCulture("de-DE")]
    public void GoldenFileMatchesByteForByte()
    {
        var path = GoldenPath("project_v1.json");
        var bytes = ProjectSerializer.SerializeToUtf8Bytes(Rich());
        if (Environment.GetEnvironmentVariable("CUTLASS_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllBytes(path, bytes);
        }

        Assert.True(File.Exists(path), $"Golden file {path} is missing; run with CUTLASS_UPDATE_GOLDEN=1.");
        Assert.Equal(Encoding.UTF8.GetString(File.ReadAllBytes(path)), Encoding.UTF8.GetString(bytes));
        ModelAssert.Equivalent(Rich(), ProjectSerializer.Deserialize(File.ReadAllBytes(path)));
    }

    [Fact]
    [UseCulture("de-DE")]
    public void NumbersUseTheInvariantCulture()
    {
        var json = ProjectSerializer.Serialize(Rich());
        Assert.Contains("\"x\": -120.5", json);
        Assert.Contains("\"gainDb\": -3.25", json);
        Assert.Contains("\"frameRate\": \"25\"", json);
        Assert.DoesNotContain("120,5", json);
        Assert.DoesNotContain("3,25", json);
    }

    [Fact]
    public void OutputIsUtf8WithoutBomAndUsesLfLineEndings()
    {
        var bytes = ProjectSerializer.SerializeToUtf8Bytes(Rich());
        Assert.Equal((byte)'{', bytes[0]);
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.DoesNotContain((byte)'\r', bytes);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("{\n  \"format\": \"cutlass-project\",\n  \"schemaVersion\": 1,\n  \"app\": \"Cutlass ", text);
        Assert.Contains("\"inFrame\": 10", text);
        Assert.Contains("\"mtimeUtc\": \"2026-10-01T09:12:44Z\"", text);
    }

    [Fact]
    public void FieldsFollowTheBriefLayout()
    {
        var root = JsonNode.Parse(ProjectSerializer.Serialize(Rich()))!;
        var tracks = root["sequence"]!["tracks"]!.AsArray();
        var v1 = tracks[0]!.AsObject();
        Assert.Equal(["id", "kind", "name", "hidden", "locked", "targeted", "clips", "transitions"], v1.Select(kv => kv.Key));
        var a1 = tracks[2]!.AsObject();
        Assert.Equal(["id", "kind", "name", "muted", "solo", "locked", "targeted", "gainDb", "clips", "transitions"], a1.Select(kv => kv.Key));
        var media = v1["clips"]![0]!.AsObject();
        Assert.Equal(["type", "id", "stream", "mediaId", "start", "duration", "enabled", "linkId", "sourceInFlicks", "speedPercent", "video"], media.Select(kv => kv.Key));
        Assert.Equal(new JsonArray(0.1, 0.05, 0.0, 0.2).ToJsonString(), media["video"]!["transform"]!["crop"]!.ToJsonString());
        var title = v1["clips"]![2]!.AsObject();
        Assert.Equal(["type", "id", "start", "duration", "enabled", "title", "opacity", "fadeIn", "fadeOut"], title.Select(kv => kv.Key));
        Assert.Equal("top", (string)title["title"]!["anchor"]!);
        Assert.Equal("dipToWhite", (string)v1["transitions"]![0]!["kind"]!);
        Assert.Equal("audioCrossfade", (string)a1["transitions"]![0]!["kind"]!);
        var audioClip = a1["clips"]![0]!.AsObject();
        Assert.Null(audioClip["video"]);
        Assert.Equal(-12.5, (double)audioClip["audio"]!["keys"]![1]!["db"]!);
        Assert.Equal(48000, (int)root["sequence"]!["sampleRate"]!);
        Assert.Equal("1/12800", (string)root["media"]![0]!["info"]!["video"]!["timeBase"]!);
    }

    [Fact]
    public void TheBriefExampleReads()
    {
        var p = ProjectSerializer.Deserialize(File.ReadAllText(GoldenPath("brief_example.json")));
        Assert.Equal(Project.CurrentSchemaVersion, p.SchemaVersion);
        var media = Assert.Single(p.Media);
        Assert.Equal(("m_7f2c4k9q", "D:/Footage/beach.mp4", "../Footage/beach.mp4"), (media.Id, media.Path, media.RelativePath));
        Assert.Equal(new DateTime(2026, 10, 1, 9, 12, 44, DateTimeKind.Utc), media.MtimeUtc);
        Assert.Equal(DateTimeKind.Utc, media.MtimeUtc.Kind);
        Assert.Equal(734003200, media.SizeBytes);
        Assert.Equal((64200000L, 0L, false), (media.Info.DurationMicros, media.Info.FormatStartMicros, media.Info.IsStill));
        var video = media.Info.Video!;
        Assert.Equal(("hevc", 3840, 2160, "yuv420p10le", 48000000L), (video.Codec, video.Width, video.Height, video.PixFmt, video.BitRate));
        Assert.Equal(new Rational(30000, 1001), video.RFrameRate);
        Assert.Equal(new Rational(1, 30000), video.TimeBase);
        Assert.Equal(new AudioStreamInfo(1, "aac", 48000, 2), media.Info.Audio);

        var seq = p.Sequence;
        Assert.Equal(new SequenceSettings(1920, 1080, FrameRate.Fps2997), seq.Settings);
        Assert.Equal((null, null, true), (seq.InFrame, seq.OutFrame, seq.HasHadClips));
        Assert.Equal(new Marker("k_3m9q0aa1", 300, "Drop", "#F5D547"), Assert.Single(seq.Markers));

        var v1 = seq.Tracks[0];
        Assert.Equal(("tr_v1", TrackKind.Video, "V1", false, false, true), (v1.Id, v1.Kind, v1.Name, v1.Hidden, v1.Locked, v1.Targeted));
        var c01 = Assert.IsType<MediaClip>(v1.Clips[0]);
        Assert.Equal(("c_01", StreamKind.Video, 0L, 150L, "l_01", 1411200000L, 100), (c01.Id, c01.Stream, c01.Start, c01.Duration, c01.LinkId, c01.SourceInFlicks, c01.SpeedPercent));
        Assert.Equal(VideoProps.Default, c01.Video);
        var c02 = Assert.IsType<TitleClip>(v1.Clips[1]);
        Assert.Equal(("Day One", "builtin:Inter-Bold", 96.0, TitleAnchor.LowerThird), (c02.Title.Text, c02.Title.Font, c02.Title.Size, c02.Title.Anchor));
        Assert.Equal((1.0, 15L, 15L), (c02.Opacity, c02.FadeInFrames, c02.FadeOutFrames));
        Assert.Equal(TitleSpec.Default with { Text = "Day One" }, c02.Title);
        Assert.Equal(new Transition("x_01", TransitionKind.Crossfade, "c_01", "c_05", 30), Assert.Single(v1.Transitions));

        var a1 = seq.Tracks[1];
        Assert.Equal(("tr_a1", TrackKind.Audio, false, false, 0.0), (a1.Id, a1.Kind, a1.Muted, a1.Solo, a1.GainDb));
        var c11 = Assert.IsType<MediaClip>(Assert.Single(a1.Clips));
        Assert.Equal((StreamKind.Audio, 12L, 0.0), (c11.Stream, c11.Audio.FadeOutFrames, c11.Audio.GainDb));
        Assert.Equal([new VolumeKey(90, 0), new VolumeKey(120, -12)], c11.Audio.Keys.ToArray());

        // The example's transition names c_05, which is not on the track: the only invariant it breaks.
        var error = Assert.Single(TimelineValidator.Check(p));
        Assert.Contains("x_01", error);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        var root = JsonNode.Parse(ProjectSerializer.Serialize(Rich()))!.AsObject();
        root["futureTopLevel"] = new JsonObject { ["nested"] = 1 };
        root["sequence"]!["tracks"]![0]!["clips"]![0]!["futureClipField"] = "x";
        root["media"]![0]!["info"]!["futureInfo"] = new JsonArray(1, 2);
        var p = ProjectSerializer.Deserialize(root.ToJsonString());
        ModelAssert.Equivalent(Rich(), p);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreTolerated()
    {
        var json = "// saved by hand\n" + ProjectSerializer.Serialize(Rich()).Replace("\"targeted\": true,", "\"targeted\": true, /* note */", StringComparison.Ordinal);
        ModelAssert.Equivalent(Rich(), ProjectSerializer.Deserialize(json));
    }

    [Fact]
    public void SchemaZeroIsMigrated()
    {
        const string json = """
            {
              "media": [],
              "sequence": {
                "width": 1280, "height": 720, "fps": 29.97, "hasHadClips": true,
                "tracks": [
                  { "id": "tr_v1", "kind": "video", "name": "V1", "targeted": true,
                    "clips": [ { "id": "c_1", "start": 0, "duration": 30, "enabled": true, "title": { "text": "Hi", "anchor": "center" } } ] },
                  { "id": "tr_a1", "kind": "audio", "name": "A1", "clips": [] }
                ]
              }
            }
            """;
        var p = ProjectSerializer.Deserialize(json);
        Assert.Equal(new SequenceSettings(1280, 720, FrameRate.Fps2997), p.Sequence.Settings);
        var title = Assert.IsType<TitleClip>(Assert.Single(p.Sequence.Tracks[0].Clips));
        Assert.Equal("Hi", title.Title.Text);
        Assert.Empty(p.Sequence.Markers);
        Assert.Equal(Project.CurrentSchemaVersion, p.SchemaVersion);
        ModelAssert.Valid(p);
    }

    [Fact]
    public void MigratorUpgradesInPlace()
    {
        var node = JsonNode.Parse("""{ "sequence": { "fps": "25", "tracks": [ { "clips": [ { "mediaId": "m_1" } ] } ] } }""")!;
        Assert.Equal(0, ProjectMigrator.ReadVersion(node));
        var migrated = ProjectMigrator.Migrate(node);
        Assert.Same(node, migrated);
        Assert.Equal(1, ProjectMigrator.ReadVersion(migrated));
        Assert.Equal("cutlass-project", (string)migrated["format"]!);
        Assert.Equal("25", (string)migrated["sequence"]!["frameRate"]!);
        Assert.Null(migrated["sequence"]!["fps"]);
        Assert.Equal("media", (string)migrated["sequence"]!["tracks"]![0]!["clips"]![0]!["type"]!);
        Assert.NotNull(migrated["sequence"]!["tracks"]![0]!["transitions"]);

        var current = JsonNode.Parse("""{ "schemaVersion": 1, "sequence": { "fps": "25" } }""")!;
        ProjectMigrator.Migrate(current);
        Assert.NotNull(current["sequence"]!["fps"]);
    }

    [Theory]
    [InlineData("""{ "format": "cutlass-project", "schemaVersion": 2, "sequence": {} }""", "newer version")]
    [InlineData("""{ "format": "something-else", "schemaVersion": 1 }""", "not a Cutlass project")]
    [InlineData("""{ "schemaVersion": "one" }""", "whole number")]
    [InlineData("""[1, 2, 3]""", "JSON object")]
    [InlineData("""{ "format": "cutlass-project", """, "not valid JSON")]
    [InlineData("""{ "schemaVersion": 1, "sequence": { "tracks": [ { "kind": "hologram" } ] } }""", "Unknown track kind")]
    [InlineData("""{ "schemaVersion": 1, "sequence": { "tracks": [ { "kind": "video", "clips": [ { "type": "shape" } ] } ] } }""", "Unknown clip type")]
    [InlineData("""{ "schemaVersion": 1, "sequence": { "width": "wide" } }""", "unexpected shape")]
    [InlineData("""{ "schemaVersion": 1, "media": [ null ] }""", "empty media entry")]
    [InlineData("""{ "schemaVersion": 1, "sequence": { "tracks": [ null ] } }""", "empty track entry")]
    [InlineData("""{ "schemaVersion": 1, "sequence": { "markers": [ null ] } }""", "empty marker entry")]
    [InlineData("""{ "schemaVersion": 1, "sequence": { "tracks": [ { "kind": "video", "clips": [ null ] } ] } }""", "empty clip entry")]
    [InlineData("""{ "schemaVersion": 1, "sequence": { "tracks": [ { "kind": "video", "transitions": [ null ] } ] } }""", "empty transition entry")]
    [InlineData("""{ "schemaVersion": 1, "sequence": { "tracks": [ { "kind": "audio", "clips": [ { "type": "media", "stream": "audio", "audio": { "keys": [ null ] } } ] } ] } }""", "empty volume key entry")]
    [InlineData("""{ "sequence": { "fps": true } }""", "fps")]
    public void UnreadableFilesThrowInvalidData(string json, string message)
    {
        var ex = Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize(json));
        Assert.Contains(message, ex.Message);
        Assert.False(ProjectSerializer.TryDeserialize(json, out var project, out var error));
        Assert.Null(project);
        Assert.Contains(message, error);
    }

    [Fact]
    public void BytesWithABomAreRead()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(ProjectSerializer.SerializeToUtf8Bytes(Rich())).ToArray();
        ModelAssert.Equivalent(Rich(), ProjectSerializer.Deserialize(bytes));
        Assert.Throws<InvalidDataException>(() => ProjectSerializer.Deserialize(new byte[] { 0xC3, 0x28 }));
    }

    [Fact]
    public void PathsUseForwardSlashesAndRelativePathsAreComputed()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "cutlass-paths"));
        var mediaPath = Path.Combine(root, "Footage", "clip one.mp4");
        var projectPath = Path.Combine(root, "Projects", "edit.cutlass");
        var asset = Asset("m_x", mediaPath, 1_000_000, true, true);
        var p = new ProjectBuilder().Build() with { Media = [asset] };

        var json = ProjectSerializer.Serialize(p, projectPath);
        var read = ProjectSerializer.Deserialize(json).Media[0];
        Assert.Equal(mediaPath.Replace('\\', '/'), read.Path);
        Assert.Equal("../Footage/clip one.mp4", read.RelativePath);
        Assert.DoesNotContain("\\\\", json);

        var candidates = ProjectSerializer.CandidateMediaPaths(read with { Path = "Z:/moved/clip one.mp4" }, Path.Combine(root, "Projects", "edit.cutlass"));
        Assert.Equal(2, candidates.Length);
        Assert.Equal(mediaPath, candidates[1]);
        Assert.Single(ProjectSerializer.CandidateMediaPaths(read, null));
    }

    [Fact]
    public void WithoutAProjectPathTheStoredRelativePathIsKept()
    {
        var asset = Asset("m_x", "D:/a.mp4", 1_000_000, true, true) with { RelativePath = "../a.mp4" };
        var p = new ProjectBuilder().Build() with { Media = [asset] };
        Assert.Equal("../a.mp4", ProjectSerializer.Deserialize(ProjectSerializer.Serialize(p)).Media[0].RelativePath);
    }

    [Fact]
    public void LocalAndUnspecifiedTimesAreWrittenAsUtc()
    {
        var local = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Local);
        var asset = Asset("m_x", "D:/a.mp4", 1_000_000, true, true) with { MtimeUtc = local };
        var p = new ProjectBuilder().Build() with { Media = [asset] };
        var read = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(p)).Media[0].MtimeUtc;
        Assert.Equal(local.ToUniversalTime(), read);
        Assert.Equal(DateTimeKind.Utc, read.Kind);
    }

    [Fact]
    [UseCulture("de-DE")]
    public void ViewStateRoundTripsAndNeverFails()
    {
        var state = new ViewState(3.5, 120, 48.5, 300, ImmutableDictionary<string, double>.Empty.Add("tr_v1", 80.5).Add("tr_a1", 500), "half", false, "list");
        var json = ProjectSerializer.SerializeViewState(state);
        Assert.Contains("\"pixelsPerFrame\": 3.5", json);
        Assert.DoesNotContain("\r", json);
        var read = ProjectSerializer.DeserializeViewState(json);
        Assert.Equal((3.5, 120L, 48.5, 300L, "half", false, "list"), (read.PixelsPerFrame, read.ScrollFrame, read.ScrollY, read.Playhead, read.PreviewQuality, read.UseProxies, read.BinView));
        Assert.Equal(80.5, read.TrackHeights["tr_v1"]);
        Assert.Equal(ViewState.MaxTrackHeight, read.TrackHeights["tr_a1"]);

        Assert.Same(ViewState.Default, ProjectSerializer.DeserializeViewState(null));
        Assert.Same(ViewState.Default, ProjectSerializer.DeserializeViewState("{ broken"));
        var odd = ProjectSerializer.DeserializeViewState("""{ "pixelsPerFrame": 900, "previewQuality": "ultra", "binView": "tiles", "playhead": -4, "extra": true }""");
        Assert.Equal((ViewState.MaxPixelsPerFrame, "auto", "grid", 0L, true), (odd.PixelsPerFrame, odd.PreviewQuality, odd.BinView, odd.Playhead, odd.UseProxies));
    }

    [Fact]
    public void AppNameCarriesTheVersion()
    {
        Assert.Equal("Cutlass 0.1.0", ProjectSerializer.AppName);
    }
}
