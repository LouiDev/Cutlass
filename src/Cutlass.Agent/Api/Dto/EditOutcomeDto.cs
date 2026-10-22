namespace Cutlass.Agent.Api.Dto;

/// <summary>The result object of a mutating call; the envelope carries <c>changed</c>, <c>select</c> and <c>undoLabel</c>.</summary>
public sealed class EditOutcomeDto
{
    /// <summary>The edit's label, for example <c>Split</c>.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The open batch label after the call, or null.</summary>
    public string? Batch { get; set; }
}
