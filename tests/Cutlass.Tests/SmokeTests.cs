using System.Collections.Immutable;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Tests;

/// <summary>Proves the test project wires up and the model records behave as immutable values.</summary>
public sealed class SmokeTests
{
    [Fact]
    public void EmptyProjectCanBeBuiltAndCopiedWithoutMutatingTheOriginal()
    {
        var sequence = new Sequence(
            new SequenceSettings(1920, 1080, FrameRate.Fps2997),
            ImmutableArray<Track>.Empty,
            ImmutableArray<Marker>.Empty,
            InFrame: null,
            OutFrame: null,
            HasHadClips: false);
        var project = new Project(Project.CurrentSchemaVersion, ImmutableArray<MediaAsset>.Empty, sequence);

        var changed = project with { Sequence = sequence with { InFrame = 10 } };

        Assert.Null(project.Sequence.InFrame);
        Assert.Equal(10, changed.Sequence.InFrame);
        Assert.NotSame(project, changed);
    }

    [Fact]
    public void ClipEndIsStartPlusDuration()
    {
        var clip = new MediaClip(
            "c_00000001", Start: 30, Duration: 45, Enabled: true, LinkId: null, MediaId: "m_00000001",
            StreamKind.Video, SourceInFlicks: 0, SpeedPercent: 100, VideoProps.Default, AudioProps.Default);

        Assert.Equal(75, clip.End);
        Assert.Equal(ColorAdjust.Neutral, clip.Video.Color);
        Assert.Equal(Transform.Identity, clip.Video.Transform);
    }
}
