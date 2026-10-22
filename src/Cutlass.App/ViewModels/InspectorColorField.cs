using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels;

/// <summary>
/// A colour row of a title (text, outline, box): a swatch and the model's <c>#RRGGBBAA</c> text. Typed colours accept
/// <c>#RGB</c>, <c>#RRGGBB</c> and <c>#RRGGBBAA</c>, with or without the <c>#</c>.
/// </summary>
public sealed partial class InspectorColorField : InspectorRow
{
    private readonly InspectorEditor _editor;
    private readonly Func<string, Func<Project, EditResult>?> _makeEdit;
    private string _loaded = string.Empty;

    /// <summary>Creates the row.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="label">The row label.</param>
    /// <param name="coalesceKey">The coalesce key.</param>
    /// <param name="makeEdit">Builds the edit for a normalized <c>#RRGGBBAA</c> colour.</param>
    public InspectorColorField(InspectorEditor editor, string label, string coalesceKey, Func<string, Func<Project, EditResult>?> makeEdit)
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

    /// <summary>The colour text, <c>#RRGGBBAA</c> (empty when mixed).</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>The swatch colour.</summary>
    [ObservableProperty]
    public partial Color Swatch { get; set; }

    /// <summary>
    /// Normalizes a typed colour to upper-case <c>#RRGGBBAA</c>: <c>#RGB</c> doubles each digit, six digits get an opaque
    /// alpha. Returns null for anything else.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The colour, or null.</returns>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var s = text.Trim().TrimStart('#');
        if (s.Length == 3)
        {
            s = string.Concat(s.Select(c => new string(c, 2)));
        }

        if (s.Length == 6)
        {
            s += "FF";
        }

        if (s.Length != 8 || !s.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        return "#" + s.ToUpperInvariant();
    }

    /// <summary>The Avalonia colour of a model <c>#RRGGBBAA</c> text (transparent when invalid).</summary>
    /// <param name="rgba">The model colour.</param>
    /// <returns>The colour.</returns>
    public static Color ToColor(string? rgba)
    {
        if (Normalize(rgba) is not { } n)
        {
            return Colors.Transparent;
        }

        var value = uint.Parse(n.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromArgb((byte)(value & 0xFF), (byte)(value >> 24), (byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF));
    }

    /// <summary>Loads the colours of the selected titles (different colours show empty).</summary>
    /// <param name="values">The model colours.</param>
    public void LoadMany(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var distinct = values.Select(v => Normalize(v) ?? v).Distinct(StringComparer.Ordinal).Take(2).ToList();
        _loaded = distinct.Count == 1 ? distinct[0] : string.Empty;
        RunLoading(() =>
        {
            if (Text == _loaded)
            {
                OnPropertyChanged(nameof(Text));
            }

            Text = _loaded;
            Swatch = ToColor(_loaded);
        });
    }

    partial void OnTextChanged(string value)
    {
        if (Loading)
        {
            return;
        }

        if (Normalize(value) is not { } color || _makeEdit(color) is not { } edit)
        {
            RunLoading(() => Text = _loaded);
            return;
        }

        _editor.Apply(edit, CoalesceKey);
    }
}
