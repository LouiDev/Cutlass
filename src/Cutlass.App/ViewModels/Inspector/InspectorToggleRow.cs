using CommunityToolkit.Mvvm.ComponentModel;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels.Inspector;

/// <summary>A check box row (enabled, title box). Null shows the indeterminate state of a mixed selection.</summary>
public sealed partial class InspectorToggleRow : InspectorRow
{
    private readonly InspectorEditor _editor;
    private readonly Func<bool, Func<Project, EditResult>?> _makeEdit;

    /// <summary>Creates the row.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="label">The row label.</param>
    /// <param name="coalesceKey">The coalesce key.</param>
    /// <param name="makeEdit">Builds the edit for a new state.</param>
    public InspectorToggleRow(InspectorEditor editor, string label, string coalesceKey, Func<bool, Func<Project, EditResult>?> makeEdit)
        : base(label)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(makeEdit);
        _editor = editor;
        _makeEdit = makeEdit;
        CoalesceKey = coalesceKey;
    }

    /// <summary>The coalesce key.</summary>
    public string CoalesceKey { get; }

    /// <summary>A tooltip for the check box, for example the shortcut.</summary>
    public string? ToolTip { get; init; }

    /// <summary>The state (null when mixed).</summary>
    [ObservableProperty]
    public partial bool? IsChecked { get; set; }

    /// <summary>Loads the states of the selected items: all equal shows that state, otherwise indeterminate.</summary>
    /// <param name="values">The states.</param>
    public void LoadMany(IEnumerable<bool> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var distinct = values.Distinct().Take(2).ToList();
        RunLoading(() => IsChecked = distinct.Count == 1 ? distinct[0] : null);
    }

    partial void OnIsCheckedChanged(bool? value)
    {
        if (!Loading && value is { } state && _makeEdit(state) is { } edit)
        {
            _editor.Apply(edit, CoalesceKey);
        }
    }
}
