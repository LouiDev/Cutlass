using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

public sealed class TrackTests
{
    private readonly SequentialIdGenerator _ids = new(1000);

    [Fact]
    public void NewProjectHasTwoVideoAndTwoAudioTracks()
    {
        var p = TimelineEdits.NewProject(_ids);
        Assert.Equal(["V1", "V2", "A1", "A2"], p.Sequence.Tracks.Select(t => t.Name));
        Assert.Equal([true, false, true, false], p.Sequence.Tracks.Select(t => t.Targeted));
        Assert.All(p.Sequence.Tracks, t => Assert.StartsWith("tr_", t.Id));
        Assert.Equal(TimelineEdits.DefaultSettings, p.Sequence.Settings);
        Assert.False(p.Sequence.HasHadClips);
        Assert.Equal(Project.CurrentSchemaVersion, p.SchemaVersion);
        ModelAssert.Valid(p);
    }

    [Fact]
    public void AddTrackRenumbersAutomaticNames()
    {
        var p = new ProjectBuilder().Build();
        var renamed = ModelAssert.Changed(p, TimelineEdits.SetTrack(p, V2, t => t with { Name = "B-roll" }, "Rename Track"));
        var r = TimelineEdits.AddTrack(_ids, renamed, TrackKind.Video, 0);
        var q = ModelAssert.Changed(renamed, r);
        Assert.Equal(["V1", "V2", "B-roll", "A1", "A2"], q.Sequence.Tracks.Select(t => t.Name));
        Assert.Equal(["tr_00001000"], r.Select.ToArray());
        Assert.Equal("tr_00001000", q.Sequence.Tracks[0].Id);
        Assert.Equal(V1, q.Sequence.Tracks[1].Id);

        var audio = ModelAssert.Changed(q, TimelineEdits.AddTrack(_ids, q, TrackKind.Audio));
        Assert.Equal(["V1", "V2", "B-roll", "A1", "A2", "A3"], audio.Sequence.Tracks.Select(t => t.Name));
    }

    [Fact]
    public void AtMostEightTracksPerKind()
    {
        var p = new ProjectBuilder().Build();
        for (var i = 0; i < 6; i++)
        {
            p = ModelAssert.Changed(p, TimelineEdits.AddTrack(_ids, p, TrackKind.Audio));
        }

        var r = TimelineEdits.AddTrack(_ids, p, TrackKind.Audio);
        ModelAssert.Unchanged(p, r);
        Assert.Contains("A sequence has at most 8 audio tracks.", r.Warnings);
    }

    [Fact]
    public void RemoveTrackRemovesItsClipsAndRenumbers()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 10)).With(V2, Title("t1", 0, 10)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.RemoveTrack(p, V1));
        Assert.Equal(["V1", "A1", "A2"], q.Sequence.Tracks.Select(t => t.Name));
        Assert.Equal(V2, q.Sequence.Tracks[0].Id);
        Assert.Null(TimelineQuery.FindClip(q, "c1"));
    }

    [Fact]
    public void TheLastTrackOfAKindAndLockedTracksStay()
    {
        var p = new ProjectBuilder().Lock(A2).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.RemoveTrack(p, A1));
        var r = TimelineEdits.RemoveTrack(q, A2);
        ModelAssert.Unchanged(q, r);
        Assert.Contains("A1 is locked.", r.Warnings);   // A2 was renumbered to A1
        var unlocked = ModelAssert.Changed(q, TimelineEdits.SetTrack(q, A2, t => t with { Locked = false }, "Unlock Track"));
        var last = TimelineEdits.RemoveTrack(unlocked, A2);
        ModelAssert.Unchanged(unlocked, last);
        Assert.Contains("A sequence needs at least one audio track.", last.Warnings);
    }

    [Fact]
    public void SetTrackKeepsKindSpecificFieldsConsistent()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 10)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SetTrack(p, V1, t => t with { Muted = true, Solo = true, GainDb = 6, Hidden = true, Name = "  ", Clips = [] }, "Track"));
        var v1 = ModelAssert.Track(q, V1);
        Assert.Equal((false, false, 0.0, true, "V1"), (v1.Muted, v1.Solo, v1.GainDb, v1.Hidden, v1.Name));
        Assert.Single(v1.Clips);

        var a = ModelAssert.Changed(p, TimelineEdits.SetTrack(p, A1, t => t with { Muted = true, Solo = true, GainDb = 40, Hidden = true }, "Track"));
        var a1 = ModelAssert.Track(a, A1);
        Assert.Equal((true, true, 12.0, false), (a1.Muted, a1.Solo, a1.GainDb, a1.Hidden));
        ModelAssert.Unchanged(p, TimelineEdits.SetTrack(p, A1, t => t, "Track"));
    }

    [Fact]
    public void LockedTracksCanBeUnlocked()
    {
        var p = new ProjectBuilder().Lock(V1).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SetTrack(p, V1, t => t with { Locked = false }, "Unlock Track"));
        Assert.False(ModelAssert.Track(q, V1).Locked);
    }
}
