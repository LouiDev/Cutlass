namespace Cutlass.Media.Platform;

/// <summary>An installed font file offered in the title font list.</summary>
/// <param name="Family">The display name: the family from fontconfig on Linux, the file name on Windows.</param>
/// <param name="Path">The full path of the font file.</param>
public sealed record SystemFont(string Family, string Path);
