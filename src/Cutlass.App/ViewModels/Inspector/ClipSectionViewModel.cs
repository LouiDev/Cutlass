using System.Collections.Immutable;
using System.Globalization;
using System.Windows.Input;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels.Inspector;

/// <summary>
/// The inspector for media clips (DESIGN 4.4): Clip (name, file, enabled, start, duration, source in and out, speed),
/// Transform, Color, Effects, Chroma Key, Fades, Audio and Audio Effects. With several clips selected only fields they
/// share show, and an edit applies to all of them as one undo step: the video groups act on the selected video clips and
/// the audio groups on the selected audio clips, so a linked video and audio pair shows both. Selected titles and generators take part in the Clip rows
/// only (an all-generator selection gets <see cref="GeneratorSectionViewModel"/>). The property groups live in
/// <see cref="InspectorPropsSectionBase"/>, shared with the generator section.
/// </summary>
public sealed partial class ClipSectionViewModel : InspectorPropsSectionBase
{
    private readonly ImmutableArray<string> _sourceIds;
    private readonly ImmutableArray<string> _freezeIds;
    private readonly InspectorGroup _clipGroup;
    private readonly InspectorInfoRow _fileRow;
    private readonly InspectorTimecodeField _sourceInRow;
    private readonly InspectorTimecodeField _sourceOutRow;
    private readonly InspectorButtonRow _speedRow;

    /// <summary>Creates the section.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="states">The remembered expanded states.</param>
    /// <param name="project">The project the selection was made in.</param>
    /// <param name="clipIds">The selected clip ids (existing, sorted).</param>
    /// <param name="openSpeedDialog">Opens the speed dialog (Ctrl+R).</param>
    public ClipSectionViewModel(
        InspectorEditor editor, IDictionary<string, bool> states, Project project, IReadOnlyList<string> clipIds, ICommand openSpeedDialog)
        : base(editor, states, "clip:" + string.Join(",", clipIds), Caption(clipIds.Count), clipIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(openSpeedDialog);
        var clips = ClipsOf(project);
        // Clips with a source range once they play (frozen ones included: the toggle can unfreeze them in place, so
        // Refresh decides the Source rows' visibility from the current freeze flags).
        _sourceIds = [.. clips.OfType<MediaClip>().Where(m => TimelineQuery.IsBounded(m with { Freeze = false }, TimelineQuery.FindMedia(project, m.MediaId))).Select(m => m.Id)];
        _freezeIds = [.. clips.OfType<MediaClip>().Where(m => m.Stream == StreamKind.Video && !IsStill(project, m)).Select(m => m.Id)];

        _clipGroup = AddGroup("CLIP");
        _clipGroup.Add(NameRow);
        _fileRow = _clipGroup.Add(new InspectorInfoRow("File"));
        _clipGroup.Add(EnabledRow);
        _clipGroup.Add(StartRow);
        _clipGroup.Add(DurationRow);
        _sourceInRow = _clipGroup.Add(new InspectorTimecodeField(editor, "Source in", Key2(_sourceIds, "clip.sourceIn"), SourceInEdit));
        _sourceOutRow = _clipGroup.Add(new InspectorTimecodeField(editor, "Source out", Key2(_sourceIds, "clip.sourceOut"), SourceOutEdit));
        _speedRow = _clipGroup.Add(new InspectorButtonRow("Speed", "100%", openSpeedDialog) { ToolTip = "Speed (Ctrl+R)" });
        // Effects batch 4: the freeze flag of the selected video clips (stills are always a single frame, so they hide it).
        // It writes only to those clips, never to a still in a mixed selection.
        FreezeRow = _clipGroup.Add(new InspectorToggleRow(editor, "Freeze frame", Key2(_freezeIds, "clip.freeze"), on => UpdateFreeze(on))
        {
            ToolTip = "Holds the source in point for the whole clip (Shift+F inserts a hold at the playhead)",
        });

        // The property groups act on the media clips only, as before generators existed: a generator in a mixed
        // selection takes part in the Clip rows alone.
        AddPropsGroups(
            [.. clips.OfType<MediaClip>().Where(m => m.Stream == StreamKind.Video).Select(m => m.Id)],
            [.. clips.OfType<MediaClip>().Where(m => m.Stream == StreamKind.Audio).Select(m => m.Id)]);

        _sourceInRow.IsVisible = clips.OfType<MediaClip>().Any(m => !m.Freeze && _sourceIds.Contains(m.Id));
        _sourceOutRow.IsVisible = _sourceInRow.IsVisible;
        FreezeRow.IsVisible = !_freezeIds.IsEmpty;
        _fileRow.IsVisible = clips.Length == 1 && clips[0] is MediaClip;
    }

    /// <summary>The speed button row.</summary>
    public InspectorButtonRow SpeedRow => _speedRow;

    /// <summary>The Freeze frame toggle (video media clips that are not stills).</summary>
    public InspectorToggleRow FreezeRow { get; }

    /// <summary>The source in row.</summary>
    public InspectorTimecodeField SourceInRow => _sourceInRow;

    /// <summary>The source out row.</summary>
    public InspectorTimecodeField SourceOutRow => _sourceOutRow;

    /// <inheritdoc />
    public override void Refresh(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var clips = ClipsOf(project);
        var rate = project.Sequence.Settings.Rate;
        RefreshClipRows(project, clips);
        _fileRow.Text = clips.Length == 1 && clips[0] is MediaClip single
            ? TimelineQuery.FindMedia(project, single.MediaId)?.Path ?? "(missing media)"
            : string.Empty;

        // Frozen clips have no source range; the rows hide when every source clip is frozen (or a still), and the
        // section is reused when the Freeze frame toggle changes that in place, so it is decided on every refresh.
        var sources = clips.OfType<MediaClip>().Where(m => !m.Freeze && _sourceIds.Contains(m.Id)).ToList();
        var ins = sources.Select(m => SourceInFrame(m, rate)).Distinct().ToList();
        var outs = sources.Select(m => SourceOutFrame(m, rate)).Distinct().ToList();
        _sourceInRow.Load(ins.Count == 1 ? ins[0] : null, rate);
        _sourceOutRow.Load(outs.Count == 1 ? outs[0] : null, rate);
        _sourceInRow.IsVisible = sources.Count > 0;
        _sourceOutRow.IsVisible = sources.Count > 0;
        // Stills and frozen clips have no speed; the row hides when the selection holds nothing else (frozen clips can
        // change in place through the Freeze frame toggle, so this is decided on every refresh).
        var moving = clips.OfType<MediaClip>().Where(m => !IsStill(project, m) && !m.Freeze).ToList();
        var speeds = moving.Select(m => m.SpeedPercent).Distinct().ToList();
        _speedRow.ButtonText = speeds.Count == 1 ? speeds[0].ToString(CultureInfo.InvariantCulture) + "%" : "Mixed";
        _speedRow.IsVisible = moving.Count > 0;
        FreezeRow.LoadMany(clips.OfType<MediaClip>().Where(m => _freezeIds.Contains(m.Id)).Select(m => m.Freeze));

        RefreshProps(project, clips);
    }

    /// <summary>The source in point shown for a clip, in sequence frames of source time.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The frame.</returns>
    public static long SourceInFrame(MediaClip clip, FrameRate rate)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return TimeMath.FloorDiv(clip.SourceInFlicks, rate.FlicksPerFrame);
    }

    /// <summary>The source out point (exclusive) shown for a clip: source in plus the duration at the clip's speed.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The frame.</returns>
    public static long SourceOutFrame(MediaClip clip, FrameRate rate)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return SourceInFrame(clip, rate) + (long)Math.Round(clip.Duration * clip.SpeedPercent / 100.0, MidpointRounding.AwayFromZero);
    }

    private static string Caption(int count) =>
        count == 1 ? "CLIP" : string.Create(CultureInfo.InvariantCulture, $"{count} CLIPS");

    private static bool IsStill(Project project, MediaClip clip) =>
        TimelineQuery.FindMedia(project, clip.MediaId) is { Info.IsStill: true };

    private Func<Project, EditResult> UpdateFreeze(bool on) =>
        p => TimelineEdits.UpdateClips(p, _freezeIds, c => c is MediaClip m ? m with { Freeze = on } : c, on ? "Freeze Frame" : "Unfreeze");

    private Func<Project, EditResult>? SourceInEdit(long frame) => frame < 0 ? null : p =>
        Chain(p, "Source In", _sourceIds, (q, id) =>
        {
            if (TimelineQuery.FindClip(q, id)?.Clip is not MediaClip m || m.Freeze)
            {
                return null;
            }

            var deltaSource = frame - SourceInFrame(m, q.Sequence.Settings.Rate);
            // Slip takes timeline frames at the clip's speed; the field shows source frames, so convert.
            var delta = (long)Math.Round(deltaSource * 100.0 / m.SpeedPercent, MidpointRounding.AwayFromZero);
            return delta == 0 ? null : TimelineEdits.Slip(q, id, delta, includeLinked: false);
        });

    private Func<Project, EditResult>? SourceOutEdit(long frame) => p =>
        Chain(p, "Source Out", _sourceIds, (q, id) =>
        {
            if (TimelineQuery.FindClip(q, id)?.Clip is not MediaClip m || m.Freeze)
            {
                return null;
            }

            var sourceFrames = frame - SourceInFrame(m, q.Sequence.Settings.Rate);
            var duration = Math.Max(1, (long)Math.Round(sourceFrames * 100.0 / m.SpeedPercent, MidpointRounding.AwayFromZero));
            return duration == m.Duration
                ? null
                : TimelineEdits.Trim(q, id, ClipEdge.End, duration - m.Duration, ripple: false, includeLinked: false);
        });
}
