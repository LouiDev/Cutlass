using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The Source monitor (DESIGN 4.3): one media item opened from the bin (or by match frame) with its own playhead,
/// In/Out, timecode and transport. The media is held as a private one-clip sequence (video on V1, audio on A1, at
/// the media's own size and nearest supported rate), so frames, playback, the In/Out rules and the transport reuse the
/// timeline machinery unchanged: the frame server renders the one-clip snapshot and a second
/// <see cref="PlaybackController"/> plays it on the shared playback session. Source frames count from the media's
/// <c>format.start_time</c>, so Source frame <c>f</c> is <c>f * FlicksPerFrame</c> flicks of source time.
/// </summary>
public sealed partial class SourceViewerViewModel : ObservableObject
{
    private const string NoTimecode = "--:--:--:--";

    // Gap fill: the Source monitor remembers In, Out and playhead per media item for the session, like a bin clip.
    private readonly Dictionary<string, (long? In, long? Out, long Playhead)> _marks = new(StringComparer.Ordinal);
    private bool _restoring;

    /// <summary>Creates an empty Source monitor.</summary>
    /// <param name="post">Runs an action on the UI thread (playback events).</param>
    public SourceViewerViewModel(Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        Session = new ProjectSession(TimelineEdits.NewProject(RandomIdGenerator.Shared), () => Environment.TickCount64);
        Timeline = new TimelineViewModel(Session, RandomIdGenerator.Shared);
        Playback = new PlaybackController(Timeline, post);
        TimecodeText = Timecode.Format(0, Rate);
        DurationText = Timecode.Format(0, Rate);
        InText = NoTimecode;
        OutText = NoTimecode;
        RangeText = string.Empty;
        Name = string.Empty;
        Timeline.PropertyChanged += OnTimelinePropertyChanged;
        Session.Changed += (_, _) => UpdateState();
    }

    /// <summary>The private session holding the one-clip sequence (its undo history is never exposed).</summary>
    public ProjectSession Session { get; }

    /// <summary>The one-clip sequence: playhead, In and Out of the Source monitor.</summary>
    public TimelineViewModel Timeline { get; }

    /// <summary>The Source transport (Space, Shift+Space, J/K/L while the Source monitor shows).</summary>
    public PlaybackController Playback { get; }

    /// <summary>The media shown, or null when nothing was opened.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedia), nameof(HasVideo), nameof(HasAudio))]
    public partial MediaAsset? Asset { get; private set; }

    /// <summary>True when the media shown has a video stream (the "Video only" drag handle).</summary>
    public bool HasVideo => Asset?.Info.Video is not null;

    /// <summary>True when the media shown has an audio stream (the "Audio only" drag handle).</summary>
    public bool HasAudio => Asset?.Info.Audio is not null;

    /// <summary>The file name of the media shown.</summary>
    [ObservableProperty]
    public partial string Name { get; private set; }

    /// <summary>The Source playhead timecode.</summary>
    [ObservableProperty]
    public partial string TimecodeText { get; private set; }

    /// <summary>The media duration timecode.</summary>
    [ObservableProperty]
    public partial string DurationText { get; private set; }

    /// <summary>The Source In timecode, or dashes.</summary>
    [ObservableProperty]
    public partial string InText { get; private set; }

    /// <summary>The Source Out timecode (the last frame of the range), or dashes.</summary>
    [ObservableProperty]
    public partial string OutText { get; private set; }

    /// <summary>The length of the range that <c>,</c> and <c>.</c> place, for example <c>Dur 00:00:04:12</c>.</summary>
    [ObservableProperty]
    public partial string RangeText { get; private set; }

    /// <summary>The playhead as a fraction of the media length (the mini ruler).</summary>
    [ObservableProperty]
    public partial double PlayheadFraction { get; private set; }

    /// <summary>The start of the range as a fraction of the media length (0 without an In).</summary>
    [ObservableProperty]
    public partial double InFraction { get; private set; }

    /// <summary>The end of the range as a fraction of the media length (1 without an Out).</summary>
    [ObservableProperty]
    public partial double OutFraction { get; private set; } = 1;

    /// <summary>True when an In point is set.</summary>
    [ObservableProperty]
    public partial bool HasIn { get; private set; }

    /// <summary>True when an Out point is set.</summary>
    [ObservableProperty]
    public partial bool HasOut { get; private set; }

    /// <summary>True when media is open.</summary>
    public bool HasMedia => Asset is not null;

    /// <summary>The one-clip project the frame server and playback render.</summary>
    public Project Project => Session.Current;

    /// <summary>The Source frame rate (the media's nearest supported rate).</summary>
    public FrameRate Rate => Session.Current.Sequence.Settings.Rate;

    /// <summary>The media length in Source frames.</summary>
    public long Duration => Timeline.SequenceEnd;

    /// <summary>The Source playhead, always on a frame of the media.</summary>
    public long Playhead => ClampFrame(Timeline.Playhead);

    /// <summary>The Source In frame, or null.</summary>
    public long? InPoint => Timeline.InPoint;

    /// <summary>The Source Out frame (exclusive), or null.</summary>
    public long? OutPoint => Timeline.OutPoint;

    /// <summary>
    /// The one-clip sequence of a media item: its video on V1 and its audio on A1 (linked), starting at frame 0 and
    /// as long as the media. The canvas is the media's display size and the rate its nearest supported rate; audio-only
    /// media and stills use <paramref name="fallback"/> for what they lack. Stills are <paramref name="stillFrames"/>
    /// long.
    /// </summary>
    /// <param name="asset">The media.</param>
    /// <param name="fallback">The settings to use where the media has no video (normally the sequence's).</param>
    /// <param name="stillFrames">The length of a still in frames.</param>
    /// <param name="inFrame">The Source In to keep, or null.</param>
    /// <param name="outFrame">The Source Out to keep, or null.</param>
    /// <returns>The project.</returns>
    public static Project BuildProject(MediaAsset asset, SequenceSettings fallback, long stillFrames, long? inFrame = null, long? outFrame = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(fallback);
        var info = asset.Info;
        var settings = fallback;
        if (info.Video is { } video)
        {
            var width = Even(video.DisplayWidth > 0 ? video.DisplayWidth : video.Width, fallback.Width);
            var height = Even(video.DisplayHeight > 0 ? video.DisplayHeight : video.Height, fallback.Height);
            var rate = fallback.Rate;
            if (!info.IsStill)
            {
                var measured = video.AvgFrameRate.IsPositive ? video.AvgFrameRate : video.RFrameRate;
                rate = measured.IsPositive ? FrameRate.Nearest(measured) : rate;
            }

            settings = new SequenceSettings(width, height, rate);
        }

        var empty = TimelineEdits.NewProject(RandomIdGenerator.Shared, settings);
        var probe = new MediaClip("c_src_v", 0, 1, true, null, asset.Id, StreamKind.Video, 0, 100, VideoProps.Default, AudioProps.Default);
        var max = TimelineQuery.MaxFramesFrom(probe, asset, settings.Rate, 100);
        var duration = max >= TimelineQuery.Unbounded ? Math.Max(1, stillFrames) : Math.Max(1, max);
        var link = info.Video is not null && info.Audio is not null ? "l_src" : null;
        var tracks = empty.Sequence.Tracks.Select(track =>
        {
            Clip? clip = track.Name switch
            {
                "V1" when info.Video is not null => probe with { Duration = duration, LinkId = link },
                "A1" when info.Audio is not null => probe with { Id = "c_src_a", Duration = duration, LinkId = link, Stream = StreamKind.Audio },
                _ => null,
            };
            return clip is null ? track : track with { Clips = [clip] };
        }).ToImmutableArray();
        var (i, o) = ClampRange(inFrame, outFrame, duration);
        var sequence = empty.Sequence with { Tracks = tracks, InFrame = i, OutFrame = o, HasHadClips = true };
        return new Project(Project.CurrentSchemaVersion, [asset], sequence);
    }

    /// <summary>
    /// The arguments of a three-point edit from Source In/Out (DESIGN 5.5): the In in flicks (0 without an In) and the
    /// Out (exclusive) in flicks, or null without an Out, so the edit takes the rest of the media.
    /// </summary>
    /// <param name="inFrame">The Source In frame, or null.</param>
    /// <param name="outFrame">The Source Out frame (exclusive), or null.</param>
    /// <param name="rate">The Source rate.</param>
    /// <returns>The source In and Out in flicks.</returns>
    public static (long InFlicks, long? OutFlicks) EditArgumentsFor(long? inFrame, long? outFrame, FrameRate rate)
    {
        var start = Math.Max(0, inFrame ?? 0);
        var inFlicks = TimeMath.FlicksAtFrame(start, rate);
        long? outFlicks = outFrame is { } o && o > start ? TimeMath.FlicksAtFrame(o, rate) : null;
        return (inFlicks, outFlicks);
    }

    /// <summary>The Source frame that shows a source time (match frame): the frame whose span holds it.</summary>
    /// <param name="flicks">The source time in flicks.</param>
    /// <param name="rate">The Source rate.</param>
    /// <returns>The frame, never negative.</returns>
    public static long FrameAtFlicks(long flicks, FrameRate rate) => Math.Max(0, TimeMath.FloorDiv(flicks, rate.FlicksPerFrame));

    /// <summary>The arguments of a three-point edit from the current Source In/Out.</summary>
    /// <returns>The source In and Out in flicks.</returns>
    public (long InFlicks, long? OutFlicks) EditArguments() => EditArgumentsFor(InPoint, OutPoint, Rate);

    /// <summary>
    /// Opens media (DESIGN 4.2 double-click, Open in Source, match frame). Reopening media restores its In, Out and
    /// playhead unless <paramref name="parkFlicks"/> asks for a source time.
    /// </summary>
    /// <param name="asset">The media.</param>
    /// <param name="fallback">The sequence settings (for media without video).</param>
    /// <param name="stillFrames">The length of a still in frames.</param>
    /// <param name="parkFlicks">The source time to park on, or null.</param>
    public void Open(MediaAsset asset, SequenceSettings fallback, long stillFrames, long? parkFlicks = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Playback.Stop(park: false, reason: "other media was opened in Source");
        Remember();
        // Unknown media starts with no In/Out at frame 0 (the default of the tuple).
        _marks.TryGetValue(asset.Id, out var marks);
        var project = BuildProject(asset, fallback, stillFrames, marks.In, marks.Out);
        var frame = parkFlicks is { } flicks ? FrameAtFlicks(flicks, project.Sequence.Settings.Rate) : marks.Playhead;
        Load(project, asset, frame);
    }

    /// <summary>
    /// Keeps the Source monitor in step with the project: closes it when its media was removed and rebuilds the
    /// one-clip sequence (keeping In, Out and playhead) when the media record changed (re-probe, relink).
    /// </summary>
    /// <param name="project">The editing project.</param>
    /// <param name="fallback">The sequence settings.</param>
    /// <param name="stillFrames">The length of a still in frames.</param>
    public void Sync(Project project, SequenceSettings fallback, long stillFrames)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (Asset is not { } asset)
        {
            return;
        }

        var current = TimelineQuery.FindMedia(project, asset.Id);
        if (current is null)
        {
            Close();
        }
        else if (current != asset)
        {
            Playback.Stop(park: false, reason: "the Source media changed");
            Load(BuildProject(current, fallback, stillFrames, InPoint, OutPoint), current, Playhead);
        }
    }

    /// <summary>Empties the Source monitor (its media left the project).</summary>
    public void Close()
    {
        Playback.Stop(park: false, reason: "the Source media was closed");
        if (Asset is not null)
        {
            Remember();
            _restoring = true;
            try
            {
                Session.Replace(TimelineEdits.NewProject(RandomIdGenerator.Shared));
                Asset = null;
                Name = string.Empty;
            }
            finally
            {
                _restoring = false;
            }
        }

        UpdateState();
    }

    /// <summary>Empties the Source monitor and forgets every remembered In/Out (another project was opened).</summary>
    public void Reset()
    {
        Close();
        _marks.Clear();
    }

    /// <summary>Moves the playhead to a frame of the media (clamped).</summary>
    /// <param name="frame">The Source frame.</param>
    public void Seek(long frame)
    {
        if (HasMedia)
        {
            Timeline.Playhead = ClampFrame(frame);
        }
    }

    /// <summary>Moves the playhead by a number of frames (Left, Right, Shift+Left, Shift+Right).</summary>
    /// <param name="delta">The offset in frames.</param>
    public void Step(long delta) => Seek(Playhead + delta);

    /// <summary>The frame under a fraction of the mini ruler.</summary>
    /// <param name="fraction">0 at the left end, 1 at the right end.</param>
    /// <returns>The Source frame.</returns>
    public long FrameAtFraction(double fraction)
    {
        var duration = Duration;
        if (duration <= 0 || !double.IsFinite(fraction))
        {
            return 0;
        }

        return ClampFrame((long)Math.Floor(Math.Clamp(fraction, 0, 1) * duration));
    }

    /// <summary>Scrubs to a fraction of the mini ruler (click or drag).</summary>
    /// <param name="fraction">0 at the left end, 1 at the right end.</param>
    public void ScrubTo(double fraction) => Seek(FrameAtFraction(fraction));

    /// <summary>I: marks In at the playhead (an In at or after the Out clears the Out, DESIGN 5.5).</summary>
    public void MarkIn()
    {
        if (HasMedia)
        {
            Timeline.SetInCommand.Execute(null);
        }
    }

    /// <summary>O: marks Out so the frame under the playhead is the last frame of the range.</summary>
    public void MarkOut()
    {
        if (HasMedia)
        {
            Timeline.SetOutCommand.Execute(null);
        }
    }

    /// <summary>Sets In at a frame (the In handle of the mini ruler, the smoke runner); an Out at or before it is cleared.</summary>
    /// <param name="frame">The Source frame.</param>
    public void SetIn(long frame)
    {
        if (!HasMedia)
        {
            return;
        }

        var i = ClampFrame(frame);
        var o = OutPoint is { } outFrame && outFrame > i ? outFrame : (long?)null;
        Session.Execute(p => TimelineEdits.SetInOut(p, i, o));
    }

    /// <summary>Sets Out (exclusive) at a frame (the Out handle of the mini ruler); an In at or after it is cleared.</summary>
    /// <param name="frame">The exclusive Out frame.</param>
    public void SetOut(long frame)
    {
        if (!HasMedia)
        {
            return;
        }

        var o = Math.Clamp(frame, 1, Math.Max(1, Duration));
        var i = InPoint is { } inFrame && inFrame < o ? inFrame : (long?)null;
        Session.Execute(p => TimelineEdits.SetInOut(p, i, o));
    }

    /// <summary>X: In and Out around the whole media.</summary>
    public void MarkAll()
    {
        if (HasMedia)
        {
            var duration = Duration;
            Session.Execute(p => TimelineEdits.SetInOut(p, 0, duration));
        }
    }

    /// <summary>Ctrl+Shift+X: clears In and Out.</summary>
    public void ClearInOut()
    {
        if (HasMedia)
        {
            Timeline.ClearInOutCommand.Execute(null);
        }
    }

    /// <summary>Shift+I: goes to In (or the first frame).</summary>
    public void GoToIn() => Seek(InPoint ?? 0);

    /// <summary>Shift+O: goes to the last frame of the range (or of the media).</summary>
    public void GoToOut() => Seek((OutPoint ?? Duration) - 1);

    /// <summary>Home: the first frame.</summary>
    public void GoToStart() => Seek(0);

    /// <summary>End: the last frame.</summary>
    public void GoToEnd() => Seek(Duration - 1);

    /// <summary>A one-line description for logs, for example <c>clipA.mp4 playhead 12 In 30 Out 90 of 360 at 30 fps</c>.</summary>
    /// <returns>The description.</returns>
    public string Describe()
    {
        var i = InPoint?.ToString(CultureInfo.InvariantCulture) ?? "-";
        var o = OutPoint?.ToString(CultureInfo.InvariantCulture) ?? "-";
        return string.Create(CultureInfo.InvariantCulture, $"{(HasMedia ? Name : "(empty)")} playhead {Playhead} In {i} Out {o} of {Duration} at {Rate.DisplayName} fps");
    }

    private static int Even(int value, int fallback)
    {
        var v = value > 1 ? value : fallback;
        return Math.Max(2, v & ~1);
    }

    private static (long? In, long? Out) ClampRange(long? inFrame, long? outFrame, long duration)
    {
        long? i = inFrame is { } a && a >= 0 && a < duration ? a : null;
        long? o = outFrame is { } b && b > 0 && b <= duration ? b : null;
        if (i is { } x && o is { } y && y <= x)
        {
            o = null;
        }

        return (i, o);
    }

    private static string FileName(string path) => path.Replace('\\', '/').Split('/')[^1];

    private void Load(Project project, MediaAsset asset, long frame)
    {
        _restoring = true;
        try
        {
            Session.Replace(project);
            Asset = asset;
            Name = FileName(asset.Path);
            Timeline.Playhead = ClampFrame(frame);
        }
        finally
        {
            _restoring = false;
        }

        UpdateState();
    }

    private long ClampFrame(long frame) => Math.Clamp(frame, 0, Math.Max(0, Duration - 1));

    private void Remember()
    {
        if (Asset is { } asset)
        {
            _marks[asset.Id] = (InPoint, OutPoint, Playhead);
        }
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TimelineViewModel.Playhead) or nameof(TimelineViewModel.InPoint) or nameof(TimelineViewModel.OutPoint))
        {
            UpdateState();
        }
    }

    private void UpdateState()
    {
        if (_restoring)
        {
            return;
        }

        var rate = Rate;
        var duration = HasMedia ? Duration : 0;
        var playhead = Playhead;
        TimecodeText = Timecode.Format(playhead, rate);
        DurationText = Timecode.Format(duration, rate);
        HasIn = InPoint is not null;
        HasOut = OutPoint is not null;
        InText = InPoint is { } i ? Timecode.Format(i, rate) : NoTimecode;
        OutText = OutPoint is { } o ? Timecode.Format(o - 1, rate) : NoTimecode;
        var lo = InPoint ?? 0;
        var hi = OutPoint ?? duration;
        RangeText = HasMedia ? "Dur " + Timecode.Format(Math.Max(0, hi - lo), rate) : string.Empty;
        var length = (double)Math.Max(1, duration);
        PlayheadFraction = duration > 0 ? (playhead + 0.5) / length : 0;
        InFraction = lo / length;
        OutFraction = duration > 0 ? Math.Clamp(hi / length, 0, 1) : 1;
        OnPropertyChanged(nameof(Playhead));
        OnPropertyChanged(nameof(InPoint));
        OnPropertyChanged(nameof(OutPoint));
        OnPropertyChanged(nameof(Duration));
        Remember();
    }
}
