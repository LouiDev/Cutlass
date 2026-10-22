using Cutlass.Core.Model;

namespace Cutlass.Core.Session;

/// <summary>One undoable step: the whole project before and after it (the model is immutable, so this is cheap).</summary>
/// <param name="Before">The project before the step (undo restores it).</param>
/// <param name="After">The project after the step (redo restores it).</param>
/// <param name="Label">The undo label shown in the Edit menu.</param>
/// <param name="CoalesceKey">Steps with the same key within 1.5 s merge into one, or null.</param>
/// <param name="TimestampMs">When the step was last recorded or merged, from the session clock.</param>
public sealed record HistoryEntry(Project Before, Project After, string Label, string? CoalesceKey, long TimestampMs);
