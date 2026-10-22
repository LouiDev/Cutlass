namespace Cutlass.Media.Platform;

/// <summary>A title font made available in the work directory.</summary>
/// <param name="BareName">The bare file name in the work directory, for <c>drawtext=fontfile=</c>.</param>
/// <param name="IsFallback">True when the requested font was unavailable and a system fallback is used (the title
/// shows a warning badge).</param>
public sealed record FontResolution(string BareName, bool IsFallback);
