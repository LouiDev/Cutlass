using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Cutlass.App.ViewModels;
using Cutlass.Compiler.Export;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Media.Export;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.App.Services;

/// <summary>
/// Runs a <see cref="SmokeScript"/> inside the running app (UI thread) through the same view model commands the
/// menus, keys and the export dialog use, logs every step and stops at the first failure. It touches no controls;
/// screenshots go through a callback the window provides. A dialog message during a step fails that step.
/// </summary>
public sealed partial class SmokeRunner
{
    /// <summary>How long <c>frame</c> waits for the viewer by default.</summary>
    public static readonly TimeSpan DefaultFrameTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long <c>export</c> waits for the job by default.</summary>
    public static readonly TimeSpan DefaultExportTimeout = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(120);

    private readonly MainWindowViewModel _vm;
    private readonly AppServices _services;
    private readonly SmokeDialogService? _dialogs;
    private readonly Func<string, Task> _capture;
    private readonly SmokeLog _log;
    private string _baseDirectory = Environment.CurrentDirectory;
    private long _lastExportFrames = -1;

    /// <summary>Creates the runner.</summary>
    /// <param name="vm">The main window view model.</param>
    /// <param name="services">The composition root (ffprobe for <c>probe</c>).</param>
    /// <param name="dialogs">The non-interactive dialog service, whose messages fail a step, or null.</param>
    /// <param name="capture">Renders the main window into a PNG file.</param>
    /// <param name="log">The log.</param>
    public SmokeRunner(MainWindowViewModel vm, AppServices services, SmokeDialogService? dialogs, Func<string, Task> capture, SmokeLog log)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(log);
        _vm = vm;
        _services = services;
        _dialogs = dialogs;
        _capture = capture;
        _log = log;
    }

    /// <summary>Reads, parses and runs a script file. Relative paths in it resolve against its folder.</summary>
    /// <param name="scriptPath">The script.</param>
    /// <returns>0 when every step passed, 1 otherwise.</returns>
    public async Task<int> RunFileAsync(string scriptPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        var full = Path.GetFullPath(scriptPath);
        string text;
        try
        {
            text = await File.ReadAllTextAsync(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("SMOKE FAIL: the script " + full + " cannot be read: " + ex.Message);
            return 1;
        }

        IReadOnlyList<SmokeStep> steps;
        try
        {
            steps = SmokeScript.Parse(text);
        }
        catch (FormatException ex)
        {
            _log.Write("SMOKE FAIL: " + ex.Message);
            return 1;
        }

        _baseDirectory = Path.GetDirectoryName(full) ?? Environment.CurrentDirectory;
        _log.Write("smoke script " + full);
        return await RunAsync(steps);
    }

    /// <summary>Runs parsed steps.</summary>
    /// <param name="steps">The steps.</param>
    /// <returns>0 when every step passed, 1 otherwise.</returns>
    public async Task<int> RunAsync(IReadOnlyList<SmokeStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"{steps.Count} steps; ffmpeg state {_services.FfmpegState}: {_vm.FfmpegStatusText}"));
        _vm.PropertyChanged += OnMainPropertyChanged;
        SnapshotSettings();
        try
        {
            foreach (var step in steps)
            {
                _log.Write(string.Create(CultureInfo.InvariantCulture, $"line {step.Line}: {step}"));
                var messages = _dialogs?.Messages.Count ?? 0;
                try
                {
                    await ExecuteAsync(step);
                    if (_dialogs is not null && _dialogs.Messages.Count > messages)
                    {
                        throw new InvalidOperationException("a dialog reported a problem: " + _dialogs.Messages[^1]);
                    }
                }
                catch (Exception ex)
                {
                    _log.Write(string.Create(CultureInfo.InvariantCulture, $"SMOKE FAIL at line {step.Line} ({step.Command}): {ex.Message}"));
                    return 1;
                }
            }
        }
        finally
        {
            _vm.PropertyChanged -= OnMainPropertyChanged;
            _agent?.Dispose();
            _agent = null;
            await RestoreSettingsAsync();
        }

        _log.Write(string.Create(CultureInfo.InvariantCulture, $"SMOKE PASS: {steps.Count} steps"));
        return 0;
    }

    private static InvalidOperationException Fail(string message) => new(message);

    private static string Describe(Clip clip, Track track) =>
        string.Create(CultureInfo.InvariantCulture, $"{track.Name}:{clip.Id} [{clip.Start},{clip.End})");

    private static TimeSpan Seconds(SmokeStep step, string name, TimeSpan fallback) =>
        step.Option(name) is { } text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0
            ? TimeSpan.FromSeconds(s)
            : fallback;

    private async Task ExecuteAsync(SmokeStep step)
    {
        var timeline = _vm.Timeline;
        switch (step.Command)
        {
            case "new":
                await _vm.NewCommand.ExecuteAsync(null);
                Expect(_vm.HasProject, "no project is open after New");
                break;
            case "open":
                Expect(await _vm.OpenProjectFileAsync(PathArg(step, 0)), "the project did not open");
                LogTimeline();
                break;
            case "save":
                await SaveAsync(PathArg(step, 0));
                break;
            case "import":
                await ImportAsync(step);
                break;
            case "insert":
            case "overwrite":
                PlaceAtPlayhead(step, step.Command == "insert");
                break;
            case "place":
                Place(step);
                break;
            case "drop-files":
                await DropFilesAsync(step);
                break;
            case "playhead":
                timeline.Playhead = Position(step, 0);
                _log.Write(string.Create(CultureInfo.InvariantCulture, $"playhead {timeline.Playhead} (sequence {timeline.SequenceEnd} frames)"));
                break;
            case "frame":
                await WaitForFrameAsync(Seconds(step, "timeout", DefaultFrameTimeout));
                break;
            case "split":
                Edit(() => timeline.SplitAtPlayheadCommand.Execute(null), "split at " + timeline.Playhead.ToString(CultureInfo.InvariantCulture));
                break;
            case "select-at":
                SelectAt(step);
                break;
            case "select-all":
                timeline.SelectAllCommand.Execute(null);
                _log.Write("selected " + timeline.SelectedClipIds.Count.ToString(CultureInfo.InvariantCulture) + " clips");
                break;
            case "deselect":
                timeline.EscapeCommand.Execute(null);
                break;
            case "delete":
                Expect(timeline.SelectedClipIds.Count > 0 || timeline.SelectedGap is not null, "nothing is selected");
                Edit(() => timeline.DeleteSelectionCommand.Execute(null), "lift");
                break;
            case "ripple-delete":
                Expect(timeline.SelectedClipIds.Count > 0 || timeline.SelectedGap is not null, "nothing is selected");
                Edit(() => timeline.RippleDeleteSelectionCommand.Execute(null), "ripple delete");
                break;
            case "undo":
                Expect(timeline.UndoCommand.CanExecute(null), "there is nothing to undo");
                var undoLabel = _vm.Session.UndoLabel;
                Edit(() => timeline.UndoCommand.Execute(null), "undo " + undoLabel);
                break;
            case "redo":
                Expect(timeline.RedoCommand.CanExecute(null), "there is nothing to redo");
                var redoLabel = _vm.Session.RedoLabel;
                Edit(() => timeline.RedoCommand.Execute(null), "redo " + redoLabel);
                break;
            case "zoom-fit":
                timeline.ZoomToFitCommand.Execute(null);
                break;
            case "expect-length":
                var length = long.Parse(Arg(step, 0), NumberStyles.None, CultureInfo.InvariantCulture);
                Expect(timeline.SequenceEnd == length, string.Create(CultureInfo.InvariantCulture, $"the sequence has {timeline.SequenceEnd} frames, expected {length}"));
                break;
            case "expect-clips":
                ExpectClips(step);
                break;
            case "export":
                await ExportAsync(step);
                break;
            case "probe":
                await ProbeAsync(step);
                break;
            case "screenshot":
                var shot = PathArg(step, 0);
                await _capture(shot);
                _log.Write("screenshot " + shot);
                break;
            case "play":
                await PlayAsync(step);
                break;
            case "stop":
                StopPlayback(step);
                break;
            case "wait-jobs":
                await WaitForJobsAsync(Seconds(step, "timeout", DefaultJobsTimeout));
                break;
            case "mute":
                var muteTrack = Track(Arg(step, 0));
                Edit(() => timeline.ToggleTrackMuted(muteTrack.Id), "toggle mute of " + muteTrack.Name);
                break;
            case "solo":
                var soloTrack = Track(Arg(step, 0));
                Edit(() => timeline.ToggleTrackSolo(soloTrack.Id), "toggle solo of " + soloTrack.Name);
                break;
            case "expect-meter":
                ExpectMeter(step);
                break;
            case "expect-artifacts":
                ExpectArtifacts(step);
                break;
            case "jobs":
                ShowJobs(step);
                break;
            case "wait":
                await Task.Delay(TimeSpan.FromMilliseconds(int.Parse(Arg(step, 0), NumberStyles.None, CultureInfo.InvariantCulture)));
                break;
            case "log":
                _log.Write(string.Join(' ', step.Args));
                break;
            default:
                if (!ExecuteEditing(step) && !ExecuteInspect(step) && !await ExecuteSettingsAsync(step) && !await ExecuteAgentAsync(step) && !await ExecuteMcpAsync(step) && !await ExecuteClientsAsync(step))
                {
                    throw Fail("unknown command " + step.Command);
                }

                break;
        }
    }

    // expect-clips <track> <count> counts one track; expect-clips <count> counts the clips of every track.
    private void ExpectClips(SmokeStep step)
    {
        var positional = step.Positional();
        if (positional.Count == 1)
        {
            var total = _vm.Session.Current.Sequence.Tracks.Sum(t => t.Clips.Length);
            var expected = int.Parse(positional[0], NumberStyles.None, CultureInfo.InvariantCulture);
            Expect(total == expected, string.Create(CultureInfo.InvariantCulture, $"the sequence holds {total} clips, expected {expected}"));
            return;
        }

        var track = Track(Arg(step, 0));
        var count = int.Parse(Arg(step, 1), NumberStyles.None, CultureInfo.InvariantCulture);
        Expect(track.Clips.Length == count, string.Create(CultureInfo.InvariantCulture, $"{track.Name} holds {track.Clips.Length} clips, expected {count}"));
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw Fail(message);
        }
    }

    private static string Arg(SmokeStep step, int index)
    {
        var positional = step.Positional();
        return index < positional.Count
            ? positional[index]
            : throw Fail("missing argument " + (index + 1).ToString(CultureInfo.InvariantCulture) + ": " + SmokeScript.Commands[step.Command]);
    }

    private string PathArg(SmokeStep step, int index) => ResolvePath(Arg(step, index));

    private string ResolvePath(string path) => Path.GetFullPath(Path.Combine(_baseDirectory, path));

    private long Position(SmokeStep step, int index)
    {
        var text = Arg(step, index);
        var timeline = _vm.Timeline;
        return SmokeScript.TryResolvePosition(text, timeline.SequenceEnd, timeline.Playhead, _vm.Session.Current.Sequence.Settings.Rate, out var frame)
            ? frame
            : throw Fail("not a timeline position: " + text);
    }

    private Track Track(string nameOrId)
    {
        var tracks = _vm.Session.Current.Sequence.Tracks;
        return tracks.FirstOrDefault(t => t.Id == nameOrId)
            ?? tracks.FirstOrDefault(t => string.Equals(t.Name, nameOrId, StringComparison.OrdinalIgnoreCase))
            ?? throw Fail("no track " + nameOrId);
    }

    private MediaAsset Media(string reference)
    {
        var media = _vm.Session.Current.Media;
        if (int.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 1 && index <= media.Length)
        {
            return media[index - 1];
        }

        return media.FirstOrDefault(m => m.Id == reference)
            ?? media.FirstOrDefault(m => string.Equals(Path.GetFileName(m.Path), reference, StringComparison.OrdinalIgnoreCase))
            ?? media.FirstOrDefault(m => string.Equals(Path.GetFileNameWithoutExtension(m.Path), reference, StringComparison.OrdinalIgnoreCase))
            ?? throw Fail("no media " + reference + " in the project");
    }

    // Runs an edit command and fails when it left the project unchanged.
    private void Edit(Action command, string what)
    {
        var before = _vm.Session.Current;
        command();
        Expect(!ReferenceEquals(before, _vm.Session.Current), what + " changed nothing");
        _log.Write(what);
        LogTimeline();
    }

    private void LogTimeline()
    {
        var project = _vm.Session.Current;
        foreach (var track in project.Sequence.Tracks.Where(t => !t.Clips.IsEmpty))
        {
            _log.Write("  " + track.Name + ": " + string.Join(", ", track.Clips.Select(c => Describe(c, track))));
        }

        _log.Write(string.Create(CultureInfo.InvariantCulture, $"  sequence {project.Sequence.Settings.Width}x{project.Sequence.Settings.Height} {project.Sequence.Settings.Rate.DisplayName}, {_vm.Timeline.SequenceEnd} frames, playhead {_vm.Timeline.Playhead}"));
    }

    private async Task SaveAsync(string path)
    {
        Expect(_vm.HasProject, "no project is open");
        Expect(await _vm.SaveToPathAsync(path), "the project was not saved");
        Expect(!_vm.IsDirty, "the project is still dirty after saving");
        var sidecar = path + ".user";
        var exists = await Task.Run(() => (File.Exists(path), File.Exists(sidecar)));
        Expect(exists.Item1, "the project file is missing after saving");
        Expect(exists.Item2, "the sidecar " + sidecar + " is missing after saving");
        _log.Write("saved " + path + " and " + Path.GetFileName(sidecar));
    }

    private async Task ImportAsync(SmokeStep step)
    {
        var paths = step.Positional().Select(ResolvePath).ToList();
        Expect(paths.Count > 0, "missing argument 1: " + SmokeScript.Commands[step.Command]);
        if (!_vm.HasProject)
        {
            await _vm.NewCommand.ExecuteAsync(null);
        }

        var assets = await _vm.MediaBin.ImportPathsAsync(paths);
        foreach (var asset in assets)
        {
            var info = asset.Info;
            var video = info.Video is { } v
                ? string.Create(CultureInfo.InvariantCulture, $"video {v.Codec} {v.DisplayWidth}x{v.DisplayHeight} {v.AvgFrameRate.ToFfString()} fps")
                : "no video";
            var audio = info.Audio is { } a
                ? string.Create(CultureInfo.InvariantCulture, $"audio {a.Codec} {a.SampleRate} Hz {a.Channels} ch")
                : "no audio";
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"imported {asset.Id} {Path.GetFileName(asset.Path)}: {info.DurationMicros / 1e6:0.000} s, {video}, {audio}, {asset.Fingerprint[..Math.Min(asset.Fingerprint.Length, 19)]}..."));
        }

        Expect(assets.Count == paths.Count, string.Create(CultureInfo.InvariantCulture, $"{assets.Count} of {paths.Count} files were imported"));
    }

    private void PlaceAtPlayhead(SmokeStep step, bool insert)
    {
        var asset = Media(Arg(step, 0));
        var timeline = _vm.Timeline;
        Edit(
            () =>
            {
                if (insert)
                {
                    timeline.InsertMediaAtPlayhead(asset.Id);
                }
                else
                {
                    timeline.OverwriteMediaAtPlayhead(asset.Id);
                }
            },
            (insert ? "inserted " : "overwrote with ") + Path.GetFileName(asset.Path));
    }

    private async Task DropFilesAsync(SmokeStep step)
    {
        var track = Track(Arg(step, 0));
        var frame = Position(step, 1);
        var paths = step.Positional("insert").Skip(2).Select(ResolvePath).ToList();
        Expect(paths.Count > 0, "missing argument 3: " + SmokeScript.Commands[step.Command]);
        var before = _vm.Session.Current;
        await _vm.Timeline.DropFilesAsync(paths, track.Id, frame, step.Flag("insert"));
        Expect(!ReferenceEquals(before, _vm.Session.Current), "the drop changed nothing");
        _log.Write("dropped " + string.Join(", ", paths.Select(Path.GetFileName)) + " on " + track.Name);
        LogTimeline();
    }

    private void Place(SmokeStep step)
    {
        var asset = Media(Arg(step, 0));
        var track = Track(Arg(step, 1));
        var frame = Position(step, 2);
        var insert = step.Flag("insert");
        Edit(() => _vm.Timeline.PlaceMedia(asset.Id, track.Id, frame, insert), "placed " + Path.GetFileName(asset.Path) + " on " + track.Name);
    }

    private void SelectAt(SmokeStep step)
    {
        var frame = Position(step, 0);
        var trackFilter = step.Positional().Skip(1).Select(Track).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var project = _vm.Session.Current;
        var ids = TimelineQuery.ClipsAt(project, frame)
            .Where(x => trackFilter.Count == 0 || trackFilter.Contains(x.Track.Id))
            .Select(x => x.Clip.Id)
            .ToList();
        Expect(ids.Count > 0, "no clip at frame " + frame.ToString(CultureInfo.InvariantCulture));
        var timeline = _vm.Timeline;
        timeline.Select(timeline.ExpandLinked(ids, singleSide: false), additive: false);
        Expect(timeline.SelectedClipIds.Count > 0, "the clips at the frame could not be selected (locked tracks?)");
        var selected = timeline.SelectedClipIds.Select(id => TimelineQuery.FindClip(project, id)).OfType<(Track Track, Clip Clip)>()
            .Select(x => Describe(x.Clip, x.Track));
        _log.Write("selected " + string.Join(", ", selected));
    }

    private async Task WaitForFrameAsync(TimeSpan timeout)
    {
        var viewer = _vm.Viewer;
        var done = new TaskCompletionSource<(long RequestId, long Frame)>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPresented(long requestId, long frame)
        {
            if (requestId == viewer.LatestRequestId)
            {
                done.TrySetResult((requestId, frame));
            }
        }

        void OnViewerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewerViewModel.StatusText) && viewer.StatusText is { } status && !viewer.IsWaiting)
            {
                done.TrySetException(Fail(status));
            }
        }

        _vm.Presenter.FramePresented += OnPresented;
        viewer.PropertyChanged += OnViewerChanged;
        try
        {
            viewer.Refresh();
            var requestId = viewer.LatestRequestId;
            var target = viewer.RequestedFrame;
            Expect(requestId >= 0, string.Create(CultureInfo.InvariantCulture, $"nothing to show at frame {target} (empty sequence or past its end)"));
            Expect(viewer.StatusText is null || viewer.IsWaiting, viewer.StatusText ?? string.Empty);
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"requested frame {target} as request {requestId}"));
            var finished = await Task.WhenAny(done.Task, Task.Delay(timeout));
            Expect(finished == done.Task, string.Create(CultureInfo.InvariantCulture, $"request {requestId} was not presented within {timeout.TotalSeconds:0} s"));
            var (presentedId, presentedFrame) = await done.Task;
            Expect(presentedFrame == target, string.Create(CultureInfo.InvariantCulture, $"request {presentedId} presented frame {presentedFrame}, expected {target}"));
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"presented request {presentedId}: timeline frame {presentedFrame}"));
        }
        finally
        {
            _vm.Presenter.FramePresented -= OnPresented;
            viewer.PropertyChanged -= OnViewerChanged;
        }
    }

    private async Task ExportAsync(SmokeStep step)
    {
        var presetId = Arg(step, 0);
        var path = PathArg(step, 1);
        var dialog = _vm.CreateExportDialog() ?? throw Fail("export needs ffmpeg");
        var option = dialog.Presets.FirstOrDefault(o => string.Equals(o.Preset.Id, presetId, StringComparison.OrdinalIgnoreCase))
            ?? throw Fail("no preset " + presetId + "; the presets are " + string.Join(", ", dialog.Presets.Select(o => o.Preset.Id)));
        Expect(option.IsAvailable, option.UnavailableReason ?? string.Empty);
        dialog.SelectedPreset = option;
        if (step.Option("quality") is { } quality)
        {
            dialog.SelectedQuality = Enum.TryParse<ExportQuality>(quality, ignoreCase: true, out var q) ? q : throw Fail("no quality " + quality);
        }

        if (step.Option("range") is { } range)
        {
            if (string.Equals(range, "inout", StringComparison.OrdinalIgnoreCase))
            {
                Expect(dialog.HasInOut, "the sequence has no In or Out");
                dialog.IsInOutRange = true;
            }
            else
            {
                dialog.IsWholeRange = true;
            }
        }

        // M7: height=<n> picks the custom resolution (even, aspect locked), width=<n> a GIF width.
        if (step.Option("height") is { } customHeight)
        {
            dialog.SelectedResolution = dialog.Resolutions.FirstOrDefault(r => r.IsCustom) ?? throw Fail("the preset offers no custom size");
            dialog.CustomHeightText = customHeight;
            _log.Write("custom resolution " + dialog.CustomSizeText);
        }

        if (step.Option("width") is { } gifWidth)
        {
            dialog.SelectedResolution = dialog.Resolutions.FirstOrDefault(r => r.Width.ToString(CultureInfo.InvariantCulture) == gifWidth)
                ?? throw Fail("no output width " + gifWidth + "; the choices are " + string.Join(", ", dialog.Resolutions.Select(r => r.Label)));
        }

        dialog.OutputPath = path;
        await dialog.ShowCommandCommand.ExecuteAsync(null);
        _log.Write("ffmpeg command: " + dialog.CommandText);
        _log.Write("range " + dialog.RangeText + ", " + dialog.EstimatedSizeText);
        Expect(dialog.ProblemText is null, dialog.ProblemText ?? string.Empty);

        var before = _vm.LastExport;
        var closed = false;
        dialog.CloseRequested += (_, queued) => closed = queued;
        await dialog.ExportCommand.ExecuteAsync(null);
        var job = _vm.LastExport;
        Expect(job is not null && !ReferenceEquals(job, before) && closed, "the export dialog did not queue an export");
        var (start, end) = dialog.SelectedRange();
        _lastExportFrames = end - start;
        await WaitForExportAsync(job!, Seconds(step, "timeout", DefaultExportTimeout));
    }

    private async Task WaitForExportAsync(ExportJob job, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        var lastPercent = -1;
        while (!job.Completion.IsCompleted)
        {
            await Task.WhenAny(job.Completion, Task.Delay(TimeSpan.FromSeconds(1)));
            var percent = (int)Math.Floor(job.Fraction * 100);
            if (percent != lastPercent && !job.Completion.IsCompleted)
            {
                lastPercent = percent;
                var speed = job.Speed is { } s ? " at " + s.ToString("0.0", CultureInfo.InvariantCulture) + "x" : string.Empty;
                _log.Write(string.Create(CultureInfo.InvariantCulture, $"export {job.State} {percent}%{speed}"));
            }

            if (DateTime.UtcNow - started > timeout)
            {
                job.Cancel();
                throw Fail(string.Create(CultureInfo.InvariantCulture, $"the export did not finish within {timeout.TotalSeconds:0} s"));
            }
        }

        if (job.State != ExportJobState.Completed)
        {
            var tail = string.Join(" | ", job.Log.TakeLast(8));
            throw Fail("the export ended " + job.State + ": " + (job.Error?.Message ?? "no error") + (tail.Length > 0 ? " | " + tail : string.Empty));
        }

        var (finalExists, partialExists, size) = await Task.Run(() =>
            (File.Exists(job.OutputPath), File.Exists(job.PartialPath), File.Exists(job.OutputPath) ? new FileInfo(job.OutputPath).Length : 0));
        Expect(finalExists, "the export completed but " + job.OutputPath + " is missing");
        Expect(!partialExists, "the partial file " + job.PartialPath + " was left behind");
        foreach (var warning in job.Warnings)
        {
            _log.Write("export warning: " + warning);
        }

        _log.Write(string.Create(CultureInfo.InvariantCulture, $"exported {job.OutputPath} ({size} bytes, {(DateTime.UtcNow - started).TotalSeconds:0.0} s)"));
    }

    private async Task ProbeAsync(SmokeStep step)
    {
        var path = PathArg(step, 0);
        var toolset = _services.Toolset ?? throw Fail("probe needs ffprobe");
        string[] args =
        [
            "-v", "error", "-count_frames",
            "-show_entries", "stream=index,codec_type,codec_name,codec_tag_string,width,height,pix_fmt,nb_read_frames,color_space,color_primaries,color_transfer,color_range,sample_rate,channels,duration:format=duration,format_name",
            "-of", "json", path,
        ];
        var spec = new ProcessSpec(toolset.FfprobePath, args, _services.Work.Path, false, ProcessPurpose.Probe, false);
        var result = await _services.Runner.RunAsync(spec, ProbeTimeout, CancellationToken.None);
        result.EnsureSuccess();
        using var json = JsonDocument.Parse(result.Stdout);
        var streams = json.RootElement.TryGetProperty("streams", out var s) ? s.EnumerateArray().ToList() : [];
        var video = streams.FirstOrDefault(x => Text(x, "codec_type") == "video");
        var audio = streams.FirstOrDefault(x => Text(x, "codec_type") == "audio");
        var duration = json.RootElement.TryGetProperty("format", out var format) ? Text(format, "duration") : null;
        var hasVideo = video.ValueKind == JsonValueKind.Object;
        var hasAudio = audio.ValueKind == JsonValueKind.Object;
        var frames = hasVideo && long.TryParse(Text(video, "nb_read_frames"), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
        _log.Write(hasVideo
            ? string.Create(CultureInfo.InvariantCulture, $"probe video: {Text(video, "codec_name")} {Text(video, "width")}x{Text(video, "height")} {Text(video, "pix_fmt")}, {frames} frames, colorspace {Text(video, "color_space")}, primaries {Text(video, "color_primaries")}, transfer {Text(video, "color_transfer")}, range {Text(video, "color_range")}")
            : "probe video: none");
        _log.Write(hasAudio
            ? string.Create(CultureInfo.InvariantCulture, $"probe audio: {Text(audio, "codec_name")} {Text(audio, "sample_rate")} Hz {Text(audio, "channels")} ch")
            : "probe audio: none");
        _log.Write("probe duration: " + (duration ?? "unknown") + " s");

        if (step.Option("frames") is { } expectedFrames)
        {
            var expected = string.Equals(expectedFrames, "export", StringComparison.OrdinalIgnoreCase)
                ? _lastExportFrames
                : long.Parse(expectedFrames, NumberStyles.None, CultureInfo.InvariantCulture);
            Expect(expected >= 0, "frames=export needs an export first");
            Expect(frames == expected, string.Create(CultureInfo.InvariantCulture, $"the file has {frames} video frames, expected {expected}"));
        }

        if (step.Option("vcodec") is { } vcodec)
        {
            Expect(hasVideo && Text(video, "codec_name") == vcodec, "the video codec is " + (hasVideo ? Text(video, "codec_name") : "missing") + ", expected " + vcodec);
        }

        if (step.Option("acodec") is { } acodec)
        {
            Expect(hasAudio && Text(audio, "codec_name") == acodec, "the audio codec is " + (hasAudio ? Text(audio, "codec_name") : "missing") + ", expected " + acodec);
        }

        if (step.Option("size") is { } size)
        {
            Expect(hasVideo && Text(video, "width") + "x" + Text(video, "height") == size, "the video size is not " + size);
        }

        ExpectProbeM7(step, json.RootElement, video, audio);

        if (step.Flag("bt709"))
        {
            Expect(
                hasVideo && Text(video, "color_space") == "bt709" && Text(video, "color_primaries") == "bt709" && Text(video, "color_transfer") == "bt709",
                "the video is not tagged BT.709");
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
            : null;

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ToastText) && !string.IsNullOrEmpty(_vm.ToastText))
        {
            _log.Write("toast: " + _vm.ToastText.Replace('\n', ' '));
        }
    }
}
