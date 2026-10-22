using System.Collections.Immutable;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

public sealed class TimelineValidatorTests
{
    private static ImmutableArray<string> Check(ProjectBuilder b) => TimelineValidator.Check(b.Build());

    [Fact]
    public void AWellFormedProjectPasses()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, link: "l1"), V("c2", 50, 50, sourceFrame: 100), Title("t1", 200, 40))
            .With(A1, A("a1", 0, 50, link: "l1"))
            .Transition(V1, "x1", "c1", "c2", 20)
            .Marker("k1", 10)
            .InOut(5, 80)
            .Build();

        Assert.Empty(TimelineValidator.Check(p));
        Assert.True(TimelineValidator.IsValid(p));
    }

    [Fact]
    public void NewProjectIsValid()
    {
        Assert.True(TimelineValidator.IsValid(TimelineEdits.NewProject(new SequentialIdGenerator())));
    }

    [Fact]
    public void OverlappingClipsFail() =>
        Assert.Contains(Check(new ProjectBuilder().With(V1, V("c1", 0, 50), V("c2", 49, 10))), e => e.Contains("overlap"));

    [Fact]
    public void ZeroDurationFails() =>
        Assert.Contains(Check(new ProjectBuilder().With(V1, V("c1", 0, 0))), e => e.Contains("duration 0"));

    [Fact]
    public void NegativeStartFails() =>
        Assert.Contains(Check(new ProjectBuilder().With(V1, V("c1", -1, 10))), e => e.Contains("negative frame"));

    [Fact]
    public void AudioClipOnVideoTrackFails() =>
        Assert.Contains(Check(new ProjectBuilder().With(V1, A("a1", 0, 10))), e => e.Contains("Audio stream on Video track"));

    [Fact]
    public void TitleOnAudioTrackFails() =>
        Assert.Contains(Check(new ProjectBuilder().With(A1, Title("t1", 0, 10))), e => e.Contains("is on audio track"));

    [Fact]
    public void MissingMediaFails() =>
        Assert.Contains(Check(new ProjectBuilder().With(V1, V("c1", 0, 10, media: "m_gone"))), e => e.Contains("missing media"));

    [Fact]
    public void MissingStreamFails() =>
        Assert.Contains(Check(new ProjectBuilder().With(V1, V("c1", 0, 10, media: Music))), e => e.Contains("does not have"));

    [Fact]
    public void ClipPastTheSourceEndFails()
    {
        Assert.Empty(Check(new ProjectBuilder().With(V1, V("c1", 0, 100, media: Short))));
        Assert.Contains(Check(new ProjectBuilder().With(V1, V("c1", 0, 101, media: Short))), e => e.Contains("past the end"));
    }

    [Fact]
    public void ClipBeforeTheSourceStartFails() =>
        Assert.Contains(Check(new ProjectBuilder().With(V1, V("c1", 0, 10, sourceFrame: -1))), e => e.Contains("before its source"));

    [Fact]
    public void StillsAndTitlesAreUnbounded() =>
        Assert.Empty(Check(new ProjectBuilder().With(V1, V("s1", 0, 100_000, media: Still), Title("t1", 100_000, 500_000))));

    [Fact]
    public void TransitionMustJoinButtingClips() =>
        Assert.Contains(
            Check(new ProjectBuilder().With(V1, V("c1", 0, 50), V("c2", 51, 50, sourceFrame: 100)).Transition(V1, "x1", "c1", "c2", 10)),
            e => e.Contains("do not butt"));

    [Fact]
    public void TransitionMustReferenceClipsOnItsTrack() =>
        Assert.Contains(
            Check(new ProjectBuilder().With(V1, V("c1", 0, 50)).Transition(V1, "x1", "c1", "c_05", 10)),
            e => e.Contains("not on track"));

    [Fact]
    public void TransitionShorterThanTwoFramesFails() =>
        Assert.Contains(
            Check(new ProjectBuilder().With(V1, V("c1", 0, 50), V("c2", 50, 50, sourceFrame: 100)).Transition(V1, "x1", "c1", "c2", 1)),
            e => e.Contains("has duration 1"));

    [Fact]
    public void TransitionLongerThanItsClipsFails() =>
        Assert.Contains(
            Check(new ProjectBuilder().With(V1, V("c1", 0, 5, sourceFrame: 100), V("c2", 5, 50, sourceFrame: 300)).Transition(V1, "x1", "c1", "c2", 12)),
            e => e.Contains("longer than its clips allow"));

    [Fact]
    public void TransitionNeedsSourceHandles()
    {
        // The right clip starts at source 0, so it has no frames before its start for a = 5.
        Assert.Contains(
            Check(new ProjectBuilder().With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50)).Transition(V1, "x1", "c1", "c2", 10)),
            e => e.Contains("before its source"));
    }

    [Fact]
    public void TwoTransitionsOverlappingOnOneClipFail() =>
        Assert.Contains(
            Check(new ProjectBuilder()
                .With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 10, sourceFrame: 300), V("c3", 60, 50, sourceFrame: 500))
                .Transition(V1, "x1", "c1", "c2", 12)
                .Transition(V1, "x2", "c2", "c3", 12)),
            e => e.Contains("overlap"));

    [Fact]
    public void AudioCrossfadeOnVideoTrackFails() =>
        Assert.Contains(
            Check(new ProjectBuilder().With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 50, sourceFrame: 300))
                .Transition(V1, "x1", "c1", "c2", 10, TransitionKind.AudioCrossfade)),
            e => e.Contains("AudioCrossfade"));

    [Fact]
    public void SpeedOutOfRangeFails() =>
        Assert.Contains(Check(new ProjectBuilder().With(V1, V("c1", 0, 10, speed: 500))), e => e.Contains("speed 500%"));

    [Fact]
    public void FadesLongerThanTheClipFail()
    {
        var clip = V("c1", 0, 10) with { Video = VideoProps.Default with { FadeInFrames = 6, FadeOutFrames = 5 } };
        Assert.Contains(Check(new ProjectBuilder().With(V1, clip)), e => e.Contains("fades 6+5"));
    }

    [Fact]
    public void KeyframesOutsideTheClipOrOutOfOrderFail()
    {
        var outside = A("a1", 0, 10) with { Audio = AudioProps.Default with { Keys = [new VolumeKey(10, 0)] } };
        var unordered = A("a2", 20, 10) with { Audio = AudioProps.Default with { Keys = [new VolumeKey(5, 0), new VolumeKey(2, 0)] } };
        var errors = Check(new ProjectBuilder().With(A1, outside, unordered));
        Assert.Contains(errors, e => e.Contains("outside [0, 10)"));
        Assert.Contains(errors, e => e.Contains("out of order"));
    }

    [Fact]
    public void LinkedClipsMayDifferInStartAndDuration() =>
        Assert.Empty(Check(new ProjectBuilder().With(V1, V("c1", 0, 50, link: "l1")).With(A1, A("a1", 10, 70, link: "l1"))));

    [Fact]
    public void DuplicateIdsFail() =>
        Assert.Contains(Check(new ProjectBuilder().With(V1, V("c1", 0, 10)).With(A1, A("c1", 0, 10))), e => e.Contains("Duplicate clip id"));

    [Fact]
    public void TooManyTracksFail()
    {
        var ids = new SequentialIdGenerator();
        var p = TimelineEdits.NewProject(ids);
        var extra = Enumerable.Range(0, 7).Select(i => new Track($"tr_x{i}", TrackKind.Video, $"V{i + 3}", false, false, false, false, false, 0, [], []));
        var tracks = p.Sequence.Tracks.InsertRange(2, extra);
        var errors = TimelineValidator.Check(p with { Sequence = p.Sequence with { Tracks = tracks } });
        Assert.Contains(errors, e => e.Contains("Too many tracks"));
    }

    [Fact]
    public void VideoTrackAfterAudioTrackFails()
    {
        var p = new ProjectBuilder().Build();
        var reordered = p.Sequence.Tracks.Reverse().ToImmutableArray();
        Assert.Contains(TimelineValidator.Check(p with { Sequence = p.Sequence with { Tracks = reordered } }), e => e.Contains("follows an audio track"));
    }

    [Fact]
    public void InOutAndMarkersAreChecked()
    {
        var p = new ProjectBuilder().InOut(50, 50).Build();
        Assert.Contains(TimelineValidator.Check(p), e => e.Contains("not after In"));
        var markers = p with { Sequence = p.Sequence with { InFrame = null, OutFrame = null, Markers = [new Marker("k1", 9, "", "#FFFFFF"), new Marker("k2", 3, "", "#FFFFFF")] } };
        Assert.Contains(TimelineValidator.Check(markers), e => e.Contains("out of order"));
    }

    [Fact]
    public void OddSequenceSizeFails()
    {
        var p = new ProjectBuilder().Build();
        var odd = p with { Sequence = p.Sequence with { Settings = p.Sequence.Settings with { Width = 1921 } } };
        Assert.Contains(TimelineValidator.Check(odd), e => e.Contains("even"));
    }
}
