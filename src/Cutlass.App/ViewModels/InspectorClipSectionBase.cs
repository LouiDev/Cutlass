using System.Collections.Immutable;
using System.Globalization;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels;

/// <summary>
/// Shared rows of the clip and title sections: name, enabled, start and duration. Start moves the whole selection
/// (its earliest clip lands on the typed frame); duration trims the end of every selected clip to the typed length.
/// </summary>
public abstract class InspectorClipSectionBase : InspectorSection
{
    /// <summary>Creates the section.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="states">The remembered expanded states.</param>
    /// <param name="key">The selection identity.</param>
    /// <param name="header">The header.</param>
    /// <param name="clipIds">The selected clip ids.</param>
    protected InspectorClipSectionBase(
        InspectorEditor editor, IDictionary<string, bool> states, string key, string header, IReadOnlyList<string> clipIds)
        : base(editor, states, key, header)
    {
        ArgumentNullException.ThrowIfNull(clipIds);
        ClipIds = [.. clipIds];
        NameRow = new InspectorInfoRow("Name");
        EnabledRow = new InspectorToggleRow(
            editor, "Enabled", InspectorEditor.CoalesceKey(ClipIds, "clip.enabled"), on => p => TimelineEdits.SetEnabled(p, ClipIds, on))
        {
            ToolTip = "Shift+E",
        };
        StartRow = new InspectorTimecodeField(editor, "Start", InspectorEditor.CoalesceKey(ClipIds, "clip.start"), StartEdit);
        DurationRow = new InspectorTimecodeField(editor, "Duration", InspectorEditor.CoalesceKey(ClipIds, "clip.duration"), DurationEdit);
    }

    /// <summary>The selected clip ids.</summary>
    public ImmutableArray<string> ClipIds { get; }

    /// <summary>The clip name (file name or first title line), or "N clips".</summary>
    public InspectorInfoRow NameRow { get; }

    /// <summary>Enabled (Shift+E).</summary>
    public InspectorToggleRow EnabledRow { get; }

    /// <summary>The start of the selection (its earliest clip).</summary>
    public InspectorTimecodeField StartRow { get; }

    /// <summary>The duration (empty when the selected clips differ).</summary>
    public InspectorTimecodeField DurationRow { get; }

    /// <summary>The selected clips that still exist, in selection order.</summary>
    /// <param name="project">The project.</param>
    /// <returns>The clips.</returns>
    protected ImmutableArray<Clip> ClipsOf(Project project) =>
        [.. ClipIds.Select(id => TimelineQuery.FindClip(project, id)?.Clip).OfType<Clip>()];

    /// <summary>Reloads name, enabled, start and duration.</summary>
    /// <param name="project">The project.</param>
    /// <param name="clips">The selected clips.</param>
    protected void RefreshClipRows(Project project, IReadOnlyList<Clip> clips)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(clips);
        var rate = project.Sequence.Settings.Rate;
        NameRow.Text = clips.Count == 1
            ? TimelineQuery.DisplayName(project, clips[0])
            : string.Create(CultureInfo.InvariantCulture, $"{clips.Count} clips");
        EnabledRow.LoadMany(clips.Select(c => c.Enabled));
        StartRow.Load(clips.Count == 0 ? null : clips.Min(c => c.Start), rate);
        var durations = clips.Select(c => c.Duration).Distinct().ToList();
        DurationRow.Load(durations.Count == 1 ? durations[0] : null, rate);
    }

    private Func<Project, EditResult>? StartEdit(long frame) => frame < 0 ? null : p =>
    {
        var clips = ClipsOf(p);
        var delta = clips.IsEmpty ? 0 : frame - clips.Min(c => c.Start);
        return delta == 0
            ? EditResult.Unchanged(p, "Move")
            : TimelineEdits.Move(Editor.Timeline.Ids, p, ClipIds, delta, 0, EditMode.Overwrite);
    };

    private Func<Project, EditResult>? DurationEdit(long frames) => frames < 1 ? null : p =>
        Chain(p, "Duration", ClipIds, (q, id) => TimelineQuery.FindClip(q, id) is { } found && found.Clip.Duration != frames
            ? TimelineEdits.Trim(q, id, ClipEdge.End, frames - found.Clip.Duration, ripple: false, includeLinked: false)
            : null);
}
