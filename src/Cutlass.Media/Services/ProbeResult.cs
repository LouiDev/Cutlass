using Cutlass.Core.Model;

namespace Cutlass.Media.Services;

/// <summary>The outcome of probing one file.</summary>
/// <param name="Info">The parsed media info.</param>
/// <param name="Json">The raw ffprobe JSON (stored as <c>probe.json</c> in the media cache).</param>
public sealed record ProbeResult(MediaInfo Info, string Json);
