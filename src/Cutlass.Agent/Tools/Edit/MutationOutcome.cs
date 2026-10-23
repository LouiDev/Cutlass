using Cutlass.Core.Editing;

namespace Cutlass.Agent.Tools.Edit;

/// <summary>What a mutation action reports to <see cref="EditRunner"/>.</summary>
/// <param name="Result">The edit result (label, warnings, selection); its project is not used.</param>
/// <param name="ResetsRedo">True when a change resets the agent redo counter (every edit except undo and redo).</param>
public sealed record MutationOutcome(EditResult Result, bool ResetsRedo);
