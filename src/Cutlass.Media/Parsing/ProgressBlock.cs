namespace Cutlass.Media.Parsing;

/// <summary>One completed <c>-progress</c> block, ended by <c>progress=continue</c> or <c>progress=end</c>.</summary>
/// <param name="OutTimeMicros">The output position (<c>out_time_us</c>, else <c>out_time_ms</c>, both in
/// microseconds), or null when the block had none or only <c>N/A</c>.</param>
/// <param name="Frame">The number of frames written, or null.</param>
/// <param name="Speed">The encoding speed as a multiple of real time (<c>5.7x</c> gives 5.7), or null.</param>
/// <param name="IsEnd">True for the final block (<c>progress=end</c>).</param>
public sealed record ProgressBlock(long? OutTimeMicros, long? Frame, double? Speed, bool IsEnd);
