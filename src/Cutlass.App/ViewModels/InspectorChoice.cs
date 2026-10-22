namespace Cutlass.App.ViewModels;

/// <summary>One option of an inspector or dialog combo box: the model value and its display label.</summary>
/// <param name="Value">The model value (an enum value, a font reference, a frame rate).</param>
/// <param name="Label">The text shown.</param>
public sealed record InspectorChoice(object Value, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}
