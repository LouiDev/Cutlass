using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Core.Time;
using Cutlass.Media.Jobs;
using Cutlass.Media.Preview;
using Cutlass.Media.Settings;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The viewer (DESIGN 4.3, TECH_BRIEF 10.3): mode, quality, timecode readout (click to type), guides and proxies, and
/// the frame requests. In Timeline mode it shows the sequence at the timeline playhead; in Source mode it shows the
/// media opened in <see cref="Source"/> at the Source playhead, with its own transport, In/Out and the three-point
/// edit buttons. It asks the frame server for the frame of the monitor shown whenever its playhead, its project, the
/// quality or the viewport changes; the server is latest-wins, so no debounce is needed.
/// </summary>
public sealed partial class ViewerViewModel : ObservableObject
{
    /// <summary>Auto quality never asks for more than this while paused (DESIGN 6).</summary>
    public const int AutoMaxWidth = 1920;

    /// <summary>Auto quality never asks for more than this while paused (DESIGN 6).</summary>
    public const int AutoMaxHeight = 1080;

    /// <summary>Auto quality never asks for more than this while playing (DESIGN 6).</summary>
    public const int AutoPlayingMaxWidth = 960;

    /// <summary>Auto quality never asks for more than this while playing (DESIGN 6).</summary>
    public const int AutoPlayingMaxHeight = 540;

    private static readonly TimeSpan StatsInterval = TimeSpan.FromMilliseconds(250);

    // Unspecified how soon the placeholder shows; 120 ms keeps cache hits and fast scrubs free of flashes.
    private static readonly TimeSpan PlaceholderDelay = TimeSpan.FromMilliseconds(120);

    private readonly Action<Action> _post;
    private readonly MeterBallistics _meterLeft = new();
    private readonly MeterBallistics _meterRight = new();
    private readonly Stopwatch _meterClock = new();
    private TimeSpan _lastMeterTick;
    private TimeSpan _lastStats;
    private JobScheduler? _scheduler;
    private FrameServer? _server;
    private long _nextRequestId;
    private long _latestRequestId = -1;
    private FrameRequest? _pendingSecond;
    private double _viewportWidth = 960;
    private double _viewportHeight = 540;

    /// <summary>Creates the viewer.</summary>
    /// <param name="timeline">The timeline (playhead and session).</param>
    /// <param name="presenter">Shows the frames.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    public ViewerViewModel(TimelineViewModel timeline, FramePresenter presenter, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(post);
        Timeline = timeline;
        Presenter = presenter;
        _post = post;
        Playback = new PlaybackController(timeline, post)
        {
            SizeProvider = () => SizeFor(Quality, timeline.Session.Current.Sequence.Settings, _viewportWidth, _viewportHeight, playing: true),
            UseProxies = () => UseProxies,
            Loop = () => IsLooping,
        };
        Source = new SourceViewerViewModel(post);
        Source.Playback.SizeProvider = () => SizeFor(Quality, Source.Project.Sequence.Settings, _viewportWidth, _viewportHeight, playing: true);
        Source.Playback.UseProxies = () => UseProxies;
        Source.Playback.Loop = () => IsLooping;
        foreach (var controller in new[] { Playback, Source.Playback })
        {
            controller.StateChanged += OnPlaybackStateChanged;
            controller.Failed += (_, message) => StatusText = "Playback failed: " + message;
            // Both monitors share one engine, so the latest report of either controller is the device state (the
            // other controller's flag may be stale until its next start).
            controller.AudioStateChanged += (sender, _) => IsAudioUnavailable = ((PlaybackController)sender!).IsAudioUnavailable;
        }

        Mode = ViewerMode.Timeline;
        Quality = PreviewQuality.Auto;
        UseProxies = true;
        TimecodeText = string.Empty;
        DurationText = string.Empty;
        TimecodeEditText = string.Empty;
        PlaceholderText = "Import media and place it on the timeline to preview it here.";
        SpeedText = "1x";
        DroppedText = "0 dropped";
        timeline.PropertyChanged += OnTimelinePropertyChanged;
        timeline.Session.Changed += OnSessionChanged;

        // DESIGN 5.6: starting a timeline drag pauses playback (either monitor; they share one playback session).
        timeline.DragStarted += (_, _) =>
        {
            if (Playback.IsPlaying || Source.Playback.IsPlaying)
            {
                StopPlayback(reason: "a timeline drag started");
            }
        };
        Source.Timeline.PropertyChanged += OnSourcePropertyChanged;
        Source.Session.Changed += OnSourceSessionChanged;
        Source.PropertyChanged += OnSourceChanged;
        presenter.FramePresented += OnFramePresented;

        UpdateReadout();
    }

    /// <summary>The timeline transport (Space, Shift+Space, J/K/L) and the playback clock.</summary>
    public PlaybackController Playback { get; }

    /// <summary>The Source monitor (DESIGN 4.3 Source mode).</summary>
    public SourceViewerViewModel Source { get; }

    /// <summary>The transport of the monitor shown: Source while Source mode shows media, else the timeline.</summary>
    public PlaybackController ActivePlayback => IsSourceActive ? Source.Playback : Playback;

    /// <summary>True while the Source monitor shows media, so the transport and mark keys go to it (DESIGN 4.3).</summary>
    public bool IsSourceActive => Mode == ViewerMode.Source && Source.HasMedia;

    /// <summary>True when media is open in Source (the Source button is enabled).</summary>
    public bool CanShowSource => Source.HasMedia;

    /// <summary>True while playing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    public partial bool IsPlaying { get; set; }

    /// <summary>The shuttle speed readout, for example <c>2x</c>.</summary>
    [ObservableProperty]
    public partial string SpeedText { get; set; }

    /// <summary>The dropped-frame readout, for example <c>3 dropped</c>.</summary>
    [ObservableProperty]
    public partial string DroppedText { get; set; }

    /// <summary>True after 2 s of continuous lag (the amber "Dropped frames" badge, DESIGN 4.3).</summary>
    [ObservableProperty]
    public partial bool ShowDroppedBadge { get; set; }

    /// <summary>
    /// True while playback runs on a silent clock because the audio device is unavailable (the "Audio unavailable"
    /// badge next to the dropped-frames badge). Re-checked after every play start, since a start may reopen the device.
    /// </summary>
    [ObservableProperty]
    public partial bool IsAudioUnavailable { get; set; }

    /// <summary>The left master level in dBFS (the meter bar).</summary>
    [ObservableProperty]
    public partial double MeterLeftDb { get; set; } = MeterBallistics.FloorDb;

    /// <summary>The right master level in dBFS (the meter bar).</summary>
    [ObservableProperty]
    public partial double MeterRightDb { get; set; } = MeterBallistics.FloorDb;

    /// <summary>The left peak-hold level in dBFS.</summary>
    [ObservableProperty]
    public partial double MeterLeftPeakDb { get; set; } = MeterBallistics.FloorDb;

    /// <summary>The right peak-hold level in dBFS.</summary>
    [ObservableProperty]
    public partial double MeterRightPeakDb { get; set; } = MeterBallistics.FloorDb;

    /// <summary>True while not playing (the play icon shows).</summary>
    public bool IsPaused => !IsPlaying;

    /// <summary>The thumbnail to show dimmed while the exact frame decodes, or null.</summary>
    [ObservableProperty]
    public partial ThumbnailTile? PlaceholderTile { get; set; }

    /// <summary>True while the dimmed placeholder replaces the canvas (DESIGN 4.3).</summary>
    [ObservableProperty]
    public partial bool IsPlaceholderVisible { get; set; }

    /// <summary>Thumbnail sheets for the placeholder, or null while ffmpeg is unavailable.</summary>
    public IMediaArtifactSource? Artifacts { get; set; }

    /// <summary>The timeline, for the transport bindings.</summary>
    public TimelineViewModel Timeline { get; }

    /// <summary>The presenter the view attaches its surface to.</summary>
    public FramePresenter Presenter { get; }

    /// <summary>The quality choices.</summary>
    public IReadOnlyList<PreviewQuality> Qualities { get; } = Enum.GetValues<PreviewQuality>();

    /// <summary>Timeline or Source (DESIGN 4.3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimelineMode))]
    [NotifyPropertyChangedFor(nameof(IsSourceMode))]
    [NotifyPropertyChangedFor(nameof(IsSourceActive))]
    [NotifyPropertyChangedFor(nameof(ActivePlayback))]
    public partial ViewerMode Mode { get; set; }

    /// <summary>The preview quality.</summary>
    [ObservableProperty]
    public partial PreviewQuality Quality { get; set; }

    /// <summary>Safe guides (G).</summary>
    [ObservableProperty]
    public partial bool ShowGuides { get; set; }

    /// <summary>Use proxies (P).</summary>
    [ObservableProperty]
    public partial bool UseProxies { get; set; }

    /// <summary>Loop (Ctrl+Shift+L): playback repeats In to Out, or the whole sequence or media without a range.</summary>
    [ObservableProperty]
    public partial bool IsLooping { get; set; }

    /// <summary>The playhead timecode of the monitor shown.</summary>
    [ObservableProperty]
    public partial string TimecodeText { get; set; }

    /// <summary>The duration timecode of the monitor shown.</summary>
    [ObservableProperty]
    public partial string DurationText { get; set; }

    /// <summary>True while the timecode readout is an editable field (click to type, DESIGN 4.3).</summary>
    [ObservableProperty]
    public partial bool IsEditingTimecode { get; set; }

    /// <summary>The text of the editable timecode field.</summary>
    [ObservableProperty]
    public partial string TimecodeEditText { get; set; }

    /// <summary>True when the sequence has no clips (the placeholder text shows).</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>
    /// True while a roll or slip drag shows a two-up (DESIGN 4.3): the hint's first frame on the left and its second
    /// frame on the right (outgoing and incoming for a roll, new first and last frame for a slip).
    /// </summary>
    [ObservableProperty]
    public partial bool IsTwoUp { get; set; }

    /// <summary>True while the latest requested frame has not arrived.</summary>
    [ObservableProperty]
    public partial bool IsWaiting { get; set; }

    /// <summary>True when there is nothing to show at the playhead (empty sequence or past its end).</summary>
    [ObservableProperty]
    public partial bool IsBlank { get; set; }

    /// <summary>A problem to show over the canvas (render failure, ffmpeg missing), or null.</summary>
    [ObservableProperty]
    public partial string? StatusText { get; set; }

    /// <summary>The text shown on an empty canvas.</summary>
    [ObservableProperty]
    public partial string PlaceholderText { get; set; }

    /// <summary>The width of the canvas shown (letterbox): the sequence, or the Source media.</summary>
    [ObservableProperty]
    public partial int CanvasWidth { get; set; } = 1920;

    /// <summary>The height of the canvas shown (letterbox).</summary>
    [ObservableProperty]
    public partial int CanvasHeight { get; set; } = 1080;

    /// <summary>The id of the newest frame request, or -1 while nothing is requested (blank canvas).</summary>
    public long LatestRequestId => _latestRequestId;

    /// <summary>The frame of the newest request: the drag hint during a trim, else the playhead of the monitor shown.</summary>
    public long RequestedFrame { get; private set; }

    /// <summary>True in Timeline mode.</summary>
    public bool IsTimelineMode => Mode == ViewerMode.Timeline;

    /// <summary>True in Source mode.</summary>
    public bool IsSourceMode => Mode == ViewerMode.Source;

    /// <summary>The preview size for a quality (DESIGN 6): Full, Half and Quarter are relative to the sequence; Auto is
    /// the viewer's physical size capped at 1920x1080 (while paused) and never above the sequence size.</summary>
    /// <param name="quality">The quality.</param>
    /// <param name="settings">The sequence settings.</param>
    /// <param name="viewportWidth">The viewer width in physical pixels.</param>
    /// <param name="viewportHeight">The viewer height in physical pixels.</param>
    /// <param name="playing">True while playing: Auto is capped at 960x540 instead of 1920x1080.</param>
    /// <returns>The even, aspect-preserving size.</returns>
    public static PreviewSize SizeFor(PreviewQuality quality, SequenceSettings settings, double viewportWidth, double viewportHeight, bool playing = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var capW = playing ? AutoPlayingMaxWidth : AutoMaxWidth;
        var capH = playing ? AutoPlayingMaxHeight : AutoMaxHeight;
        var (maxW, maxH) = quality switch
        {
            PreviewQuality.Full => (settings.Width, settings.Height),
            PreviewQuality.Half => (settings.Width / 2, settings.Height / 2),
            PreviewQuality.Quarter => (settings.Width / 4, settings.Height / 4),
            _ => (
                (int)Math.Min(Math.Min(settings.Width, capW), Math.Max(16, viewportWidth)),
                (int)Math.Min(Math.Min(settings.Height, capH), Math.Max(16, viewportHeight))),
        };
        return FrameServer.FitSize(settings, Math.Max(2, maxW), Math.Max(2, maxH));
    }

    /// <summary>Starts using a frame server (null while ffmpeg is missing) and requests the current frame.</summary>
    /// <param name="server">The frame server, or null.</param>
    public void AttachFrameServer(FrameServer? server)
    {
        if (_server is not null)
        {
            _server.RenderFailed -= OnRenderFailed;
        }

        _server = server;
        if (server is not null)
        {
            server.RenderFailed += OnRenderFailed;
        }

        Refresh();
    }

    /// <summary>
    /// Starts using a playback engine (null while ffmpeg is unavailable) for both monitors. The scheduler is told while
    /// playback runs, so background jobs drop to one process (TECH_BRIEF 6.7).
    /// </summary>
    /// <param name="engine">The engine, or null.</param>
    /// <param name="scheduler">The job scheduler, or null.</param>
    public void AttachPlayback(IPlaybackEngine? engine, JobScheduler? scheduler)
    {
        Playback.Attach(engine);
        Source.Playback.Attach(engine);
        _scheduler = scheduler;
        IsAudioUnavailable = Playback.IsAudioUnavailable;
    }

    /// <summary>Stops whatever plays (both monitors share one playback session).</summary>
    /// <param name="park">True to park the playhead on the frame shown last.</param>
    /// <param name="reason">Why playback stopped.</param>
    public void StopPlayback(bool park = true, string reason = "K")
    {
        Playback.Stop(park, reason);
        Source.Playback.Stop(park, reason);
    }

    /// <summary>
    /// Opens media in the Source monitor and shows it (DESIGN 4.2 double-click and Open in Source, DESIGN 5.5 match
    /// frame).
    /// </summary>
    /// <param name="asset">The media.</param>
    /// <param name="parkFlicks">The source time to park on, or null to restore the media's last Source playhead.</param>
    public void OpenSource(MediaAsset asset, long? parkFlicks = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        StopPlayback(reason: "media was opened in Source");
        Source.Open(asset, Timeline.Session.Current.Sequence.Settings, Timeline.StillDurationFrames, parkFlicks);
        if (Mode == ViewerMode.Source)
        {
            UpdateReadout();
            Refresh();
        }
        else
        {
            Mode = ViewerMode.Source;
        }
    }

    /// <summary>Shows the Timeline monitor (a click in the timeline panel, DESIGN 4.3).</summary>
    public void ShowTimeline() => Mode = ViewerMode.Timeline;

    /// <summary>Records the viewer's physical size (for Auto quality) and re-requests when it changed.</summary>
    /// <param name="width">The width in physical pixels.</param>
    /// <param name="height">The height in physical pixels.</param>
    public void SetViewport(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1)
        {
            return;
        }

        var changed = Math.Abs(width - _viewportWidth) >= 1 || Math.Abs(height - _viewportHeight) >= 1;
        _viewportWidth = width;
        _viewportHeight = height;
        if (changed && Quality == PreviewQuality.Auto && !ActivePlayback.IsPlaying)
        {
            Refresh();
        }
    }

    /// <summary>Requests the frame of the monitor shown (or blanks the canvas when there is nothing to show).</summary>
    public void Refresh()
    {
        UpdateReadout();
        var source = IsSourceActive;
        var project = source ? Source.Project : Timeline.Session.Current;
        var end = TimelineQuery.Duration(project);
        IsEmpty = !source && end == 0;
        if (Playback.IsPlaying || Source.Playback.IsPlaying)
        {
            // Playback presents its own frames; the scrub request follows when it stops.
            return;
        }

        // DESIGN 4.3 trim feedback: during a trim the viewer shows the frame at the edited edge, and a roll or slip
        // shows a two-up of the hint's two frames (Timeline mode only).
        var hint = source ? null : Timeline.DragHint;
        var frame = source ? Source.Playhead : hint?.Frame ?? Timeline.Playhead;
        RequestedFrame = frame;
        _pendingSecond = null;
        Presenter.SecondaryRequestId = -1;
        IsTwoUp = hint?.SecondFrame is not null;
        if (end == 0 || frame >= end)
        {
            // Past the last clip the sequence is black.
            _latestRequestId = -1;
            IsBlank = true;
            IsPlaceholderVisible = false;
            IsWaiting = false;
            StatusText = null;
            Presenter.Clear();
            return;
        }

        IsBlank = false;
        if (_server is null)
        {
            IsWaiting = false;
            StatusText = "Preview needs ffmpeg.";
            return;
        }

        var size = SizeFor(Quality, project.Sequence.Settings, _viewportWidth, _viewportHeight);
        var id = ++_nextRequestId;
        _latestRequestId = id;
        IsWaiting = true;
        PlaceholderTile = PlaceholderFor(project, frame);
        _server.Request(new FrameRequest(project, frame, size, UseProxies, id));
        if (hint?.SecondFrame is { } second)
        {
            // The frame server is latest-wins, so the right frame is requested once the left one is on screen.
            _pendingSecond = new FrameRequest(project, Math.Clamp(second, 0, end - 1), size, UseProxies, 0);
        }

        if (PlaceholderTile is not null && !IsPlaceholderVisible)
        {
            _ = ShowPlaceholderLaterAsync(id);
        }
    }

    /// <summary>
    /// The thumbnail of the topmost visible video clip at a frame (TECH_BRIEF 8.1 step 6), or null when there is none
    /// or its sheets do not exist yet.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="frame">The timeline frame.</param>
    /// <returns>The tile, or null.</returns>
    public ThumbnailTile? PlaceholderFor(Project project, long frame)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (Artifacts is not { } artifacts)
        {
            return null;
        }

        var top = TimelineQuery.ClipsAt(project, frame)
            .Where(x => x.Track.Kind == TrackKind.Video && !x.Track.Hidden && x.Clip is MediaClip { Enabled: true, Stream: StreamKind.Video })
            .OrderByDescending(x => TimelineQuery.KindIndex(project, x.Track.Id))
            .Select(x => (MediaClip)x.Clip)
            .FirstOrDefault();
        if (top is null || TimelineQuery.FindMedia(project, top.MediaId) is not { } asset || artifacts.TryGetSheet(asset) is not { } sheet)
        {
            return null;
        }

        var micros = TimeMath.FloorMicrosFromFlicks(TimeMath.SourceFlicksAt(top, frame, project.Sequence.Settings.Rate));
        var (path, x, y, w, h) = sheet.TileAt(micros);
        return new ThumbnailTile(path, x, y, w, h);
    }

    /// <summary>
    /// The payload of a drag out of the Source monitor (DESIGN 4.3): the open media, its Source In and Out in flicks
    /// (as <c>,</c> and <c>.</c> use them) and the streams. The canvas drags both, the handles one stream.
    /// </summary>
    /// <param name="streams">The streams to place.</param>
    /// <returns>The payload, or null when no media is open or the media lacks the stream.</returns>
    public SourceDrag? CreateSourceDrag(SourceStreams streams)
    {
        // No Source drags to the timeline while an agent holds the project (AGENT_INTEGRATION 3.7).
        if (Timeline.IsReadOnly || Source.Asset is not { } asset
            || (streams == SourceStreams.Video && !Source.HasVideo)
            || (streams == SourceStreams.Audio && !Source.HasAudio))
        {
            return null;
        }

        var (inFlicks, outFlicks) = Source.EditArguments();
        return new SourceDrag(asset.Id, inFlicks, outFlicks, streams);
    }

    /// <summary>
    /// <c>,</c> and <c>.</c> from Source (DESIGN 5.5): places the Source In/Out range (or In to the end, or the whole
    /// media) on the topmost targeted tracks at the timeline In or playhead.
    /// </summary>
    /// <param name="insert">True for insert, false for overwrite.</param>
    /// <returns>False when no media is open in Source.</returns>
    public bool EditFromSource(bool insert)
    {
        if (Source.Asset is not { } asset)
        {
            return false;
        }

        if (Timeline.IsReadOnly)
        {
            // An agent holds the project (AGENT_INTEGRATION 3.7): the edit is claimed and dropped.
            return true;
        }

        // A three-point edit changes the sequence the timeline transport plays; Source playback keeps going.
        var (inFlicks, outFlicks) = Source.EditArguments();
        if (insert)
        {
            Timeline.SourceInsert(asset.Id, inFlicks, outFlicks);
        }
        else
        {
            Timeline.SourceOverwrite(asset.Id, inFlicks, outFlicks);
        }

        // Gap fill: the monitor stays in Source mode after an edit, so the next range can be marked and placed at once.
        return true;
    }

    /// <summary>Click on the timecode readout: turns it into an editable field (DESIGN 4.3).</summary>
    public void BeginTimecodeEdit()
    {
        TimecodeEditText = TimecodeText;
        IsEditingTimecode = true;
    }

    /// <summary>Esc (or focus leaving the field): leaves the timecode field without seeking.</summary>
    /// <returns>True when a timecode edit was cancelled.</returns>
    public bool CancelTimecodeEdit()
    {
        if (!IsEditingTimecode)
        {
            return false;
        }

        IsEditingTimecode = false;
        return true;
    }

    /// <summary>
    /// Enter in the timecode field: seeks the monitor shown to an absolute timecode or a <c>+N</c>/<c>-N</c> offset
    /// (frames or a timecode). Invalid text leaves the playhead where it is.
    /// </summary>
    /// <returns>True when the text was valid and the playhead moved there.</returns>
    public bool CommitTimecodeEdit()
    {
        if (!IsEditingTimecode)
        {
            return false;
        }

        IsEditingTimecode = false;
        return SeekToText(TimecodeEditText);
    }

    /// <summary>Seeks the monitor shown to a typed timecode or offset (see <see cref="TimecodeEntry"/>).</summary>
    /// <param name="text">The text.</param>
    /// <returns>True when the text was valid.</returns>
    public bool SeekToText(string text)
    {
        if (IsSourceActive)
        {
            if (!TimecodeEntry.TryResolve(text, Source.Playhead, Source.Rate, out var sourceFrame))
            {
                return false;
            }

            Source.Seek(sourceFrame);
            return true;
        }

        if (!TimecodeEntry.TryResolve(text, Timeline.Playhead, Timeline.Session.Current.Sequence.Settings.Rate, out var frame))
        {
            return false;
        }

        Timeline.Playhead = frame;
        return true;
    }

    // The placeholder appears only when the exact frame takes a moment, so cache hits never flash it.
    private async Task ShowPlaceholderLaterAsync(long requestId)
    {
        await Task.Delay(PlaceholderDelay);
        _post(() =>
        {
            if (requestId == _latestRequestId && IsWaiting && !IsPlaying)
            {
                IsPlaceholderVisible = PlaceholderTile is not null;
            }
        });
    }

    partial void OnQualityChanged(PreviewQuality value) => RefreshOrRestart();

    partial void OnUseProxiesChanged(bool value) => RefreshOrRestart();

    partial void OnModeChanged(ViewerMode value)
    {
        // One monitor plays at a time: switching parks the other one where it was.
        var other = value == ViewerMode.Source ? Playback : Source.Playback;
        other.Stop(reason: "the viewer switched monitors");
        IsEditingTimecode = false;
        Refresh();
    }

    partial void OnIsLoopingChanged(bool value)
    {
        // The loop range is part of a run, so a change while playing restarts at the current frame with the new range.
        var playback = ActivePlayback;
        if (playback.IsPlaying)
        {
            playback.Stop(reason: "loop was toggled");
            playback.TogglePlay();
        }
    }

    private void RefreshOrRestart()
    {
        if (ActivePlayback.IsPlaying)
        {
            ActivePlayback.RestartNow();
        }
        else
        {
            Refresh();
        }
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        var active = sender as PlaybackController ?? ActivePlayback;
        var playing = Playback.IsPlaying || Source.Playback.IsPlaying;
        SpeedText = active.Speed.ToString(CultureInfo.InvariantCulture) + "x";
        if (playing == IsPlaying)
        {
            return;
        }

        IsPlaying = playing;
        if (_scheduler is { } scheduler)
        {
            scheduler.IsPlaybackActive = playing;
        }

        if (_server is { } server)
        {
            // The frame server prefetches only while paused (TECH_BRIEF 8.1, M5).
            server.PrefetchEnabled = !playing;
        }

        if (playing)
        {
            StatusText = null;
            IsWaiting = false;
            IsBlank = false;
            IsPlaceholderVisible = false;
            ShowDroppedBadge = false;
            IsEditingTimecode = false;
            IsTwoUp = false;
            DroppedText = "0 dropped";
            _meterLeft.Reset();
            _meterRight.Reset();
            _meterClock.Restart();
            _lastMeterTick = TimeSpan.Zero;
            _lastStats = TimeSpan.Zero;
            Presenter.StartPlayback(NextPlaybackFrame);
        }
        else
        {
            Presenter.StopPlayback();
            _meterLeft.Reset();
            _meterRight.Reset();
            PublishMeter();
            ShowDroppedBadge = false;
            Refresh();
        }
    }

    private PlaybackController PlayingController() => Source.Playback.IsPlaying ? Source.Playback : Playback;

    // One animation frame while playing: playhead, meter, stats, then the frame due.
    private VideoFrame? NextPlaybackFrame()
    {
        var playback = PlayingController();
        var frame = playback.Tick(out _);
        var now = _meterClock.Elapsed;
        var peaks = playback.ReadPeaks();
        var dt = (now - _lastMeterTick).TotalSeconds;
        _lastMeterTick = now;
        _meterLeft.Update(peaks.MasterLeft, dt);
        _meterRight.Update(peaks.MasterRight, dt);
        PublishMeter();
        if (now - _lastStats >= StatsInterval)
        {
            _lastStats = now;
            var stats = playback.Stats();
            DroppedText = stats.FramesDropped.ToString(CultureInfo.InvariantCulture) + " dropped";
            ShowDroppedBadge = stats.ShowDroppedBadge;
        }

        return frame;
    }

    private void PublishMeter()
    {
        MeterLeftDb = _meterLeft.LevelDb;
        MeterRightDb = _meterRight.LevelDb;
        MeterLeftPeakDb = _meterLeft.PeakDb;
        MeterRightPeakDb = _meterRight.PeakDb;
    }

    private void OnTimelinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.IsReadOnly))
        {
            // AGENT_INTEGRATION 3.7: Source insert and overwrite follow the agent lock.
            InsertFromSourceCommand.NotifyCanExecuteChanged();
            OverwriteFromSourceCommand.NotifyCanExecuteChanged();
            return;
        }

        if (e.PropertyName is nameof(TimelineViewModel.Playhead) or nameof(TimelineViewModel.DragHint))
        {
            if (IsSourceActive)
            {
                // A trim drag shows its edge frame, so a drag in the timeline brings the Timeline monitor back.
                if (e.PropertyName == nameof(TimelineViewModel.DragHint) && Timeline.DragHint is not null)
                {
                    Mode = ViewerMode.Timeline;
                }

                return;
            }

            Refresh();
        }
    }

    private void OnSessionChanged(object? sender, ProjectChangedEventArgs e)
    {
        if (e.Kind == ChangeKind.Saved || (ReferenceEquals(e.Old.Sequence, e.New.Sequence) && e.Old.Media == e.New.Media))
        {
            return;
        }

        if (e.Old.Media != e.New.Media)
        {
            // The Source media left the project or its record changed (re-probe, relink).
            Source.Sync(e.New, e.New.Sequence.Settings, Timeline.StillDurationFrames);
        }

        if (!IsSourceActive)
        {
            Refresh();
        }
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.Playhead) && IsSourceActive)
        {
            Refresh();
        }
    }

    private void OnSourceSessionChanged(object? sender, ProjectChangedEventArgs e)
    {
        if (IsSourceActive && e.Kind != ChangeKind.Saved)
        {
            Refresh();
        }
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SourceViewerViewModel.HasMedia))
        {
            return;
        }

        OnPropertyChanged(nameof(CanShowSource));
        OnPropertyChanged(nameof(IsSourceActive));
        OnPropertyChanged(nameof(ActivePlayback));
        if (!Source.HasMedia && Mode == ViewerMode.Source)
        {
            Mode = ViewerMode.Timeline;
        }
    }

    private void OnFramePresented(long requestId, long timelineFrame)
    {
        if (requestId == _latestRequestId)
        {
            IsWaiting = false;
            IsPlaceholderVisible = false;
            StatusText = null;
            if (_pendingSecond is { } second && _server is { } server && !IsPlaying)
            {
                _pendingSecond = null;
                var id = ++_nextRequestId;
                Presenter.SecondaryRequestId = id;
                server.Request(second with { RequestId = id });
            }
        }
    }

    private void OnRenderFailed(long requestId, string message) => _post(() =>
    {
        if (requestId == _latestRequestId)
        {
            IsWaiting = false;
            StatusText = "Preview failed: " + message;
            IsPlaceholderVisible = false;
        }
    });

    private void UpdateReadout()
    {
        if (IsSourceActive)
        {
            var settings = Source.Project.Sequence.Settings;
            CanvasWidth = settings.Width;
            CanvasHeight = settings.Height;
            TimecodeText = Source.TimecodeText;
            DurationText = Source.DurationText;
            return;
        }

        var project = Timeline.Session.Current;
        var sequence = project.Sequence.Settings;
        CanvasWidth = sequence.Width;
        CanvasHeight = sequence.Height;
        TimecodeText = Timecode.Format(Timeline.Playhead, sequence.Rate);
        DurationText = Timecode.Format(TimelineQuery.Duration(project), sequence.Rate);
    }

    /// <summary>Space: play or pause the monitor shown.</summary>
    [RelayCommand]
    private void PlayPause() => ActivePlayback.TogglePlay();

    /// <summary>Shift+Space: play In to Out of the monitor shown.</summary>
    [RelayCommand]
    private void PlayInToOut() => ActivePlayback.PlayInToOut();

    /// <summary>L: play forward, again for 2x and 4x.</summary>
    [RelayCommand]
    private void ShuttleForward() => ActivePlayback.Shuttle();

    /// <summary>K: pause.</summary>
    [RelayCommand]
    private void Pause() => ActivePlayback.Pause();

    /// <summary>Left (and J until reverse shuttle exists): one frame back on the monitor shown.</summary>
    [RelayCommand]
    private void StepBackward()
    {
        if (IsSourceActive)
        {
            Source.Step(-1);
        }
        else
        {
            Timeline.StepBackwardCommand.Execute(null);
        }
    }

    /// <summary>Right: one frame forward on the monitor shown.</summary>
    [RelayCommand]
    private void StepForward()
    {
        if (IsSourceActive)
        {
            Source.Step(1);
        }
        else
        {
            Timeline.StepForwardCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void ToggleGuides() => ShowGuides = !ShowGuides;

    [RelayCommand]
    private void ToggleProxies() => UseProxies = !UseProxies;

    [RelayCommand]
    private void ToggleLoop() => IsLooping = !IsLooping;

    [RelayCommand]
    private void SetMode(ViewerMode mode)
    {
        if (mode == ViewerMode.Timeline || Source.HasMedia)
        {
            Mode = mode;
        }
    }

    [RelayCommand]
    private void GoToIn()
    {
        if (IsSourceActive)
        {
            Source.GoToIn();
        }
        else
        {
            Timeline.GoToInCommand.Execute(null);
        }
    }

    // Out is exclusive, so Go to Out lands on the last frame of the range (DESIGN 5.5).
    [RelayCommand]
    private void GoToOut()
    {
        if (IsSourceActive)
        {
            Source.GoToOut();
        }
        else
        {
            Timeline.GoToOutCommand.Execute(null);
        }
    }

    /// <summary>The Source monitor's Insert button (<c>,</c>).</summary>
    [RelayCommand(CanExecute = nameof(CanEditFromSource))]
    private void InsertFromSource() => EditFromSource(insert: true);

    /// <summary>The Source monitor's Overwrite button (<c>.</c>).</summary>
    [RelayCommand(CanExecute = nameof(CanEditFromSource))]
    private void OverwriteFromSource() => EditFromSource(insert: false);

    private bool CanEditFromSource() => !Timeline.IsReadOnly;

    /// <summary>The Source monitor's Mark In button (I).</summary>
    [RelayCommand]
    private void SourceMarkIn() => Source.MarkIn();

    /// <summary>The Source monitor's Mark Out button (O).</summary>
    [RelayCommand]
    private void SourceMarkOut() => Source.MarkOut();

    [RelayCommand]
    private void BeginTimecodeEditing() => BeginTimecodeEdit();

    [RelayCommand]
    private void CommitTimecodeEditing() => CommitTimecodeEdit();

    [RelayCommand]
    private void CancelTimecodeEditing() => CancelTimecodeEdit();
}
