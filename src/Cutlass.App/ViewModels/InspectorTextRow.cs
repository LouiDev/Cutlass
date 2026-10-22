using CommunityToolkit.Mvvm.ComponentModel;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels;

/// <summary>
/// An editable multi-line text row (the title text). Each change runs one coalesced edit, so typing merges into one
/// undo step while the keys keep coming (TECH_BRIEF 3.4).
/// </summary>
public sealed partial class InspectorTextRow : InspectorRow
{
    private readonly InspectorEditor _editor;
    private readonly Func<string, Func<Project, EditResult>?> _makeEdit;

    /// <summary>Creates the row.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="label">The row label.</param>
    /// <param name="coalesceKey">The coalesce key.</param>
    /// <param name="makeEdit">Builds the edit for a new text.</param>
    public InspectorTextRow(InspectorEditor editor, string label, string coalesceKey, Func<string, Func<Project, EditResult>?> makeEdit)
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

    /// <summary>The text.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>Loads the text without touching the session.</summary>
    /// <param name="text">The text.</param>
    public void Load(string text) => RunLoading(() => Text = text);

    partial void OnTextChanged(string value)
    {
        if (!Loading && _makeEdit(value ?? string.Empty) is { } edit)
        {
            _editor.Apply(edit, CoalesceKey);
        }
    }
}
