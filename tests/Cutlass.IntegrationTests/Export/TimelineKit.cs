using System.Collections.Immutable;
using Cutlass.Compiler;
using Cutlass.Compiler.Export;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.IntegrationTests.Export;

/// <summary>Builders for integration test timelines.</summary>
internal static class TimelineKit
{
    /// <summary>A video clip.</summary>
    public static MediaClip V(string id, long start, long duration, string media, long sourceInFlicks, int speed = 100, VideoProps? video = null) =>
        new(id, start, duration, true, null, media, StreamKind.Video, sourceInFlicks, speed, video ?? VideoProps.Default, AudioProps.Default);

    /// <summary>An audio clip.</summary>
    public static MediaClip A(string id, long start, long duration, string media, long sourceInFlicks, int speed = 100, AudioProps? audio = null) =>
        new(id, start, duration, true, null, media, StreamKind.Audio, sourceInFlicks, speed, VideoProps.Default, audio ?? AudioProps.Default);

    /// <summary>A title clip.</summary>
    public static TitleClip Title(string id, long start, long duration, TitleSpec spec, long fadeIn = 0, long fadeOut = 0) =>
        new(id, start, duration, true, spec, 1, fadeIn, fadeOut);

    /// <summary>A track.</summary>
    public static Track Track(string id, TrackKind kind, IEnumerable<Clip> clips, IEnumerable<Transition>? transitions = null) =>
        new(id, kind, id.ToUpperInvariant(), false, false, false, false, false, 0, [.. clips.OrderBy(c => c.Start)], [.. transitions ?? []]);

    /// <summary>A project; fails when it breaks a model invariant, so tests only render valid timelines.</summary>
    public static Project Project(int width, int height, FrameRate rate, IEnumerable<MediaAsset> media, params Track[] tracks)
    {
        var project = new Project(
            Cutlass.Core.Model.Project.CurrentSchemaVersion,
            [.. media],
            new Sequence(new SequenceSettings(width, height, rate), [.. tracks], ImmutableArray<Marker>.Empty, null, null, true));
        var errors = TimelineValidator.Check(project);
        Assert.True(errors.IsEmpty, "Invalid test timeline: " + string.Join(" | ", errors));
        return project;
    }

    /// <summary>A preview request producing BGRA frames.</summary>
    public static RenderRequest Preview(long start, long end, int width, int height, RenderPurpose purpose = RenderPurpose.PreviewVideo) =>
        new(start, end, width, height, purpose, true, false, false, AudioLayout.Mixed, 1, OutputTail.Preview, false);

    /// <summary>The source flicks of a time in microseconds.</summary>
    public static long Micros(long micros) => TimeMath.FlicksFromMicros(micros);
}
