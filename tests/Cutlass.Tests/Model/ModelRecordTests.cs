using System.Collections.Immutable;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Tests.Model;

/// <summary>Checks the immutable model records against TECH_BRIEF 3.2.</summary>
public sealed class ModelRecordTests
{
    [Fact]
    public void NeutralDefaultsMatchTheBrief()
    {
        Assert.Equal(new ColorAdjust(0, 1, 1, 1), ColorAdjust.Neutral);
        Assert.Equal(new Transform(0, 0, 1, 0, 0, 0, 0), Transform.Identity);
        Assert.Equal(1.0, VideoProps.Default.Opacity);
        Assert.Equal(0, VideoProps.Default.FadeInFrames);
        Assert.Equal(0, VideoProps.Default.FadeOutFrames);
        Assert.Equal(0, AudioProps.Default.GainDb);
        Assert.True(AudioProps.Default.Keys.IsEmpty);
    }

    [Fact]
    public void TitleClipHasNoLinkAndAComputedEnd()
    {
        var clip = new TitleClip("c_00000002", Start: 300, Duration: 90, Enabled: true, MakeTitle(), 1.0, 15, 15);

        Assert.Null(clip.LinkId);
        Assert.Equal(390, clip.End);
        Assert.IsAssignableFrom<Clip>(clip);
    }

    [Fact]
    public void WithCreatesACopyAndLeavesTheOriginalUntouched()
    {
        var clip = MakeMediaClip();

        var moved = clip with { Start = 200 };
        var faded = clip with { Video = clip.Video with { FadeInFrames = 10 } };

        Assert.Equal(100, clip.Start);
        Assert.Equal(0, clip.Video.FadeInFrames);
        Assert.Equal(200, moved.Start);
        Assert.Equal(250, moved.End);
        Assert.Equal(10, faded.Video.FadeInFrames);
        Assert.Same(clip.Audio, faded.Audio);
    }

    [Fact]
    public void RecordsHaveValueEquality()
    {
        Assert.Equal(MakeMediaClip(), MakeMediaClip());
        Assert.NotEqual(MakeMediaClip(), MakeMediaClip() with { SpeedPercent = 50 });
        Assert.Equal(new SequenceSettings(1920, 1080, FrameRate.Fps2997), new SequenceSettings(1920, 1080, FrameRate.Fps2997));
    }

    [Fact]
    public void TrackHoldsMixedClipKindsInAnImmutableArray()
    {
        var media = MakeMediaClip();
        var title = new TitleClip("c_00000002", Start: 150, Duration: 30, Enabled: true, MakeTitle(), 1.0, 0, 0);
        var track = new Track(
            "tr_v1", TrackKind.Video, "V1", Hidden: false, Locked: false, Targeted: true, Muted: false, Solo: false,
            GainDb: 0, [media, title], ImmutableArray<Transition>.Empty);

        var added = track with { Clips = track.Clips.Add(MakeMediaClip() with { Id = "c_00000003", Start = 180 }) };

        Assert.Equal(2, track.Clips.Length);
        Assert.Equal(3, added.Clips.Length);
        Assert.IsType<TitleClip>(added.Clips[1]);
    }

    [Fact]
    public void EnumsListTheBriefMembersInOrder()
    {
        Assert.Equal(["Video", "Audio"], Enum.GetNames<TrackKind>());
        Assert.Equal(["Video", "Audio"], Enum.GetNames<StreamKind>());
        Assert.Equal(["Center", "LowerThird", "Top"], Enum.GetNames<TitleAnchor>());
        Assert.Equal(
            ["Crossfade", "DipToBlack", "DipToWhite", "WipeLeft", "WipeRight", "SlideLeft", "SlideRight", "CircleOpen", "AudioCrossfade"],
            Enum.GetNames<TransitionKind>());
    }

    private static MediaClip MakeMediaClip() =>
        new(
            "c_00000001",
            Start: 100,
            Duration: 50,
            Enabled: true,
            LinkId: "l_00000001",
            MediaId: "m_00000001",
            StreamKind.Video,
            SourceInFlicks: 1_411_200_000,
            SpeedPercent: 100,
            VideoProps.Default,
            AudioProps.Default);

    private static TitleSpec MakeTitle() =>
        new("Day One", "builtin:Inter-Bold", 96, "#FFFFFFFF", 3, "#00000099", false, "#00000080", 16,
            TitleAnchor.LowerThird, 0, 0);
}
