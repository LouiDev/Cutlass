using System.Globalization;
using System.Windows.Input;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The inspector with nothing selected (DESIGN 4.4): the sequence settings. They are editable in place while the
/// sequence has never held a clip; afterwards they show read-only and "Change..." opens the sequence settings dialog,
/// which warns about rate conversion. Audio is fixed at 48 kHz stereo. The model has no sequence name, so none shows.
/// </summary>
public sealed partial class SequenceSectionViewModel : InspectorSection
{
    /// <summary>Creates the section.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="states">The remembered expanded states.</param>
    /// <param name="openSettings">Opens the sequence settings dialog.</param>
    public SequenceSectionViewModel(InspectorEditor editor, IDictionary<string, bool> states, ICommand openSettings)
        : base(editor, states, "sequence", "SEQUENCE")
    {
        ArgumentNullException.ThrowIfNull(openSettings);
        var group = AddGroup("SEQUENCE");
        PresetRow = group.Add(new InspectorChoiceRow(editor, "Frame size", "inspector:sequence:size", v => v is SequenceSizePreset { Width: > 0 } preset
            ? p => TimelineEdits.SetSequenceSettings(p, p.Sequence.Settings with { Width = preset.Width, Height = preset.Height })
            : null));
        PresetRow.SetOptions(SequenceSizePreset.All.Select(p => new InspectorChoice(p, p.Name)));
        WidthRow = group.Add(new InspectorNumberField(editor, "Width", "inspector:sequence:width", "Sequence Size", v =>
            p => TimelineEdits.SetSequenceSettings(p, p.Sequence.Settings with { Width = (int)v }))
        {
            Minimum = 16,
            Maximum = 16384,
            Step = 2,
            Unit = "px",
        });
        HeightRow = group.Add(new InspectorNumberField(editor, "Height", "inspector:sequence:height", "Sequence Size", v =>
            p => TimelineEdits.SetSequenceSettings(p, p.Sequence.Settings with { Height = (int)v }))
        {
            Minimum = 16,
            Maximum = 16384,
            Step = 2,
            Unit = "px",
        });
        RateRow = group.Add(new InspectorChoiceRow(editor, "Frame rate", "inspector:sequence:rate", v => v is FrameRate rate
            ? p => TimelineEdits.SetSequenceSettings(p, p.Sequence.Settings with { Rate = rate })
            : null));
        RateRow.SetOptions(SequenceSettingsDialogViewModel.RateChoices());
        SizeRow = group.Add(new InspectorInfoRow("Frame size"));
        RateInfoRow = group.Add(new InspectorInfoRow("Frame rate"));
        group.Add(new InspectorInfoRow("Audio") { Text = "48 kHz stereo" });
        DurationRow = group.Add(new InspectorInfoRow("Duration"));
        ChangeRow = group.Add(new InspectorButtonRow(string.Empty, "Change...", openSettings) { ToolTip = "Change the sequence format" });
        Hint = group.Add(new InspectorNoticeRow(isDanger: false));
    }

    /// <summary>The size preset (editable while the sequence is unused).</summary>
    public InspectorChoiceRow PresetRow { get; }

    /// <summary>The width (editable while the sequence is unused).</summary>
    public InspectorNumberField WidthRow { get; }

    /// <summary>The height (editable while the sequence is unused).</summary>
    public InspectorNumberField HeightRow { get; }

    /// <summary>The frame rate (editable while the sequence is unused).</summary>
    public InspectorChoiceRow RateRow { get; }

    /// <summary>The read-only frame size.</summary>
    public InspectorInfoRow SizeRow { get; }

    /// <summary>The read-only frame rate.</summary>
    public InspectorInfoRow RateInfoRow { get; }

    /// <summary>The sequence duration.</summary>
    public InspectorInfoRow DurationRow { get; }

    /// <summary>The Change... button (after the first clip was placed).</summary>
    public InspectorButtonRow ChangeRow { get; }

    /// <summary>The hint under the settings.</summary>
    public InspectorNoticeRow Hint { get; }

    /// <summary>True while the settings can be edited in place.</summary>
    public bool IsEditable { get; private set; }

    /// <inheritdoc />
    public override void Refresh(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var settings = project.Sequence.Settings;
        IsEditable = !project.Sequence.HasHadClips;
        PresetRow.IsVisible = IsEditable;
        WidthRow.IsVisible = IsEditable;
        HeightRow.IsVisible = IsEditable;
        RateRow.IsVisible = IsEditable;
        SizeRow.IsVisible = !IsEditable;
        RateInfoRow.IsVisible = !IsEditable;
        ChangeRow.IsVisible = !IsEditable;
        PresetRow.Load(SequenceSizePreset.Find(settings.Width, settings.Height));
        WidthRow.Load(settings.Width);
        HeightRow.Load(settings.Height);
        RateRow.Load(settings.Rate);
        SizeRow.Text = string.Create(CultureInfo.InvariantCulture, $"{settings.Width} x {settings.Height}");
        RateInfoRow.Text = settings.Rate.DisplayName + " fps";
        DurationRow.Text = Timecode.Format(TimelineQuery.Duration(project), settings.Rate);
        Hint.Text = IsEditable
            ? "Until a clip is placed, the first video you add sets the size and rate."
            : "Select a clip to edit its properties.";
    }
}
