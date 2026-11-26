using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels.Inspector;

/// <summary>
/// The content of the inspector for one kind of selection (DESIGN 4.4): clips, titles, a transition, a gap, or the
/// sequence when nothing is selected. A section lives as long as the selection it was built for (its
/// <see cref="Key"/>); session changes only <see cref="Refresh"/> it, so a running drag keeps its bindings.
/// </summary>
public abstract partial class InspectorSection : ObservableObject
{
    private readonly IDictionary<string, bool> _states;

    /// <summary>Creates the section.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="states">The remembered expanded states of the groups.</param>
    /// <param name="key">The selection identity, for example <c>clip:c_1,c_2</c>.</param>
    /// <param name="header">The header, for example <c>CLIP</c>.</param>
    protected InspectorSection(InspectorEditor editor, IDictionary<string, bool> states, string key, string header)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(states);
        Editor = editor;
        _states = states;
        Key = key;
        Header = header;
    }

    /// <summary>The selection identity this section was built for.</summary>
    public string Key { get; }

    /// <summary>The header shown under the panel title, for example <c>3 CLIPS</c>.</summary>
    [ObservableProperty]
    public partial string Header { get; set; }

    /// <summary>The collapsible groups.</summary>
    public ObservableCollection<InspectorGroup> Groups { get; } = [];

    /// <summary>The session path.</summary>
    protected InspectorEditor Editor { get; }

    /// <summary>Reloads every field from the project.</summary>
    /// <param name="project">The current project.</param>
    public abstract void Refresh(Project project);

    /// <summary>
    /// Runs one edit per id on the result of the previous one and returns a single result (one undo step), with the
    /// warnings of every step. Ids whose step returns null are skipped.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="label">The undo label.</param>
    /// <param name="ids">The ids.</param>
    /// <param name="step">The edit of one id, or null to skip it.</param>
    /// <returns>The combined result.</returns>
    public static EditResult Chain(Project project, string label, IEnumerable<string> ids, Func<Project, string, EditResult?> step)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(step);
        var current = project;
        var warnings = ImmutableArray.CreateBuilder<string>();
        foreach (var id in ids)
        {
            if (step(current, id) is { } result)
            {
                current = result.Project;
                warnings.AddRange(result.Warnings.IsDefault ? [] : result.Warnings);
            }
        }

        return new EditResult(current, label, warnings.ToImmutable(), ImmutableArray<string>.Empty);
    }

    /// <summary>Adds a group.</summary>
    /// <param name="title">The caps title.</param>
    /// <param name="reset">The reset action, or null.</param>
    /// <returns>The group.</returns>
    protected InspectorGroup AddGroup(string title, Action? reset = null)
    {
        var group = new InspectorGroup(title, _states, reset);
        Groups.Add(group);
        return group;
    }

    /// <summary>Applies an edit through the editor without a coalesce key (Reset buttons, keyframe add and remove).</summary>
    /// <param name="edit">The edit.</param>
    protected void Run(Func<Project, EditResult> edit) => Editor.Apply(edit, null);
}
