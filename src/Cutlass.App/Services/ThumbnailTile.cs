namespace Cutlass.App.Services;

/// <summary>One thumbnail on a sheet: the sheet file and the tile's pixel rectangle (TECH_BRIEF 6.5).</summary>
/// <param name="SheetPath">The JPEG sheet.</param>
/// <param name="X">The tile's left edge on the sheet.</param>
/// <param name="Y">The tile's top edge on the sheet.</param>
/// <param name="Width">The tile width.</param>
/// <param name="Height">The tile height.</param>
public sealed record ThumbnailTile(string SheetPath, int X, int Y, int Width, int Height);
