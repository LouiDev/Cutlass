using System.Collections.Immutable;
using System.Globalization;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The inspector for generator clips (effects batch 5, DESIGN 4.4): Clip (name, enabled, start, duration), Generator
/// (the matte colour, the tone frequency), then for colour mattes and bars the Transform, Color, Effects, Chroma Key and
/// Fades groups and for tones the Audio and Audio Effects groups, all shared with the media clip section. Several
/// selected generators share every field; an edit applies to those it fits as one undo step.
/// </summary>
public sealed class GeneratorSectionViewModel : InspectorPropsSectionBase
{
    private readonly InspectorGroup _generatorGroup;

    /// <summary>Creates the section.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="states">The remembered expanded states.</param>
    /// <param name="project">The project the selection was made in.</param>
    /// <param name="clipIds">The selected generator ids (existing, sorted).</param>
    public GeneratorSectionViewModel(InspectorEditor editor, IDictionary<string, bool> states, Project project, IReadOnlyList<string> clipIds)
        : base(editor, states, "generator:" + string.Join(",", clipIds), Caption(clipIds.Count), clipIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        var generators = ClipsOf(project).OfType<GeneratorClip>().ToList();

        var clip = AddGroup("CLIP");
        clip.Add(NameRow);
        clip.Add(EnabledRow);
        clip.Add(StartRow);
        clip.Add(DurationRow);

        _generatorGroup = AddGroup("GENERATOR", () => Run(Update("Reset Generator", g => g with
        {
            Color = GeneratorClip.DefaultColor,
            ToneHz = GeneratorClip.DefaultToneHz,
        })));
        // The colour field normalizes to #RRGGBBAA; a matte is opaque (its Opacity row is separate), so the model keeps #RRGGBB.
        ColorRow = _generatorGroup.Add(new InspectorColorField(editor, "Color", InspectorEditor.CoalesceKey(ClipIds, "generator.color"), c =>
            Update("Matte Color", g => g.Kind == GeneratorKind.ColorMatte ? g with { Color = c[..7] } : g)));
        FrequencyRow = _generatorGroup.Add(new InspectorNumberField(
            editor,
            "Frequency",
            InspectorEditor.CoalesceKey(ClipIds, "generator.toneHz"),
            "Tone Frequency",
            v => Update("Tone Frequency", g => g.Kind == GeneratorKind.Tone ? g with { ToneHz = v } : g))
        {
            Read = c => c is GeneratorClip { Kind: GeneratorKind.Tone } g ? g.ToneHz : null,
            DefaultValue = GeneratorClip.DefaultToneHz,
            Minimum = GeneratorClip.MinToneHz,
            Maximum = GeneratorClip.MaxToneHz,
            Step = 1,
            Decimals = 0,
            Unit = "Hz",
        });

        // A generator never changes its kind of track, so the sides are fixed for the section's life; the matte and the
        // bars can swap (through the agent), so the Color row is decided in Refresh.
        AddPropsGroups(
            [.. generators.Where(g => g.Kind.IsVideo()).Select(g => g.Id)],
            [.. generators.Where(g => g.Kind.IsAudio()).Select(g => g.Id)]);
    }

    /// <summary>The GENERATOR group.</summary>
    public InspectorGroup GeneratorGroup => _generatorGroup;

    /// <summary>The matte colour (visible while a colour matte is selected).</summary>
    public InspectorColorField ColorRow { get; }

    /// <summary>The tone frequency in Hz (visible while a tone is selected).</summary>
    public InspectorNumberField FrequencyRow { get; }

    /// <inheritdoc />
    public override void Refresh(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var clips = ClipsOf(project);
        RefreshClipRows(project, clips);
        var generators = clips.OfType<GeneratorClip>().ToList();
        var mattes = generators.Where(g => g.Kind == GeneratorKind.ColorMatte).ToList();
        var tones = generators.Where(g => g.Kind == GeneratorKind.Tone).ToList();
        ColorRow.LoadMany(mattes.Select(g => g.Color));
        FrequencyRow.LoadFrom(tones);
        ColorRow.IsVisible = mattes.Count > 0;
        FrequencyRow.IsVisible = tones.Count > 0;
        _generatorGroup.IsVisible = ColorRow.IsVisible || FrequencyRow.IsVisible;
        RefreshProps(project, clips);
    }

    private static string Caption(int count) =>
        count == 1 ? "GENERATOR" : string.Create(CultureInfo.InvariantCulture, $"{count} GENERATORS");

    private Func<Project, EditResult> Update(string label, Func<GeneratorClip, GeneratorClip> change) =>
        p => TimelineEdits.UpdateClips(p, ClipIds, c => c is GeneratorClip g ? change(g) : c, label);
}
