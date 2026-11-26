using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels.Inspector;

/// <summary>A combo box row (font, anchor, transition kind, frame size, frame rate).</summary>
public sealed partial class InspectorChoiceRow : InspectorRow
{
    private readonly InspectorEditor _editor;
    private readonly Func<object, Func<Project, EditResult>?> _makeEdit;

    /// <summary>Creates the row.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="label">The row label.</param>
    /// <param name="coalesceKey">The coalesce key.</param>
    /// <param name="makeEdit">Builds the edit for a chosen <see cref="InspectorChoice.Value"/>.</param>
    public InspectorChoiceRow(InspectorEditor editor, string label, string coalesceKey, Func<object, Func<Project, EditResult>?> makeEdit)
        : base(label)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(makeEdit);
        _editor = editor;
        _makeEdit = makeEdit;
        CoalesceKey = coalesceKey;
        IsEnabled = true;
    }

    /// <summary>The coalesce key.</summary>
    public string CoalesceKey { get; }

    /// <summary>The options.</summary>
    public ObservableCollection<InspectorChoice> Options { get; } = [];

    /// <summary>The selected option, or null when the selection's values differ.</summary>
    [ObservableProperty]
    public partial InspectorChoice? Selected { get; set; }

    /// <summary>False shows the combo box disabled (an audio transition has one kind only).</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <summary>Replaces the options without touching the session, keeping the selection by value.</summary>
    /// <param name="options">The new options.</param>
    public void SetOptions(IEnumerable<InspectorChoice> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var list = options.ToList();
        if (list.SequenceEqual(Options))
        {
            return;
        }

        var value = Selected?.Value;
        RunLoading(() =>
        {
            Options.Clear();
            foreach (var option in list)
            {
                Options.Add(option);
            }

            Selected = value is null ? null : Options.FirstOrDefault(o => Equals(o.Value, value));
        });
    }

    /// <summary>Selects the option whose value equals <paramref name="value"/> (null or unknown clears it).</summary>
    /// <param name="value">The model value.</param>
    public void Load(object? value) =>
        RunLoading(() => Selected = value is null ? null : Options.FirstOrDefault(o => Equals(o.Value, value)));

    partial void OnSelectedChanged(InspectorChoice? value)
    {
        if (!Loading && value is not null && _makeEdit(value.Value) is { } edit)
        {
            _editor.Apply(edit, CoalesceKey);
        }
    }
}
