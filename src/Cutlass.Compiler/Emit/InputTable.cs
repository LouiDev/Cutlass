using Cutlass.Core.Time;

namespace Cutlass.Compiler.Emit;

/// <summary>
/// The inputs of one compile (TECH_BRIEF 7.7), numbered by first use. Two uses share one input only when they cannot
/// desynchronise: the path, the seek and the still flag are equal and both segments start at the same timeline frame
/// and read the source at the same speed (the linked audio and video of one clip, or the same clip stacked on two
/// tracks). The shared input keeps the larger span. Every other use gets its own <c>-i</c>, because one input feeding
/// segments that are far apart on the timeline makes ffmpeg buffer decoded frames for the later one without bound.
/// </summary>
internal sealed class InputTable
{
    private readonly List<InputSpec> _inputs = [];
    private readonly List<(long StartFrame, int SpeedPercent)> _uses = [];

    /// <summary>The number of inputs so far.</summary>
    public int Count => _inputs.Count;

    /// <summary>Adds an input or merges it into an equal one that is read in step with it.</summary>
    /// <param name="path">The absolute path.</param>
    /// <param name="seekMicros">The seek, or null for a looped still.</param>
    /// <param name="durationMicros">The span this use needs.</param>
    /// <param name="loopStill">True for a looped still.</param>
    /// <param name="stillRate">The still frame rate, or null.</param>
    /// <param name="startFrame">The absolute timeline frame where the segment reading this use starts.</param>
    /// <param name="speedPercent">The speed the segment reads the source at (100 for stills).</param>
    /// <returns>The input index.</returns>
    public int Add(
        string path,
        long? seekMicros,
        long durationMicros,
        bool loopStill,
        Rational? stillRate,
        long startFrame,
        int speedPercent)
    {
        for (var i = 0; i < _inputs.Count; i++)
        {
            var existing = _inputs[i];
            if (string.Equals(existing.Path, path, StringComparison.Ordinal)
                && existing.SeekMicros == seekMicros
                && existing.LoopStill == loopStill
                && existing.StillRate == stillRate
                && _uses[i] == (startFrame, speedPercent))
            {
                if (durationMicros > existing.DurationMicros)
                {
                    _inputs[i] = existing with { DurationMicros = durationMicros };
                }

                return i;
            }
        }

        _inputs.Add(new InputSpec(path, seekMicros, durationMicros, loopStill, stillRate));
        _uses.Add((startFrame, speedPercent));
        return _inputs.Count - 1;
    }

    /// <summary>A snapshot of the inputs in index order.</summary>
    /// <returns>The inputs.</returns>
    public IReadOnlyList<InputSpec> ToList() => [.. _inputs];
}
