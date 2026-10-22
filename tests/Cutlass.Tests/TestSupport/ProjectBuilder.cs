using System.Collections.Immutable;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Tests.TestSupport;

/// <summary>
/// Builds test projects with tracks V1, V2, A1 and A2 (ids <see cref="V1"/> ... <see cref="A2"/>), the
/// <see cref="TestMedia"/> assets and 1920x1080 at 25 fps. V1 and A1 are targeted.
/// </summary>
internal sealed class ProjectBuilder
{
    public const string V1 = "tr_v1";
    public const string V2 = "tr_v2";
    public const string A1 = "tr_a1";
    public const string A2 = "tr_a2";

    private readonly List<(string Id, TrackKind Kind, string Name)> _tracks =
    [
        (V1, TrackKind.Video, "V1"),
        (V2, TrackKind.Video, "V2"),
        (A1, TrackKind.Audio, "A1"),
        (A2, TrackKind.Audio, "A2"),
    ];

    private readonly Dictionary<string, List<Clip>> _clips = [];
    private readonly Dictionary<string, List<Transition>> _transitions = [];
    private readonly HashSet<string> _locked = [];
    private readonly HashSet<string> _targeted = [V1, A1];
    private readonly List<Marker> _markers = [];
    private long? _in;
    private long? _out;
    private bool? _hasHadClips;
    private FrameRate _rate = TestMedia.Rate;

    public ProjectBuilder With(string trackId, params Clip[] clips)
    {
        if (!_clips.TryGetValue(trackId, out var list))
        {
            list = [];
            _clips[trackId] = list;
        }

        list.AddRange(clips);
        return this;
    }

    public ProjectBuilder Transition(string trackId, string id, string left, string right, long duration, TransitionKind? kind = null)
    {
        if (!_transitions.TryGetValue(trackId, out var list))
        {
            list = [];
            _transitions[trackId] = list;
        }

        var k = kind ?? (trackId is A1 or A2 ? TransitionKind.AudioCrossfade : TransitionKind.Crossfade);
        list.Add(new Transition(id, k, left, right, duration));
        return this;
    }

    public ProjectBuilder Lock(string trackId)
    {
        _locked.Add(trackId);
        return this;
    }

    public ProjectBuilder Target(params string[] trackIds)
    {
        _targeted.Clear();
        _targeted.UnionWith(trackIds);
        return this;
    }

    public ProjectBuilder Marker(string id, long frame, string name = "")
    {
        _markers.Add(new Marker(id, frame, name, TimelineEdits.DefaultMarkerColor));
        return this;
    }

    public ProjectBuilder InOut(long? inFrame, long? outFrame)
    {
        _in = inFrame;
        _out = outFrame;
        return this;
    }

    public ProjectBuilder Fresh()
    {
        _hasHadClips = false;
        return this;
    }

    public ProjectBuilder Rate(FrameRate rate)
    {
        _rate = rate;
        return this;
    }

    public Project Build()
    {
        var tracks = _tracks.Select(t => new Track(
            t.Id,
            t.Kind,
            t.Name,
            Hidden: false,
            Locked: _locked.Contains(t.Id),
            Targeted: _targeted.Contains(t.Id),
            Muted: false,
            Solo: false,
            GainDb: 0,
            [.. _clips.GetValueOrDefault(t.Id, []).OrderBy(c => c.Start)],
            [.. _transitions.GetValueOrDefault(t.Id, [])])).ToImmutableArray();
        var hasClips = tracks.Any(t => t.Clips.Length > 0);
        var sequence = new Sequence(
            new SequenceSettings(1920, 1080, _rate),
            tracks,
            [.. _markers.OrderBy(m => m.Frame)],
            _in,
            _out,
            _hasHadClips ?? hasClips);
        return new Project(Project.CurrentSchemaVersion, TestMedia.All, sequence);
    }
}
