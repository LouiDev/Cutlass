using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels.Inspector;

/// <summary>
/// A numeric inspector field shown with a <c>ScrubbyNumberBox</c> (DESIGN 4.4). A value set by typing, clicking or a
/// reset runs one <see cref="InspectorEditor.Apply"/> with <see cref="CoalesceKey"/>; a drag wraps its changes in a
/// session gesture through <see cref="BeginDragCommand"/> and <see cref="EndDragCommand"/>. Values are in display
/// units (percent, dB, frames); <see cref="Read"/> and the edit factory convert to and from the model.
/// </summary>
public sealed partial class InspectorNumberField : InspectorRow
{
    private readonly InspectorEditor _editor;
    private readonly Func<double, Func<Project, EditResult>?> _makeEdit;

    /// <summary>Creates the field.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="label">The row label.</param>
    /// <param name="coalesceKey">The coalesce key of typed changes, for example <c>inspector:c_1:color.gamma</c>.</param>
    /// <param name="undoLabel">The undo label of a drag, for example "Gamma".</param>
    /// <param name="makeEdit">Builds the edit that sets a value (null to ignore it).</param>
    public InspectorNumberField(
        InspectorEditor editor, string label, string coalesceKey, string undoLabel, Func<double, Func<Project, EditResult>?> makeEdit)
        : base(label)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(makeEdit);
        _editor = editor;
        _makeEdit = makeEdit;
        CoalesceKey = coalesceKey;
        UndoLabel = undoLabel;
        Value = double.NaN;
    }

    /// <summary>The coalesce key of typed changes.</summary>
    public string CoalesceKey { get; }

    /// <summary>The undo label of a drag.</summary>
    public string UndoLabel { get; }

    /// <summary>The unit suffix, for example <c>%</c>.</summary>
    public string? Unit { get; init; }

    /// <summary>The smallest value.</summary>
    public double Minimum { get; init; } = -100_000;

    /// <summary>The largest value.</summary>
    public double Maximum { get; init; } = 100_000;

    /// <summary>The change per dragged pixel.</summary>
    public double Step { get; init; } = 1;

    /// <summary>The decimals shown.</summary>
    public int Decimals { get; init; }

    /// <summary>The value a right click restores, or NaN.</summary>
    public double DefaultValue { get; init; } = double.NaN;

    /// <summary>Reads a clip's value in display units, or null when the clip does not have this field.</summary>
    public Func<Clip, double?>? Read { get; init; }

    /// <summary>The value (NaN while the selection's values differ).</summary>
    [ObservableProperty]
    public partial double Value { get; set; }

    /// <summary>True when the selected items have different values.</summary>
    [ObservableProperty]
    public partial bool IsMixed { get; set; }

    /// <summary>Loads one value without touching the session.</summary>
    /// <param name="value">The value.</param>
    public void Load(double value) => RunLoading(() =>
    {
        IsMixed = false;
        Value = value;
    });

    /// <summary>Loads the values of several items: equal values show, different ones show as mixed.</summary>
    /// <param name="values">The values (empty hides nothing and shows a dash).</param>
    public void LoadMany(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var distinct = values.Distinct().Take(2).ToList();
        RunLoading(() =>
        {
            IsMixed = distinct.Count != 1;
            Value = distinct.Count == 1 ? distinct[0] : double.NaN;
        });
    }

    /// <summary>Loads from clips with <see cref="Read"/>.</summary>
    /// <param name="clips">The selected clips.</param>
    public void LoadFrom(IEnumerable<Clip> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        if (Read is { } read)
        {
            LoadMany(clips.Select(read).Where(v => v.HasValue).Select(v => v!.Value));
        }
    }

    partial void OnValueChanged(double value)
    {
        if (Loading || double.IsNaN(value))
        {
            return;
        }

        IsMixed = false;
        if (_makeEdit(value) is { } edit)
        {
            _editor.Apply(edit, CoalesceKey);
        }
    }

    /// <summary>Starts a drag gesture (bound to the box's DragStartedCommand).</summary>
    [RelayCommand]
    private void BeginDrag() => _editor.BeginGesture();

    /// <summary>Ends a drag gesture (bound to the box's DragCompletedCommand); false cancels it.</summary>
    /// <param name="committed">True when the drag committed.</param>
    [RelayCommand]
    private void EndDrag(bool committed) => _editor.EndGesture(committed, UndoLabel);
}
