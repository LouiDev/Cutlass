using System.Globalization;
using System.Text;

namespace Cutlass.Media.Parsing;

/// <summary>
/// Incremental parser for <c>-progress pipe:1 -nostats</c> output (TECH_BRIEF 9). Feed it text as it arrives, in
/// chunks of any size (a line may be split across reads); it returns each block when its <c>progress=</c> line ends.
/// Values are trimmed (<c>speed= 5.7x</c> has a leading space). Not thread-safe: one reader feeds one parser.
/// </summary>
public sealed class ProgressParser
{
    private readonly StringBuilder _pending = new();
    private long? _outTimeMicros;
    private long? _frame;
    private double? _speed;

    /// <summary>The last completed block, or null before the first one.</summary>
    public ProgressBlock? Last { get; private set; }

    /// <summary>
    /// Computes the export fraction <c>out_time_us / (N/R * 1e6)</c>, clamped to [0, 1].
    /// </summary>
    /// <param name="outTimeMicros">The output position in microseconds.</param>
    /// <param name="totalMicros">The planned output duration in microseconds (N frames at rate R).</param>
    /// <returns>The fraction done.</returns>
    public static double Fraction(long outTimeMicros, long totalMicros) =>
        totalMicros <= 0 ? 0 : Math.Clamp((double)outTimeMicros / totalMicros, 0, 1);

    /// <summary>Feeds a chunk of text.</summary>
    /// <param name="chunk">The text as read from stdout.</param>
    /// <returns>The blocks completed by this chunk, oldest first (often none or one).</returns>
    public IReadOnlyList<ProgressBlock> Feed(ReadOnlySpan<char> chunk)
    {
        List<ProgressBlock>? done = null;
        foreach (var c in chunk)
        {
            if (c == '\n')
            {
                if (ProcessLine(_pending.ToString()) is { } block)
                {
                    (done ??= []).Add(block);
                }

                _pending.Clear();
            }
            else
            {
                _pending.Append(c);
            }
        }

        return done ?? (IReadOnlyList<ProgressBlock>)[];
    }

    /// <summary>Feeds one complete line (without its newline).</summary>
    /// <param name="line">The line.</param>
    /// <returns>The block this line completed, or null.</returns>
    public ProgressBlock? FeedLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return ProcessLine(line);
    }

    private ProgressBlock? ProcessLine(string line)
    {
        var eq = line.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0)
        {
            return null;
        }

        var key = line[..eq].Trim();
        var value = line[(eq + 1)..].Trim();
        switch (key)
        {
            case "out_time_us":
                _outTimeMicros = ParseLong(value) ?? _outTimeMicros;
                break;
            case "out_time_ms":
                // Despite its name, out_time_ms is also in microseconds; out_time_us wins when both appear.
                _outTimeMicros ??= ParseLong(value);
                break;
            case "frame":
                _frame = ParseLong(value) ?? _frame;
                break;
            case "speed":
                _speed = ParseSpeed(value) ?? _speed;
                break;
            case "progress":
                var block = new ProgressBlock(_outTimeMicros, _frame, _speed, value == "end");
                Last = block;
                _outTimeMicros = null;
                _frame = null;
                _speed = null;
                return block;
        }

        return null;
    }

    private static long? ParseLong(string value) =>
        long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static double? ParseSpeed(string value)
    {
        var text = value.EndsWith('x') ? value[..^1].Trim() : value;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ? speed : null;
    }
}
