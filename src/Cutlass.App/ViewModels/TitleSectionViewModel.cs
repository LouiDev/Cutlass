using System.Collections.Immutable;
using System.Globalization;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The inspector for title clips (DESIGN 4.4): text (multi-line), font (bundled Inter Regular, SemiBold, Bold or a
/// system font file), size, colour, outline width and colour, box (on/off, colour, padding), position preset with an
/// X/Y offset, opacity and fades. Several selected titles share every field; an edit applies to all of them.
/// </summary>
public sealed partial class TitleSectionViewModel : InspectorClipSectionBase
{
    /// <summary>The bundled font references, in menu order.</summary>
    public static readonly ImmutableArray<InspectorChoice> BundledFonts =
    [
        new("builtin:Inter-Regular", "Inter Regular"),
        new("builtin:Inter-SemiBold", "Inter SemiBold"),
        new("builtin:Inter-Bold", "Inter Bold"),
    ];

    private readonly List<InspectorNumberField> _numbers = [];
    private IReadOnlyList<string> _systemFonts;
    private string? _currentFont;

    /// <summary>Creates the section.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="states">The remembered expanded states.</param>
    /// <param name="clipIds">The selected title ids.</param>
    /// <param name="systemFonts">Font file paths the shell found on the system (may be empty).</param>
    public TitleSectionViewModel(InspectorEditor editor, IDictionary<string, bool> states, IReadOnlyList<string> clipIds, IReadOnlyList<string> systemFonts)
        : base(editor, states, "title:" + string.Join(",", clipIds), clipIds.Count == 1 ? "TITLE" : string.Create(CultureInfo.InvariantCulture, $"{clipIds.Count} TITLES"), clipIds)
    {
        _systemFonts = systemFonts ?? [];
        var clip = AddGroup("CLIP");
        clip.Add(NameRow);
        clip.Add(EnabledRow);
        clip.Add(StartRow);
        clip.Add(DurationRow);

        var text = AddGroup("TEXT", () => Run(Update("Reset Text Style", t => t with
        {
            Title = t.Title with
            {
                Font = TitleSpec.Default.Font,
                Size = TitleSpec.Default.Size,
                Color = TitleSpec.Default.Color,
                OutlineWidth = TitleSpec.Default.OutlineWidth,
                OutlineColor = TitleSpec.Default.OutlineColor,
            },
        })));
        TextRow = text.Add(new InspectorTextRow(editor, "Text", FieldKey("title.text"), s => Update("Title Text", t => t with { Title = t.Title with { Text = s } })));
        FontRow = text.Add(new InspectorChoiceRow(editor, "Font", FieldKey("title.font"), v => v is string font
            ? Update("Font", t => t with { Title = t.Title with { Font = font } })
            : null));
        Number(text, "Size", "title.size", "Font Size", s => s.Size, (s, v) => s with { Size = v }, TitleSpec.Default.Size, 1, 2000, 0.5, 0, "px");
        TextColorRow = text.Add(new InspectorColorField(editor, "Color", FieldKey("title.color"), c => Update("Text Color", t => t with { Title = t.Title with { Color = c } })));
        Number(text, "Outline", "title.outlineWidth", "Outline", s => s.OutlineWidth, (s, v) => s with { OutlineWidth = v }, TitleSpec.Default.OutlineWidth, 0, 100, 0.1, 1, "px");
        OutlineColorRow = text.Add(new InspectorColorField(editor, "Outline color", FieldKey("title.outlineColor"), c => Update("Outline Color", t => t with { Title = t.Title with { OutlineColor = c } })));

        var box = AddGroup("BOX", () => Run(Update("Reset Box", t => t with
        {
            Title = t.Title with { Box = false, BoxColor = TitleSpec.Default.BoxColor, BoxPadding = TitleSpec.Default.BoxPadding },
        })));
        BoxRow = box.Add(new InspectorToggleRow(editor, "Box", FieldKey("title.box"), on => Update("Title Box", t => t with { Title = t.Title with { Box = on } })));
        BoxColorRow = box.Add(new InspectorColorField(editor, "Box color", FieldKey("title.boxColor"), c => Update("Box Color", t => t with { Title = t.Title with { BoxColor = c } })));
        Number(box, "Padding", "title.boxPadding", "Box Padding", s => s.BoxPadding, (s, v) => s with { BoxPadding = v }, TitleSpec.Default.BoxPadding, 0, 1000, 0.5, 0, "px");

        var position = AddGroup("POSITION", () => Run(Update("Reset Position", t => t with { Title = t.Title with { OffsetX = 0, OffsetY = 0 } })));
        AnchorRow = position.Add(new InspectorChoiceRow(editor, "Preset", FieldKey("title.anchor"), v => v is TitleAnchor anchor
            ? Update("Title Position", t => t with { Title = t.Title with { Anchor = anchor } })
            : null));
        AnchorRow.SetOptions(
        [
            new(TitleAnchor.Center, "Center"),
            new(TitleAnchor.LowerThird, "Lower Third"),
            new(TitleAnchor.Top, "Top"),
        ]);
        Number(position, "Offset X", "title.offsetX", "Title Offset", s => s.OffsetX, (s, v) => s with { OffsetX = v }, 0, -100_000, 100_000, 1, 0, "px");
        Number(position, "Offset Y", "title.offsetY", "Title Offset", s => s.OffsetY, (s, v) => s with { OffsetY = v }, 0, -100_000, 100_000, 1, 0, "px");

        var look = AddGroup("OPACITY", () => Run(Update("Reset Opacity", t => t with { Opacity = 1, FadeInFrames = 0, FadeOutFrames = 0 })));
        Clip(look, "Opacity", "title.opacity", "Opacity", t => t.Opacity * 100, (t, v) => t with { Opacity = v / 100 }, 100, 0, 100, 0.5, 0, "%");
        Clip(look, "Fade in", "title.fadeIn", "Fade", t => t.FadeInFrames, (t, v) => t with { FadeInFrames = (long)v }, 0, 0, 1_000_000, 0.25, 0, "f");
        Clip(look, "Fade out", "title.fadeOut", "Fade", t => t.FadeOutFrames, (t, v) => t with { FadeOutFrames = (long)v }, 0, 0, 1_000_000, 0.25, 0, "f");
    }

    /// <summary>The title text.</summary>
    public InspectorTextRow TextRow { get; }

    /// <summary>The font choice.</summary>
    public InspectorChoiceRow FontRow { get; }

    /// <summary>The text colour.</summary>
    public InspectorColorField TextColorRow { get; }

    /// <summary>The outline colour.</summary>
    public InspectorColorField OutlineColorRow { get; }

    /// <summary>The box toggle.</summary>
    public InspectorToggleRow BoxRow { get; }

    /// <summary>The box colour.</summary>
    public InspectorColorField BoxColorRow { get; }

    /// <summary>The position preset.</summary>
    public InspectorChoiceRow AnchorRow { get; }

    /// <summary>Every numeric field.</summary>
    public IReadOnlyList<InspectorNumberField> NumberFields => _numbers;

    /// <summary>Finds a numeric field by label.</summary>
    /// <param name="label">The label, for example "Size".</param>
    /// <returns>The field.</returns>
    public InspectorNumberField Field(string label) => _numbers.First(f => f.Label == label);

    /// <summary>The font menu: the bundled fonts, the system fonts and, when it is neither, the current font.</summary>
    /// <param name="systemFonts">Font file paths.</param>
    /// <param name="current">The current font reference, or null.</param>
    /// <returns>The choices.</returns>
    public static IReadOnlyList<InspectorChoice> FontChoices(IReadOnlyList<string> systemFonts, string? current)
    {
        ArgumentNullException.ThrowIfNull(systemFonts);
        var list = new List<InspectorChoice>(BundledFonts);
        foreach (var path in systemFonts.Distinct(StringComparer.Ordinal))
        {
            list.Add(new InspectorChoice(path, Path.GetFileNameWithoutExtension(path)));
        }

        if (current is not null && list.All(c => !Equals(c.Value, current)))
        {
            var name = current.StartsWith("builtin:", StringComparison.Ordinal) ? current["builtin:".Length..] : Path.GetFileNameWithoutExtension(current);
            list.Add(new InspectorChoice(current, name));
        }

        return list;
    }

    /// <summary>Replaces the system fonts of the font menu (the shell lists them in the background).</summary>
    /// <param name="systemFonts">Font file paths.</param>
    public void SetFonts(IReadOnlyList<string> systemFonts)
    {
        _systemFonts = systemFonts ?? [];
        FontRow.SetOptions(FontChoices(_systemFonts, _currentFont));
    }

    /// <inheritdoc />
    public override void Refresh(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var clips = ClipsOf(project);
        RefreshClipRows(project, clips);
        var titles = clips.OfType<TitleClip>().ToList();
        var specs = titles.Select(t => t.Title).ToList();
        var texts = specs.Select(s => s.Text).Distinct(StringComparer.Ordinal).ToList();
        TextRow.Load(texts.Count == 1 ? texts[0] : string.Empty);
        var fonts = specs.Select(s => s.Font).Distinct(StringComparer.Ordinal).ToList();
        _currentFont = fonts.Count == 1 ? fonts[0] : null;
        FontRow.SetOptions(FontChoices(_systemFonts, _currentFont));
        FontRow.Load(_currentFont);
        TextColorRow.LoadMany(specs.Select(s => s.Color));
        OutlineColorRow.LoadMany(specs.Select(s => s.OutlineColor));
        BoxRow.LoadMany(specs.Select(s => s.Box));
        BoxColorRow.LoadMany(specs.Select(s => s.BoxColor));
        var anchors = specs.Select(s => s.Anchor).Distinct().ToList();
        AnchorRow.Load(anchors.Count == 1 ? anchors[0] : null);
        foreach (var field in _numbers)
        {
            field.LoadFrom(titles);
        }
    }

    private string FieldKey(string field) => InspectorEditor.CoalesceKey(ClipIds, field);

    private Func<Project, EditResult> Update(string label, Func<TitleClip, TitleClip> change) =>
        p => TimelineEdits.UpdateClips(p, ClipIds, c => c is TitleClip t ? change(t) : c, label);

    private void Number(
        InspectorGroup group,
        string label,
        string field,
        string undoLabel,
        Func<TitleSpec, double> read,
        Func<TitleSpec, double, TitleSpec> write,
        double defaultValue,
        double min,
        double max,
        double step,
        int decimals,
        string? unit) =>
        Clip(group, label, field, undoLabel, t => read(t.Title), (t, v) => t with { Title = write(t.Title, v) }, defaultValue, min, max, step, decimals, unit);

    private void Clip(
        InspectorGroup group,
        string label,
        string field,
        string undoLabel,
        Func<TitleClip, double> read,
        Func<TitleClip, double, TitleClip> write,
        double defaultValue,
        double min,
        double max,
        double step,
        int decimals,
        string? unit)
    {
        var number = new InspectorNumberField(Editor, label, FieldKey(field), undoLabel, v => Update(undoLabel, t => write(t, v)))
        {
            Read = c => c is TitleClip t ? read(t) : null,
            DefaultValue = defaultValue,
            Minimum = min,
            Maximum = max,
            Step = step,
            Decimals = decimals,
            Unit = unit,
        };
        _numbers.Add(group.Add(number));
    }
}
