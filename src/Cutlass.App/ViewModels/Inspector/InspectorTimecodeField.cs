using CommunityToolkit.Mvvm.ComponentModel;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels.Inspector;

/// <summary>
/// A timecode inspector field (start, duration, source in and out, keyframe time). The text box commits on Enter or
/// when focus leaves; the text is parsed with <see cref="Timecode.TryParse"/> (invariant digits) and turned into an
/// edit. Invalid text, or an edit the model refuses, shows the model's value again.
/// </summary>
public sealed partial class InspectorTimecodeField : InspectorRow
{
    private readonly InspectorEditor _editor;
    private readonly Func<long, Func<Project, EditResult>?>? _makeEdit;
    private FrameRate _rate = FrameRate.Fps30;
    private string _loaded = string.Empty;

    /// <summary>Creates the field.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="label">The row label.</param>
    /// <param name="coalesceKey">The coalesce key, for example <c>inspector:c_1:clip.start</c>.</param>
    /// <param name="makeEdit">Builds the edit for a typed frame, or null for a read-only field.</param>
    public InspectorTimecodeField(InspectorEditor editor, string label, string coalesceKey, Func<long, Func<Project, EditResult>?>? makeEdit)
        : base(label)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
        _makeEdit = makeEdit;
        CoalesceKey = coalesceKey;
        IsReadOnly = makeEdit is null;
    }

    /// <summary>The coalesce key.</summary>
    public string CoalesceKey { get; }

    /// <summary>The timecode text, or empty while the selection's values differ.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>True when the field cannot be typed into.</summary>
    [ObservableProperty]
    public partial bool IsReadOnly { get; set; }

    /// <summary>The placeholder shown for an empty (mixed) field.</summary>
    public string Watermark => "Mixed";

    /// <summary>Loads a frame (null shows the field empty as mixed).</summary>
    /// <param name="frame">The frame, or null.</param>
    /// <param name="rate">The sequence rate.</param>
    public void Load(long? frame, FrameRate rate)
    {
        _rate = rate;
        _loaded = frame is { } f ? Timecode.Format(f, rate) : string.Empty;
        RunLoading(() =>
        {
            // Force a change notification so a refused edit restores the text even when the model value is unchanged.
            if (Text == _loaded)
            {
                OnPropertyChanged(nameof(Text));
            }

            Text = _loaded;
        });
    }

    partial void OnTextChanged(string value)
    {
        if (Loading)
        {
            return;
        }

        if (_makeEdit is null || !Timecode.TryParse(value ?? string.Empty, _rate, out var frame) || _makeEdit(frame) is not { } edit)
        {
            RunLoading(() => Text = _loaded);
            return;
        }

        var before = _editor.Session.Current;
        _editor.Apply(edit, CoalesceKey);
        if (ReferenceEquals(before, _editor.Session.Current))
        {
            RunLoading(() => Text = _loaded);
        }
    }
}
