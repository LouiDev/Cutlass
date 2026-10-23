using System.Collections.Immutable;
using System.Globalization;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Services.Smoke;

/// <summary>
/// The M6 verify commands that set up the DESIGN 5 checklist screenshots: Source monitor drags with a stream choice,
/// Effects tab drops, transition selection, volume keyframes, the media panel tab and an Inspector section check.
/// Drops go through the same view model calls the timeline control makes after reading the drag data, and the Source
/// payload round-trips through its drag text first.
/// </summary>
public sealed partial class SmokeRunner
{
    private bool ExecuteInspect(SmokeStep step)
    {
        switch (step.Command)
        {
            case "source-drop":
                SourceDrop(step);
                return true;
            case "effect-drop":
                EffectDrop(step);
                return true;
            case "select-transition":
                SelectTransition(step);
                return true;
            case "volume-key":
                VolumeKey(step);
                return true;
            case "panel":
                Panel(step);
                return true;
            case "expect-section":
                ExpectSection(step);
                return true;
            default:
                return false;
        }
    }

    // source-drop video|audio|both <track> <pos> [insert]: the Source canvas or a stream handle dropped on a track.
    private void SourceDrop(SmokeStep step)
    {
        var streams = Arg(step, 0).ToLowerInvariant() switch
        {
            "both" => SourceStreams.Both,
            "video" => SourceStreams.Video,
            "audio" => SourceStreams.Audio,
            _ => throw Fail("source-drop takes video, audio or both"),
        };
        var payload = _vm.Viewer.CreateSourceDrag(streams) ?? throw Fail("Source cannot drag " + streams + " (nothing open or no such stream)");
        Expect(SourceDrag.TryParse(payload.Format(), out var drag) && drag == payload, "the Source drag text does not round-trip");
        var track = Track(Arg(step, 1));
        var frame = Position(step, 2);
        Expect(!_vm.Timeline.PreviewSourcePlacements(drag!, track.Id).IsEmpty, "the timeline shows no ghost for the Source drop on " + track.Name);
        var insert = step.Flag("insert");
        Edit(
            () => _vm.Timeline.PlaceSource(drag!, track.Id, frame, insert),
            string.Create(CultureInfo.InvariantCulture, $"dropped Source {streams} ({_vm.Viewer.Source.Describe()}) on {track.Name} at {frame}{(insert ? " (insert)" : string.Empty)}"));
    }

    // effect-drop <effect id> <track> <pos>: an Effects tab item dropped on a track.
    private void EffectDrop(SmokeStep step)
    {
        var effects = _vm.Timeline.Effects ?? throw Fail("the timeline has no Effects tab");
        var id = Arg(step, 0);
        var track = Track(Arg(step, 1));
        var frame = Position(step, 2);
        var preview = effects.PreviewAt(id, track.Id, frame) ?? throw Fail("the timeline shows no ghost for " + id + " on " + track.Name);
        Edit(
            () => effects.ApplyAt(id, track.Id, frame),
            string.Create(CultureInfo.InvariantCulture, $"dropped {id} on {track.Name} at {frame} ({preview.Category} [{preview.Start},{preview.Start + preview.Duration}))"));
        _log.Write("  Effects: " + effects.StatusText);
    }

    // select-transition <track> [index]: a click on a transition block.
    private void SelectTransition(SmokeStep step)
    {
        var track = Track(Arg(step, 0));
        var positional = step.Positional();
        var index = positional.Count > 1 ? int.Parse(positional[1], NumberStyles.None, CultureInfo.InvariantCulture) : 0;
        Expect(index < track.Transitions.Length, track.Name + " has no transition " + index.ToString(CultureInfo.InvariantCulture));
        var transition = track.Transitions[index];
        _vm.Timeline.SelectTransition(transition.Id);
        Expect(_vm.Timeline.SelectedTransitionId == transition.Id, "the transition could not be selected");
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"selected {transition.Kind} ({transition.Duration} frames) on {track.Name}"));
    }

    // volume-key <track> <pos> <dB>: selects the audio clip under the frame and adds a keyframe at that level.
    private void VolumeKey(SmokeStep step)
    {
        var track = Track(Arg(step, 0));
        var frame = Position(step, 1);
        var db = double.Parse(Arg(step, 2), NumberStyles.Float, CultureInfo.InvariantCulture);
        var clip = TimelineQuery.ClipAt(track, frame) as MediaClip;
        Expect(clip is { Stream: StreamKind.Audio }, string.Create(CultureInfo.InvariantCulture, $"no audio clip on {track.Name} at {frame}"));
        var at = frame - clip!.Start;
        _vm.Timeline.Select([clip.Id], false);
        Edit(
            () => _vm.Timeline.Run(p => TimelineEdits.UpdateClips(p, [clip.Id], c => c is MediaClip m
                ? m with { Audio = m.Audio with { Keys = [.. m.Audio.Keys.Where(k => k.Frame != at).Append(new VolumeKey(at, db)).OrderBy(k => k.Frame)] } }
                : c, "Add Keyframe")),
            string.Create(CultureInfo.InvariantCulture, $"keyframe {db} dB at clip frame {at} on {track.Name}"));
        var keys = (TimelineQuery.FindClip(_vm.Session.Current, clip.Id)?.Clip as MediaClip)?.Audio.Keys ?? ImmutableArray<VolumeKey>.Empty;
        _log.Write("  keys: " + string.Join(", ", keys.Select(k => string.Create(CultureInfo.InvariantCulture, $"{k.Frame}:{k.Db}"))));
    }

    // panel media|effects: the media panel's MEDIA and EFFECTS tabs.
    private void Panel(SmokeStep step)
    {
        var bin = _vm.MediaBin;
        switch (Arg(step, 0).ToLowerInvariant())
        {
            case "media":
                bin.ShowMediaTabCommand.Execute(null);
                Expect(!bin.IsEffectsTab, "the MEDIA tab did not open");
                break;
            case "effects":
                bin.ShowEffectsTabCommand.Execute(null);
                Expect(bin.IsEffectsTab, "the EFFECTS tab did not open");
                break;
            case "agent":
                bin.ShowAgentTabPageCommand.Execute(null);
                Expect(bin.IsAgentTab, "the AGENT tab did not open");
                break;
            default:
                throw Fail("panel takes media, effects or agent");
        }

        _log.Write("media panel shows " + Arg(step, 0).ToLowerInvariant());
    }

    // expect-section <key prefix> [header=<text>]: the Inspector shows a section such as clip, title, transition, gap.
    private void ExpectSection(SmokeStep step)
    {
        var prefix = Arg(step, 0);
        var section = _vm.Inspector.Section;
        Expect(section is not null && section.Key.StartsWith(prefix, StringComparison.Ordinal), "the Inspector shows " + (section?.Key ?? "nothing") + ", expected " + prefix);
        if (step.Option("header") is { } header)
        {
            Expect(string.Equals(section!.Header, header, StringComparison.Ordinal), "the Inspector header is " + section.Header + ", expected " + header);
        }

        _log.Write("Inspector: " + section!.Header + " (" + string.Join(", ", section.Groups.Select(g => g.Title)) + ")");
    }
}
