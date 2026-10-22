using System.Globalization;
using System.Windows.Input;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels;

/// <summary>The inspector for a selected gap (DESIGN 4.4): its track, start and length, and "Close gap".</summary>
public sealed partial class GapSectionViewModel : InspectorSection
{
    /// <summary>Creates the section.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="states">The remembered expanded states.</param>
    /// <param name="trackId">The gap's track.</param>
    /// <param name="start">The first frame of the gap.</param>
    /// <param name="end">The end of the gap (exclusive).</param>
    /// <param name="closeGap">The timeline's Close Gap command.</param>
    public GapSectionViewModel(InspectorEditor editor, IDictionary<string, bool> states, string trackId, long start, long end, ICommand closeGap)
        : base(editor, states, string.Create(CultureInfo.InvariantCulture, $"gap:{trackId}:{start}:{end}"), "GAP")
    {
        ArgumentNullException.ThrowIfNull(closeGap);
        TrackId = trackId;
        Start = start;
        End = end;
        var group = AddGroup("GAP");
        TrackRow = group.Add(new InspectorInfoRow("Track"));
        StartRow = group.Add(new InspectorInfoRow("Start"));
        LengthRow = group.Add(new InspectorInfoRow("Length"));
        CloseRow = group.Add(new InspectorButtonRow(string.Empty, "Close gap", closeGap) { ToolTip = "Close gap (Delete)" });
    }

    /// <summary>The gap's track.</summary>
    public string TrackId { get; }

    /// <summary>The first frame of the gap.</summary>
    public long Start { get; }

    /// <summary>The end of the gap (exclusive).</summary>
    public long End { get; }

    /// <summary>The track name.</summary>
    public InspectorInfoRow TrackRow { get; }

    /// <summary>The start timecode.</summary>
    public InspectorInfoRow StartRow { get; }

    /// <summary>The length as a timecode and in frames.</summary>
    public InspectorInfoRow LengthRow { get; }

    /// <summary>The Close gap button.</summary>
    public InspectorButtonRow CloseRow { get; }

    /// <inheritdoc />
    public override void Refresh(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var rate = project.Sequence.Settings.Rate;
        TrackRow.Text = TimelineQuery.FindTrack(project, TrackId)?.Name ?? string.Empty;
        StartRow.Text = Timecode.Format(Start, rate);
        LengthRow.Text = Timecode.Format(End - Start, rate) + string.Create(CultureInfo.InvariantCulture, $"  ({End - Start} f)");
    }
}
