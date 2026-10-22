using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

/// <summary>
/// Applies thousands of seeded random edits through a <see cref="ProjectSession"/> (TECH_BRIEF 11.1): the validator
/// must hold after every one, and undoing everything must return the exact initial reference (and redo the final one).
/// </summary>
public sealed class EditFuzzTests
{
    private static readonly string[] MediaIds = [Long, Short, Still, Music, Uhd];

    [Theory]
    [InlineData(20261021, 5000)]
    [InlineData(7, 1500)]
    [InlineData(424242, 1500)]
    public void RandomEditsKeepTheModelValidAndUndoRestoresEveryState(int seed, int steps)
    {
        var rng = new Random(seed);
        var ids = new SequentialIdGenerator();
        var initial = new ProjectBuilder()
            .With(V1, V("c1", 0, 60, link: "l1", sourceFrame: 50), V("c2", 60, 40, media: Short, sourceFrame: 10), Title("t1", 150, 30))
            .With(V2, V("s1", 20, 40, media: Still))
            .With(A1, A("a1", 0, 60, link: "l1", sourceFrame: 50), A("a2", 70, 50, media: Music, sourceFrame: 100))
            .Transition(V1, "x1", "c1", "c2", 10)
            .Marker("k1", 30)
            .Build();
        ModelAssert.Valid(initial);

        long now = 0;
        var session = new ProjectSession(initial, () => now, historyCapacity: steps + 10);
        var snapshots = new List<Project> { initial };
        var clipboard = ClipClipboard.Empty;
        for (var step = 0; step < steps; step++)
        {
            now += 2000;     // never coalesce, so every change is its own undo step
            var op = rng.Next(30);
            if (op == 29)
            {
                // A gesture: several live updates from the gesture start, then commit or cancel.
                RunGesture(session, rng, ids);
            }
            else
            {
                if (op == 28)
                {
                    clipboard = ClipClipboard.Copy(session.Current, RandomClips(session.Current, rng, 3));
                }

                var board = clipboard;
                var result = session.Execute(p => RandomEdit(p, op, rng, ids, board));
                Assert.NotNull(result.Label);
            }

            var errors = TimelineValidator.Check(session.Current);
            Assert.True(errors.IsEmpty, $"Step {step} (op {op}): {string.Join(" | ", errors)}");
            if (!ReferenceEquals(session.Current, snapshots[^1]))
            {
                snapshots.Add(session.Current);
            }

            Assert.Equal(snapshots.Count - 1, session.UndoCount);
        }

        var final = session.Current;
        for (var i = snapshots.Count - 2; i >= 0; i--)
        {
            Assert.True(session.Undo());
            Assert.Same(snapshots[i], session.Current);
        }

        Assert.False(session.Undo());
        Assert.Same(initial, session.Current);
        while (session.Redo())
        {
        }

        Assert.Same(final, session.Current);
        Assert.True(snapshots.Count > steps / 3, $"Only {snapshots.Count} of {steps} edits changed the project.");
    }

    private static void RunGesture(ProjectSession session, Random rng, IIdGenerator ids)
    {
        var p = session.Current;
        var clip = RandomClip(p, rng);
        if (clip is null)
        {
            return;
        }

        var edge = rng.Next(2) == 0 ? ClipEdge.Start : ClipEdge.End;
        var ripple = rng.Next(2) == 0;
        var move = rng.Next(2) == 0;
        session.BeginGesture();
        for (var i = 0; i < 6; i++)
        {
            var delta = rng.Next(-40, 41);
            session.UpdateGesture(start => move
                ? TimelineEdits.Move(ids, start, [clip.Id], delta, 0, EditMode.Overwrite)
                : TimelineEdits.Trim(start, clip.Id, edge, delta, ripple, includeLinked: true));
            Assert.Empty(TimelineValidator.Check(session.Current));
        }

        if (rng.Next(4) == 0)
        {
            session.CancelGesture();
            Assert.Same(p, session.Current);
        }
        else
        {
            session.CommitGesture("Drag");
        }
    }

    private static EditResult RandomEdit(Project p, int op, Random rng, IIdGenerator ids, ClipClipboard clipboard)
    {
        var tracks = p.Sequence.Tracks;
        var length = Math.Max(50, TimelineQuery.Duration(p));
        long Frame() => rng.NextInt64(0, length + 60);
        long Delta(int max) => rng.Next(-max, max + 1);
        string TrackId() => tracks[rng.Next(tracks.Length)].Id;
        var clip = RandomClip(p, rng);
        var someClips = RandomClips(p, rng, 3);
        switch (op)
        {
            case 0:
            case 1:
            {
                var media = MediaIds[rng.Next(MediaIds.Length)];
                var video = TimelineQuery.TracksOfKind(p, TrackKind.Video);
                var audio = TimelineQuery.TracksOfKind(p, TrackKind.Audio);
                var placements = TimelineEdits.SourcePlacements(
                    p, media, rng.NextInt64(0, 120) * F, rng.Next(1, 120), video[rng.Next(video.Length)].Id, audio[rng.Next(audio.Length)].Id);
                return TimelineEdits.Place(ids, p, placements, Frame(), rng.Next(3) == 0 ? EditMode.Insert : EditMode.Overwrite);
            }

            case 2:
                return TimelineEdits.Place(ids, p, [new ClipPlacement(TrackId(), Title("tt", 0, rng.Next(1, 80)))], Frame(), EditMode.Overwrite);
            case 3:
                return TimelineEdits.Move(ids, p, TimelineQuery.WithLinkedPartners(p, someClips), Delta(60), rng.Next(-1, 2), rng.Next(4) == 0 ? EditMode.Insert : EditMode.Overwrite);
            case 4:
            case 5:
                return clip is null
                    ? EditResult.Unchanged(p, "Trim")
                    : TimelineEdits.Trim(p, clip.Id, rng.Next(2) == 0 ? ClipEdge.Start : ClipEdge.End, Delta(50), rng.Next(2) == 0, rng.Next(2) == 0);
            case 6:
            {
                var track = tracks[rng.Next(tracks.Length)];
                if (track.Clips.Length < 2)
                {
                    return EditResult.Unchanged(p, "Roll");
                }

                var i = rng.Next(track.Clips.Length - 1);
                return TimelineEdits.Roll(p, track.Clips[i].Id, track.Clips[i + 1].Id, Delta(30), rng.Next(2) == 0);
            }

            case 7:
                return clip is null ? EditResult.Unchanged(p, "Slip") : TimelineEdits.Slip(p, clip.Id, Delta(80), rng.Next(2) == 0);
            case 8:
            case 9:
                return rng.Next(3) == 0 && clip is not null
                    ? TimelineEdits.SplitLinked(ids, p, clip.Id, clip.Start + rng.Next(0, (int)Math.Min(clip.Duration + 1, 1000)))
                    : TimelineEdits.Split(ids, p, [TrackId(), TrackId()], Frame());
            case 10:
                return TimelineEdits.Lift(p, someClips);
            case 11:
                return TimelineEdits.RippleDelete(p, TimelineQuery.WithLinkedPartners(p, someClips));
            case 12:
            {
                var track = tracks[rng.Next(tracks.Length)];
                var gaps = TimelineQuery.Gaps(track);
                return gaps.IsEmpty ? TimelineEdits.CloseGap(p, track.Id, Frame()) : TimelineEdits.CloseGap(p, track.Id, gaps[rng.Next(gaps.Length)].Start);
            }

            case 13:
            case 14:
            {
                var track = tracks[rng.Next(tracks.Length)];
                var cuts = track.Clips.Zip(track.Clips.Skip(1)).Where(pair => pair.First.End == pair.Second.Start).Select(pair => pair.First.End).ToList();
                var cut = cuts.Count > 0 ? cuts[rng.Next(cuts.Count)] : Frame();
                var kind = (TransitionKind)rng.Next(9);
                return TimelineEdits.AddTransition(ids, p, track.Id, cut, kind, rng.Next(0, 90));
            }

            case 15:
            {
                var transitions = tracks.SelectMany(t => t.Transitions).ToList();
                if (transitions.Count == 0)
                {
                    return EditResult.Unchanged(p, "Transition");
                }

                var t = transitions[rng.Next(transitions.Count)];
                return rng.Next(3) switch
                {
                    0 => TimelineEdits.SetTransitionDuration(p, t.Id, rng.Next(-5, 120)),
                    1 => TimelineEdits.SetTransitionKind(p, [t.Id], (TransitionKind)rng.Next(9)),
                    _ => TimelineEdits.RemoveTransitions(p, [t.Id]),
                };
            }

            case 16:
                return TimelineEdits.SetSpeed(p, someClips, rng.Next(10, 450), rng.Next(2) == 0);
            case 17:
                return TimelineEdits.UpdateClips(p, someClips, c => RandomProps(c, rng), "Props");
            case 18:
                return TimelineEdits.SetEnabled(p, someClips, rng.Next(2) == 0);
            case 19:
                return rng.Next(2) == 0 ? TimelineEdits.Link(ids, p, someClips) : TimelineEdits.Unlink(p, someClips);
            case 20:
            {
                var markers = p.Sequence.Markers;
                return rng.Next(3) switch
                {
                    0 => TimelineEdits.AddMarker(ids, p, Frame(), "m"),
                    1 when markers.Length > 0 => TimelineEdits.MoveMarker(p, markers[rng.Next(markers.Length)].Id, Frame() - 20),
                    _ when markers.Length > 0 => TimelineEdits.RemoveMarkers(p, [markers[rng.Next(markers.Length)].Id]),
                    _ => EditResult.Unchanged(p, "Marker"),
                };
            }

            case 21:
                return rng.Next(4) == 0 ? TimelineEdits.SetInOut(p, null, null) : TimelineEdits.SetInOut(p, Frame() - 10, Frame());
            case 22:
            {
                // Locks stay rare so most edits still have something to work on.
                var track = tracks[rng.Next(tracks.Length)];
                return TimelineEdits.SetTrack(
                    p,
                    track.Id,
                    t => t with
                    {
                        Locked = !t.Locked && rng.Next(3) == 0,
                        Muted = rng.Next(2) == 0,
                        Solo = rng.Next(5) == 0,
                        Hidden = rng.Next(5) == 0,
                        Targeted = rng.Next(2) == 0,
                        GainDb = rng.Next(-80, 30),
                    },
                    "Track");
            }

            case 23:
                return rng.Next(2) == 0
                    ? TimelineEdits.AddTrack(ids, p, rng.Next(2) == 0 ? TrackKind.Video : TrackKind.Audio, rng.Next(-1, 4))
                    : TimelineEdits.RemoveTrack(p, TrackId());
            case 24:
                return TimelineEdits.RippleTrimToFrame(p, [TrackId(), TrackId()], Frame(), rng.Next(2) == 0 ? ClipEdge.Start : ClipEdge.End);
            case 25:
                return TimelineEdits.Nudge(ids, p, someClips, Delta(3));
            case 26:
                if (rng.Next(8) == 0)
                {
                    var rate = FrameRate.Supported[rng.Next(FrameRate.Supported.Length)];
                    return TimelineEdits.SetSequenceSettings(p, new SequenceSettings(rng.Next(100, 4000), rng.Next(100, 3000), rate));
                }

                return clip is null ? EditResult.Unchanged(p, "Sync") : TimelineEdits.MoveIntoSync(ids, p, clip.Id);
            case 27:
                return rng.Next(2) == 0
                    ? TimelineEdits.SourceEdit(ids, p, MediaIds[rng.Next(MediaIds.Length)], rng.Next(2) == 0 ? null : rng.NextInt64(0, 200) * F, rng.Next(2) == 0 ? null : rng.NextInt64(0, 400) * F, Frame(), rng.Next(2) == 0 ? EditMode.Insert : EditMode.Overwrite, 125)
                    : TimelineEdits.ImportMedia(p, [Asset(Short, "D:/Footage/short.mov", rng.Next(1, 8) * 1_000_000, video: true, audio: rng.Next(4) != 0)]);
            default:
                return clipboard.Paste(ids, p, Frame(), rng.Next(3) == 0 ? EditMode.Insert : EditMode.Overwrite);
        }
    }

    private static Clip RandomProps(Clip clip, Random rng) => clip switch
    {
        MediaClip m => m with
        {
            Video = m.Video with
            {
                Opacity = rng.NextDouble() * 1.5 - 0.2,
                FadeInFrames = rng.Next(-5, 60),
                FadeOutFrames = rng.Next(-5, 60),
                Transform = m.Video.Transform with { Scale = rng.NextDouble() * 3, CropLeft = rng.NextDouble(), CropRight = rng.NextDouble() },
                Color = new ColorAdjust(rng.NextDouble() * 4 - 2, rng.NextDouble() * 3, rng.NextDouble() * 4, rng.NextDouble() * 5),
            },
            Audio = m.Audio with
            {
                GainDb = rng.Next(-90, 30),
                FadeInFrames = rng.Next(-5, 60),
                FadeOutFrames = rng.Next(-5, 60),
                Keys = [.. Enumerable.Range(0, rng.Next(0, 5)).Select(_ => new VolumeKey(rng.Next(-10, (int)Math.Min(m.Duration + 10, 1000)), rng.Next(-80, 20)))],
            },
        },
        TitleClip t => t with { Opacity = rng.NextDouble() * 2 - 0.5, FadeInFrames = rng.Next(0, 50), FadeOutFrames = rng.Next(0, 50), Title = t.Title with { Size = rng.Next(-10, 300) } },
        _ => clip,
    };

    private static Clip? RandomClip(Project p, Random rng)
    {
        var all = p.Sequence.Tracks.SelectMany(t => t.Clips).ToList();
        return all.Count == 0 ? null : all[rng.Next(all.Count)];
    }

    private static List<string> RandomClips(Project p, Random rng, int max)
    {
        var all = p.Sequence.Tracks.SelectMany(t => t.Clips).ToList();
        if (all.Count == 0)
        {
            return [];
        }

        return [.. Enumerable.Range(0, rng.Next(1, max + 1)).Select(_ => all[rng.Next(all.Count)].Id).Distinct()];
    }
}
