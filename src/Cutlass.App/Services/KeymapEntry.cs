namespace Cutlass.App.Services;

/// <summary>One line of the keymap: a chord and the command it runs.</summary>
/// <param name="Chord">The chord text, for example <c>Ctrl+Shift+K</c>.</param>
/// <param name="CommandId">One of <see cref="CommandIds"/>.</param>
public sealed record KeymapEntry(string Chord, string CommandId);
