using System.Collections.Immutable;
using Cutlass.Core.Model;

namespace Cutlass.Core.Editing;

/// <summary>
/// The outcome of a pure timeline edit. When nothing changed, <see cref="Project"/> is the very same reference that
/// was passed in, and <see cref="Session.ProjectSession"/> records nothing.
/// </summary>
/// <param name="Project">The edited project (or the input reference when unchanged).</param>
/// <param name="Label">The undo label, for example <c>"Ripple Trim"</c>.</param>
/// <param name="Warnings">Clamping and refusal notes for the user, in plain English. Empty when the edit applied as asked.</param>
/// <param name="Select">Ids the UI should select after the edit (new or moved clips, a new transition). Empty to keep the selection.</param>
public sealed record EditResult(Project Project, string Label, ImmutableArray<string> Warnings, ImmutableArray<string> Select)
{
    /// <summary>Creates a result that leaves the project untouched.</summary>
    /// <param name="project">The unchanged project.</param>
    /// <param name="label">The label of the attempted edit.</param>
    /// <param name="warnings">Optional reasons why nothing happened.</param>
    /// <returns>A result whose <see cref="Project"/> is <paramref name="project"/>.</returns>
    public static EditResult Unchanged(Project project, string label, params string[] warnings) =>
        new(project, label, [.. warnings], ImmutableArray<string>.Empty);

    /// <summary>True when the edit produced a different project than <paramref name="before"/>.</summary>
    /// <param name="before">The project the edit was applied to.</param>
    /// <returns>False when the edit returned the same reference.</returns>
    public bool Changed(Project before) => !ReferenceEquals(before, Project);
}
