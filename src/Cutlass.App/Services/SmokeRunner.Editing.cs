using System.Globalization;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Services;

/// <summary>
/// The M6 smoke commands (TECH_BRIEF 12, M6): the Source monitor and three-point edits, match frame, key presses
/// through the real keymap, click-to-type timecode, copy and paste, ripple trims, transitions, speed, gaps and tracks.
/// Keys go through <see cref="KeyBindingService.HandleKeyDown"/> and menu commands through the command registry, the
/// same paths the window uses.
/// </summary>
public sealed partial class SmokeRunner
{
    private bool ExecuteEditing(SmokeStep step)
    {
        var timeline = _vm.Timeline;
        var viewer = _vm.Viewer;
        switch (step.Command)
        {
            case "source-open":
                SourceOpen(step);
                return true;
            case "source-in":
                SourceMark(step, isIn: true);
                return true;
            case "source-out":
                SourceMark(step, isIn: false);
                return true;
            case "source-insert":
            case "source-overwrite":
                Expect(viewer.Source.HasMedia, "nothing is open in Source");
                var insert = step.Command == "source-insert";
                Edit(() => Run(insert ? CommandIds.Insert : CommandIds.Overwrite), (insert ? "inserted " : "overwrote with ") + viewer.Source.Describe());
                return true;
            case "monitor":
                Monitor(step);
                return true;
            case "match-frame":
                MatchFrame(step);
                return true;
            case "key":
                PressKey(step);
                return true;
            case "timecode":
                TypeTimecode(step);
                return true;
            case "copy":
                Expect(timeline.SelectedClipIds.Count > 0, "nothing is selected");
                Run(CommandIds.Copy);
                Expect(timeline.HasClipboard, "the clipboard is empty after copy");
                _log.Write("copied " + timeline.SelectedClipIds.Count.ToString(CultureInfo.InvariantCulture) + " clips");
                return true;
            case "cut":
                Expect(timeline.SelectedClipIds.Count > 0, "nothing is selected");
                Edit(() => Run(CommandIds.Cut), "cut");
                return true;
            case "paste":
                Expect(timeline.HasClipboard, "the clipboard is empty");
                Edit(() => Run(step.Flag("insert") ? CommandIds.PasteInsert : CommandIds.Paste), step.Flag("insert") ? "pasted (insert)" : "pasted (overwrite)");
                return true;
            case "trim-start":
                Edit(() => Run(CommandIds.RippleTrimStart), "ripple trimmed the start to " + timeline.Playhead.ToString(CultureInfo.InvariantCulture));
                return true;
            case "trim-end":
                Edit(() => Run(CommandIds.RippleTrimEnd), "ripple trimmed the end to " + timeline.Playhead.ToString(CultureInfo.InvariantCulture));
                return true;
            case "add-transition":
                AddTransition(step);
                return true;
            case "speed":
                Speed(step);
                return true;
            case "select-gap":
                SelectGap(step);
                return true;
            case "close-gap":
                Expect(timeline.SelectedGap is not null, "no gap is selected");
                Edit(() => Run(CommandIds.CloseGap), "closed the gap");
                return true;
            case "add-track":
                AddTrack(step);
                return true;
            case "expect-duration":
                var frames = long.Parse(Arg(step, 0), NumberStyles.None, CultureInfo.InvariantCulture);
                Expect(timeline.SequenceEnd == frames, string.Create(CultureInfo.InvariantCulture, $"the sequence has {timeline.SequenceEnd} frames, expected {frames}"));
                return true;
            case "expect-playhead":
                var playhead = Position(step, 0);
                Expect(timeline.Playhead == playhead, string.Create(CultureInfo.InvariantCulture, $"the playhead is at {timeline.Playhead}, expected {playhead}"));
                return true;
            case "expect-source":
                ExpectSource(step);
                return true;
            case "expect-transitions":
                var track = Track(Arg(step, 0));
                var count = int.Parse(Arg(step, 1), NumberStyles.None, CultureInfo.InvariantCulture);
                Expect(track.Transitions.Length == count, string.Create(CultureInfo.InvariantCulture, $"{track.Name} holds {track.Transitions.Length} transitions, expected {count}"));
                return true;
            default:
                return false;
        }
    }

    // Runs a registered command the way a key or menu item does; fails when nothing is registered under the id.
    private void Run(string id) => Expect(_vm.Commands.TryExecute(id), "no command is registered for " + id);

    private void SourceOpen(SmokeStep step)
    {
        var asset = Media(Arg(step, 0));
        var source = _vm.Viewer.Source;
        _vm.Viewer.OpenSource(asset);
        Expect(_vm.Viewer.IsSourceActive, "the viewer does not show Source");
        if (step.Positional().Count > 1)
        {
            source.Seek(SourcePosition(step.Positional()[1]));
        }

        _log.Write("Source: " + source.Describe());
    }

    private void SourceMark(SmokeStep step, bool isIn)
    {
        var viewer = _vm.Viewer;
        Expect(viewer.IsSourceActive, "the viewer does not show Source (source-open first)");
        viewer.Source.Seek(SourcePosition(Arg(step, 0)));
        // The I and O keys, routed to the Source monitor because it shows.
        Run(isIn ? CommandIds.MarkIn : CommandIds.MarkOut);
        _log.Write("Source: " + viewer.Source.Describe());
    }

    private long SourcePosition(string text)
    {
        var source = _vm.Viewer.Source;
        var t = text.Trim();
        if (string.Equals(t, "end", StringComparison.OrdinalIgnoreCase))
        {
            return source.Duration - 1;
        }

        return TimecodeEntry.TryResolve(t, source.Playhead, source.Rate, out var frame)
            ? frame
            : throw Fail("not a Source position: " + text);
    }

    private void Monitor(SmokeStep step)
    {
        var which = Arg(step, 0).ToLowerInvariant();
        Run(which switch
        {
            "timeline" => CommandIds.ShowTimelineMonitor,
            "source" => CommandIds.ShowSourceMonitor,
            _ => throw Fail("monitor takes timeline or source"),
        });
        Expect(_vm.Viewer.IsSourceActive == (which == "source"), "the viewer did not switch to " + which);
        _log.Write("viewer shows " + which);
    }

    private void MatchFrame(SmokeStep step)
    {
        var viewer = _vm.Viewer;
        var expected = _vm.Timeline.FindMatchFrame() ?? throw Fail("no media clip under the playhead");
        Run(CommandIds.MatchFrame);
        Expect(viewer.IsSourceActive && viewer.Source.Asset?.Id == expected.AssetId, "match frame did not open the clip in Source");
        _log.Write("match frame: " + viewer.Source.Describe());
        if (step.Option("frame") is { } frameText)
        {
            var frame = long.Parse(frameText, NumberStyles.None, CultureInfo.InvariantCulture);
            Expect(viewer.Source.Playhead == frame, string.Create(CultureInfo.InvariantCulture, $"Source parked on {viewer.Source.Playhead}, expected {frame}"));
        }
    }

    private void PressKey(SmokeStep step)
    {
        var text = Arg(step, 0);
        var chord = KeyChord.TryParse(text, out var c) ? c : throw Fail("not a key chord: " + text);
        var before = _vm.Session.Current;
        var handled = _vm.Keys.HandleKeyDown(chord.Key, chord.Modifiers, textInputFocused: false);
        _vm.Keys.HandleKeyUp(chord.Key);
        Expect(handled, "no command took " + chord);
        if (step.Flag("edit"))
        {
            Expect(!ReferenceEquals(before, _vm.Session.Current), chord + " changed nothing");
        }

        _log.Write("key " + chord + (ReferenceEquals(before, _vm.Session.Current) ? string.Empty : ": " + _vm.Session.UndoLabel));
        if (!ReferenceEquals(before, _vm.Session.Current))
        {
            LogTimeline();
        }
    }

    private void TypeTimecode(SmokeStep step)
    {
        var viewer = _vm.Viewer;
        viewer.BeginTimecodeEdit();
        Expect(viewer.IsEditingTimecode, "the timecode readout did not turn into a field");
        viewer.TimecodeEditText = Arg(step, 0);
        Expect(viewer.CommitTimecodeEdit(), "the viewer did not accept " + Arg(step, 0));
        _log.Write("typed " + Arg(step, 0) + ": the viewer shows " + viewer.TimecodeText);
    }

    private void AddTransition(SmokeStep step)
    {
        var timeline = _vm.Timeline;
        var project = _vm.Session.Current;
        var positional = step.Positional();
        var kindText = positional.Count > 0 ? positional[0] : "crossfade";
        var kind = string.Equals(kindText, "audio", StringComparison.OrdinalIgnoreCase)
            ? TransitionKind.AudioCrossfade
            : Enum.TryParse<TransitionKind>(kindText, ignoreCase: true, out var k) ? k : throw Fail("no transition kind " + kindText);
        var rate = project.Sequence.Settings.Rate;
        // DESIGN 5.5: one second by default, centred on the cut.
        var frames = positional.Count > 1 ? long.Parse(positional[1], NumberStyles.None, CultureInfo.InvariantCulture) : rate.TimecodeBase;
        var trackKind = kind == TransitionKind.AudioCrossfade ? TrackKind.Audio : TrackKind.Video;
        var playhead = timeline.Playhead;
        var cuts = project.Sequence.Tracks
            .Where(t => t.Kind == trackKind && t.Targeted && !t.Locked)
            .Select(t => (Track: t, Cut: NearestCut(t, playhead)))
            .Where(x => x.Cut is not null)
            .Select(x => (x.Track.Id, x.Cut!.Value))
            .ToList();
        Expect(cuts.Count > 0, "no cut on a targeted " + trackKind + " track");
        Edit(
            () => timeline.Run(p => TimelineEdits.AddTransitions(timeline.Ids, p, cuts, kind, frames)),
            string.Create(CultureInfo.InvariantCulture, $"added {kind} ({frames} frames) at {string.Join(", ", cuts.Select(c => c.Item2))}"));
        foreach (var track in _vm.Session.Current.Sequence.Tracks.Where(t => !t.Transitions.IsEmpty))
        {
            _log.Write("  " + track.Name + " transitions: " + string.Join(", ", track.Transitions.Select(x =>
                string.Create(CultureInfo.InvariantCulture, $"{x.Kind} {x.Duration} frames at {TimelineQuery.CutFrame(track, x)}"))));
        }
    }

    private static long? NearestCut(Track track, long frame)
    {
        long? best = null;
        for (var i = 0; i + 1 < track.Clips.Length; i++)
        {
            var cut = track.Clips[i].End;
            if (track.Clips[i + 1].Start == cut && (best is null || Math.Abs(cut - frame) < Math.Abs(best.Value - frame)))
            {
                best = cut;
            }
        }

        return best;
    }

    private void Speed(SmokeStep step)
    {
        var timeline = _vm.Timeline;
        var percent = int.Parse(Arg(step, 0), NumberStyles.None, CultureInfo.InvariantCulture);
        var ids = TimelineQuery.ExistingIds(_vm.Session.Current, timeline.SelectedClipIds);
        Expect(ids.Length > 0, "nothing is selected");
        var ripple = !step.Flag("noripple");
        var what = string.Create(CultureInfo.InvariantCulture, $"speed {percent}%{(ripple ? " (ripple)" : string.Empty)}");
        if (_dialogs is { } dialogs)
        {
            // Ctrl+R opens the speed dialog, which the smoke dialog service answers with this speed.
            dialogs.NextSpeedPercent = percent;
            dialogs.NextSpeedRipple = ripple;
            var before = _vm.Session.Current;
            var handled = _vm.Keys.HandleKeyDown(Avalonia.Input.Key.R, Avalonia.Input.KeyModifiers.Control, textInputFocused: false);
            dialogs.NextSpeedPercent = null;
            Expect(handled, "Ctrl+R is not bound");
            Expect(!ReferenceEquals(before, _vm.Session.Current), what + " changed nothing");
            _log.Write(what + " through the speed dialog");
            LogTimeline();
            return;
        }

        // Without the smoke dialog service: the edit the dialog applies (DESIGN 5.5: 25 to 400 percent, ripple on).
        Edit(() => timeline.Run(p => TimelineEdits.SetSpeed(p, ids, percent, ripple)), what);
    }

    private void SelectGap(SmokeStep step)
    {
        var track = Track(Arg(step, 0));
        var frame = Position(step, 1);
        var gap = TimelineQuery.GapAt(track, frame) ?? throw Fail(string.Create(CultureInfo.InvariantCulture, $"no gap on {track.Name} at {frame}"));
        _vm.Timeline.SelectGap(track.Id, gap.Start, gap.End);
        Expect(_vm.Timeline.SelectedGap is not null, "the gap could not be selected");
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"selected the gap [{gap.Start},{gap.End}) on {track.Name}"));
    }

    private void AddTrack(SmokeStep step)
    {
        var kind = Arg(step, 0).ToLowerInvariant();
        var before = _vm.Session.Current.Sequence.Tracks.Length;
        Edit(
            () => Run(kind switch
            {
                "video" => CommandIds.AddVideoTrack,
                "audio" => CommandIds.AddAudioTrack,
                _ => throw Fail("add-track takes video or audio"),
            }),
            "added a " + kind + " track");
        var tracks = _vm.Session.Current.Sequence.Tracks;
        Expect(tracks.Length == before + 1, "the track count did not grow by one");
        _log.Write("tracks: " + string.Join(", ", tracks.Select(t => t.Name)));
    }

    private void ExpectSource(SmokeStep step)
    {
        var source = _vm.Viewer.Source;
        Expect(source.HasMedia, "nothing is open in Source");
        if (step.Option("media") is { } media)
        {
            Expect(source.Asset!.Id == Media(media).Id, "Source shows " + source.Name + ", expected " + media);
        }

        Check("playhead", source.Playhead);
        Check("in", source.InPoint);
        Check("out", source.OutPoint);
        _log.Write("Source: " + source.Describe());

        void Check(string name, long? actual)
        {
            if (step.Option(name) is not { } text)
            {
                return;
            }

            long? expected = text == "-" ? null : long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            Expect(actual == expected, string.Create(CultureInfo.InvariantCulture, $"Source {name} is {actual?.ToString(CultureInfo.InvariantCulture) ?? "-"}, expected {text}"));
        }
    }
}
