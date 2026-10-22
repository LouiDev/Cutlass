namespace Cutlass.Compiler;

/// <summary>
/// The output of <see cref="Emit.TimelineCompiler.Compile"/>: the inputs, the filtergraph and what it produces
/// (TECH_BRIEF 7.1). The same request always gives byte-identical output.
/// </summary>
/// <param name="Inputs">The ffmpeg inputs, in index order (<c>[0:...]</c>, <c>[1:...]</c>, ...).</param>
/// <param name="GraphText">The filtergraph: one chain per line, chains separated by <c>;\n</c>, no trailing newline.</param>
/// <param name="VideoLabel">The video output pad to map (<c>[vout]</c>), or null without video.</param>
/// <param name="AudioLabel">The audio output pad to map (<c>[aout]</c>), or null without audio.</param>
/// <param name="FrameCount">The number of video frames the graph outputs (0 without video).</param>
/// <param name="SampleCount">The number of audio samples per channel the graph outputs (0 without audio).</param>
/// <param name="AudioChannels">The audio channel count: 2 for a mix, 2N for N tracks per track, 0 without audio.</param>
/// <param name="AuxFiles">Files the graph refers to by bare name, to be written into the work directory.</param>
/// <param name="Warnings">Non-fatal problems, for example offline media or a fallback font.</param>
public sealed record CompiledRender(
    IReadOnlyList<InputSpec> Inputs,
    string GraphText,
    string? VideoLabel,
    string? AudioLabel,
    long FrameCount,
    long SampleCount,
    int AudioChannels,
    IReadOnlyList<AuxFile> AuxFiles,
    IReadOnlyList<string> Warnings);
