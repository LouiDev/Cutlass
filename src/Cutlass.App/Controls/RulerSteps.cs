namespace Cutlass.App.Controls;

/// <summary>The ruler tick spacing in frames: labelled major ticks and unlabelled minor ticks.</summary>
/// <param name="Major">Frames between labelled ticks.</param>
/// <param name="Minor">Frames between minor ticks (divides <paramref name="Major"/>).</param>
public sealed record RulerSteps(long Major, long Minor);
