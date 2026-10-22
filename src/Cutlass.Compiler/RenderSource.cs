namespace Cutlass.Compiler;

/// <summary>A file the compiler can read video or audio from: the original media or its proxy.</summary>
/// <param name="Path">The absolute path.</param>
/// <param name="FormatStartMicros">The file's <c>format.start_time</c> in microseconds (seeks are relative to it).</param>
/// <param name="Index">The frame index of its video stream, or null when there is none (CFR fallback).</param>
public sealed record RenderSource(string Path, long FormatStartMicros, FrameIndex? Index);
