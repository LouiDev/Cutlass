using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.ViewModels.Dialogs;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;

namespace Cutlass.App.ViewModels.Inspector;

/// <summary>
/// The context-sensitive inspector (DESIGN 4.4). The timeline selection picks a <see cref="Section"/>: a selected
/// transition, a selected gap, selected titles, selected media clips, or the sequence when nothing is selected. Each
/// section is a list of collapsible groups whose rows go through <see cref="Editor"/>: typed changes run
/// <c>session.Execute(edit, "inspector:&lt;ids&gt;:&lt;field&gt;")</c> and scrubby drags run as one gesture
/// (TECH_BRIEF 3.4, 10.1). <see cref="Load"/> runs on every session change and selection change; a section survives
/// while its selection stays, so a drag keeps its bindings.
/// </summary>
/// <remarks>
/// The shell wires two dialog hooks: <see cref="ShowSpeedDialog"/> (for example
/// <c>vm =&gt; SpeedDialog.ShowAsync(window, vm)</c>) and <see cref="ShowSequenceSettingsDialog"/>, and fills
/// <see cref="AvailableFonts"/> with system font file paths. The Effects tab is a separate <see cref="EffectsViewModel"/>.
/// </remarks>
public sealed partial class InspectorViewModel : ObservableObject
{
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _fonts = [];

    /// <summary>Creates the inspector.</summary>
    /// <param name="session">The project session (the timeline's session).</param>
    /// <param name="timeline">The timeline (selection, playhead, id generator, warnings).</param>
    public InspectorViewModel(ProjectSession session, TimelineViewModel timeline)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeline);
        if (!ReferenceEquals(session, timeline.Session))
        {
            throw new ArgumentException("The inspector must share the timeline's session.", nameof(session));
        }

        Timeline = timeline;
        Editor = new InspectorEditor(timeline);
        Editor.Unchanged += (_, _) => Load();
        Header = "SEQUENCE";
        session.Changed += OnSessionChanged;
        timeline.SelectionChanged += (_, _) => Load();
        Load();
    }

    /// <summary>The timeline.</summary>
    public TimelineViewModel Timeline { get; }

    /// <summary>The session path of every field.</summary>
    public InspectorEditor Editor { get; }

    /// <summary>The current section.</summary>
    [ObservableProperty]
    public partial InspectorSection? Section { get; private set; }

    /// <summary>The selection caption, for example <c>CLIP</c>, <c>3 CLIPS</c>, <c>TRANSITION</c> or <c>SEQUENCE</c>.</summary>
    [ObservableProperty]
    public partial string Header { get; private set; }

    /// <summary>
    /// True while an agent holds or pauses the project (AGENT_INTEGRATION 3.7): the view disables every editor while
    /// the values keep updating live from the session.
    /// </summary>
    [ObservableProperty]
    public partial bool IsReadOnly { get; set; }

    /// <summary>
    /// System font file paths for the title font menu, besides the bundled Inter fonts. The shell fills it, for example
    /// from <c>FontLocator.ListSystemFontsAsync</c>, on the UI thread.
    /// </summary>
    public IReadOnlyList<string> AvailableFonts
    {
        get => _fonts;
        set
        {
            _fonts = value ?? [];
            if (Section is TitleSectionViewModel titles)
            {
                titles.SetFonts(_fonts);
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Shows the speed dialog and returns true when the user applied it. Set by the shell, for example
    /// <c>vm =&gt; SpeedDialog.ShowAsync(mainWindow, vm)</c>; while null, <see cref="OpenSpeedDialogCommand"/> does nothing.
    /// </summary>
    public Func<SpeedDialogViewModel, Task<bool>>? ShowSpeedDialog { get; set; }

    /// <summary>
    /// Shows the sequence settings dialog and returns true when the user applied it. Set by the shell, for example
    /// <c>vm =&gt; SequenceSettingsDialog.ShowAsync(mainWindow, vm)</c>.
    /// </summary>
    public Func<SequenceSettingsDialogViewModel, Task<bool>>? ShowSequenceSettingsDialog { get; set; }

    /// <summary>The coalesce key of an inspector field of one clip (TECH_BRIEF 3.4).</summary>
    /// <param name="clipId">The clip.</param>
    /// <param name="field">The field, for example <c>video.opacity</c>.</param>
    /// <returns>The key.</returns>
    public static string CoalesceKey(string clipId, string field) => InspectorEditor.CoalesceKey([clipId], field);

    /// <summary>Reloads the section from the session and the selection, replacing it when the selection changed.</summary>
    public void Load()
    {
        var project = Timeline.Session.Current;
        var key = SectionKey(project, out var clipIds);
        var section = Section is { } current && current.Key == key ? current : CreateSection(project, key, clipIds);
        section.Refresh(project);
        Section = section;
        Header = section.Header;
    }

    private string SectionKey(Project project, out IReadOnlyList<string> clipIds)
    {
        clipIds = [];
        if (Timeline.SelectedTransitionId is { } transition && TimelineQuery.FindTransition(project, transition) is not null)
        {
            return "transition:" + transition;
        }

        if (Timeline.SelectedGapTrackId is { } track && Timeline.SelectedGapStart is { } start && Timeline.SelectedGapEnd is { } end)
        {
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"gap:{track}:{start}:{end}");
        }

        var ids = TimelineQuery.ExistingIds(project, Timeline.SelectedClipIds).Order(StringComparer.Ordinal).ToList();
        clipIds = ids;
        if (ids.Count == 0)
        {
            return "sequence";
        }

        var allTitles = ids.All(id => TimelineQuery.FindClip(project, id)?.Clip is TitleClip);
        return (allTitles ? "title:" : "clip:") + string.Join(",", ids);
    }

    private InspectorSection CreateSection(Project project, string key, IReadOnlyList<string> clipIds)
    {
        if (key.StartsWith("transition:", StringComparison.Ordinal))
        {
            return new TransitionSectionViewModel(Editor, _expanded, key["transition:".Length..]);
        }

        if (key.StartsWith("gap:", StringComparison.Ordinal))
        {
            return new GapSectionViewModel(
                Editor, _expanded, Timeline.SelectedGapTrackId!, Timeline.SelectedGapStart!.Value, Timeline.SelectedGapEnd!.Value, Timeline.CloseGapCommand);
        }

        if (key.StartsWith("title:", StringComparison.Ordinal))
        {
            return new TitleSectionViewModel(Editor, _expanded, clipIds, _fonts);
        }

        if (key.StartsWith("clip:", StringComparison.Ordinal))
        {
            return new ClipSectionViewModel(Editor, _expanded, project, clipIds, OpenSpeedDialogCommand);
        }

        return new SequenceSectionViewModel(Editor, _expanded, OpenSequenceSettingsCommand);
    }

    private void OnSessionChanged(object? sender, ProjectChangedEventArgs e)
    {
        if (e.Kind != ChangeKind.Saved)
        {
            Load();
        }
    }

    /// <summary>Ctrl+R: opens the speed dialog for the selected media clips and applies its result as one undo step.</summary>
    /// <returns>A task that completes when the dialog closed.</returns>
    [RelayCommand]
    private async Task OpenSpeedDialog()
    {
        var project = Timeline.Session.Current;
        var ids = TimelineQuery.ExistingIds(project, Timeline.SelectedClipIds)
            .Where(id => TimelineQuery.FindClip(project, id)?.Clip is MediaClip)
            .ToList();
        if (ids.Count == 0 || ShowSpeedDialog is not { } show)
        {
            return;
        }

        var dialog = new SpeedDialogViewModel(project, ids);
        if (await show(dialog) && dialog.CanApply)
        {
            Editor.Apply(dialog.CreateEdit(), null);
        }
    }

    /// <summary>"Change..." in the sequence section: opens the sequence settings dialog and applies its result.</summary>
    /// <returns>A task that completes when the dialog closed.</returns>
    [RelayCommand]
    private async Task OpenSequenceSettings()
    {
        if (ShowSequenceSettingsDialog is not { } show)
        {
            return;
        }

        var sequence = Timeline.Session.Current.Sequence;
        var dialog = new SequenceSettingsDialogViewModel(sequence.Settings, sequence.HasHadClips);
        if (await show(dialog))
        {
            Editor.Apply(dialog.CreateEdit(), null);
        }
    }

    /// <summary>Shift+E: enables or disables the selected clips (all disabled when any is enabled).</summary>
    [RelayCommand]
    private void ToggleEnabled()
    {
        var project = Timeline.Session.Current;
        var ids = TimelineQuery.ExistingIds(project, Timeline.SelectedClipIds);
        if (ids.IsEmpty)
        {
            return;
        }

        var enable = !ids.Any(id => TimelineQuery.FindClip(project, id)?.Clip.Enabled == true);
        Editor.Apply(p => TimelineEdits.SetEnabled(p, ids, enable), null);
    }
}
