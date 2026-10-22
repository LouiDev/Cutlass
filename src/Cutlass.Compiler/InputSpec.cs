using Cutlass.Core.Time;

namespace Cutlass.Compiler;

/// <summary>
/// One ffmpeg input of a compiled render (TECH_BRIEF 7.1 and 7.7). Inputs are numbered by first use. Two uses share an
/// input only when the path and the seek are equal and their segments start at the same timeline frame at the same
/// speed (linked audio and video); the shared input keeps the larger span.
/// </summary>
/// <param name="Path">The absolute media path (passed through <c>ArgumentList</c>, never inside the graph).</param>
/// <param name="SeekMicros">The accurate input seek (<c>-ss</c>) in microseconds from the file's start time, or null
/// for no seek (looped stills).</param>
/// <param name="DurationMicros">The input span (<c>-t</c>) in microseconds.</param>
/// <param name="LoopStill">True for a still image read with <c>-loop 1 -framerate R</c>.</param>
/// <param name="StillRate">The frame rate of a looped still (the sequence rate), else null.</param>
public sealed record InputSpec(string Path, long? SeekMicros, long DurationMicros, bool LoopStill, Rational? StillRate);
