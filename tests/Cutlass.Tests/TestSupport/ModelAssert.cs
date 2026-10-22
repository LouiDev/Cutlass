using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.Tests.TestSupport;

/// <summary>Assertions and lookups shared by the editing, session and serialization tests.</summary>
internal static class ModelAssert
{
    /// <summary>Fails with the violations when the project breaks an invariant.</summary>
    public static void Valid(Project project)
    {
        var errors = TimelineValidator.Check(project);
        Assert.True(errors.IsEmpty, "Validator: " + string.Join(" | ", errors));
    }

    /// <summary>Asserts the edit changed the project and the result is valid.</summary>
    public static Project Changed(Project before, EditResult result)
    {
        Assert.NotSame(before, result.Project);
        Valid(result.Project);
        return result.Project;
    }

    /// <summary>Asserts the edit returned the very same project reference.</summary>
    public static void Unchanged(Project before, EditResult result) => Assert.Same(before, result.Project);

    /// <summary>Finds a clip by id or fails.</summary>
    public static Clip Clip(Project project, string id) =>
        TimelineQuery.FindClip(project, id)?.Clip ?? throw new Xunit.Sdk.XunitException($"Clip {id} not found.");

    /// <summary>Finds a media clip by id or fails.</summary>
    public static MediaClip Media(Project project, string id) => Assert.IsType<MediaClip>(Clip(project, id));

    /// <summary>Finds a track by id or fails.</summary>
    public static Track Track(Project project, string id) =>
        TimelineQuery.FindTrack(project, id) ?? throw new Xunit.Sdk.XunitException($"Track {id} not found.");

    /// <summary>The clips of a track as <c>(id, start, duration)</c> tuples.</summary>
    public static (string Id, long Start, long Duration)[] Layout(Project project, string trackId) =>
        [.. Track(project, trackId).Clips.Select(c => (c.Id, c.Start, c.Duration))];

    /// <summary>The clips of a track as <c>(start, duration)</c> pairs, ignoring ids.</summary>
    public static (long Start, long Duration)[] Spans(Project project, string trackId) =>
        [.. Track(project, trackId).Clips.Select(c => (c.Start, c.Duration))];

    /// <summary>Asserts two projects are structurally equal (deep, including keyframe arrays).</summary>
    public static void Equivalent(Project expected, Project actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Media.ToArray(), actual.Media.ToArray());
        var e = expected.Sequence;
        var a = actual.Sequence;
        Assert.Equal(e.Settings, a.Settings);
        Assert.Equal(e.Markers.ToArray(), a.Markers.ToArray());
        Assert.Equal(e.InFrame, a.InFrame);
        Assert.Equal(e.OutFrame, a.OutFrame);
        Assert.Equal(e.HasHadClips, a.HasHadClips);
        Assert.Equal(e.Tracks.Length, a.Tracks.Length);
        for (var i = 0; i < e.Tracks.Length; i++)
        {
            var et = e.Tracks[i];
            var at = a.Tracks[i];
            Assert.Equal(et with { Clips = [], Transitions = [] }, at with { Clips = [], Transitions = [] });
            Assert.Equal(et.Transitions.ToArray(), at.Transitions.ToArray());
            Assert.Equal(et.Clips.Length, at.Clips.Length);
            for (var j = 0; j < et.Clips.Length; j++)
            {
                Assert.Equal(Normalize(et.Clips[j]), Normalize(at.Clips[j]));
                if (et.Clips[j] is MediaClip em && at.Clips[j] is MediaClip am)
                {
                    Assert.Equal(em.Audio.Keys.ToArray(), am.Audio.Keys.ToArray());
                }
            }
        }
    }

    private static Clip Normalize(Clip clip) =>
        clip is MediaClip m ? m with { Audio = m.Audio with { Keys = [] } } : clip;
}
