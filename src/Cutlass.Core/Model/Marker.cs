namespace Cutlass.Core.Model;

/// <summary>A sequence marker.</summary>
/// <param name="Id">Marker id (prefix <c>k_</c>).</param>
/// <param name="Frame">Timeline frame.</param>
/// <param name="Name">Label, may be empty.</param>
/// <param name="Color">Colour as <c>#RRGGBB</c>, default <c>#F5D547</c>.</param>
public sealed record Marker(string Id, long Frame, string Name, string Color);
