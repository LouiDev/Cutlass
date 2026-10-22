using Cutlass.Core.Time;

namespace Cutlass.Agent.Tools;

/// <summary>
/// Loudness per interval of s16le, 48 kHz stereo PCM (AGENT_INTEGRATION 4.4): RMS over both channels and the peak,
/// in dBFS against full scale 32768, floored at -120. A full-scale sine reads about -3.01 dBFS RMS. Feed it with
/// <see cref="Add"/> in order, then call <see cref="Complete"/>. Not thread-safe.
/// </summary>
public sealed class LevelAnalyzer
{
    /// <summary>The level of silence in dBFS.</summary>
    public const double FloorDb = -120;

    private const double FullScale = 32768.0;

    private readonly long _samplesPerInterval;
    private readonly long _sampleLimit;
    private readonly List<double> _rms = [];
    private readonly List<double> _peak = [];
    private readonly byte[] _carry = new byte[4];
    private int _carryLength;
    private double _sumSquares;
    private int _peakAbs;
    private long _inInterval;
    private long _total;

    /// <summary>Creates the analyzer.</summary>
    /// <param name="intervalMs">The interval length in milliseconds (at least 1).</param>
    /// <param name="sampleLimit">Stop after this many stereo samples, or null for all.</param>
    public LevelAnalyzer(int intervalMs, long? sampleLimit = null)
    {
        IntervalMs = Math.Max(1, intervalMs);
        _samplesPerInterval = (long)TimeMath.SampleRate * IntervalMs / 1000;
        _sampleLimit = sampleLimit ?? long.MaxValue;
    }

    /// <summary>The interval length in milliseconds.</summary>
    public int IntervalMs { get; }

    /// <summary>Converts a linear level (1 is full scale) to dBFS, floored at -120.</summary>
    /// <param name="linear">The level.</param>
    /// <returns>The dBFS, rounded to 0.01.</returns>
    public static double ToDb(double linear) =>
        linear <= 0 || double.IsNaN(linear) ? FloorDb : Math.Round(Math.Max(FloorDb, 20 * Math.Log10(linear)), 2);

    /// <summary>Adds PCM bytes; a sample frame split across calls is joined.</summary>
    /// <param name="pcm">s16le stereo bytes.</param>
    public void Add(ReadOnlySpan<byte> pcm)
    {
        while (pcm.Length > 0 && _total < _sampleLimit)
        {
            if (_carryLength > 0 || pcm.Length < 4)
            {
                var take = Math.Min(4 - _carryLength, pcm.Length);
                pcm[..take].CopyTo(_carry.AsSpan(_carryLength));
                _carryLength += take;
                pcm = pcm[take..];
                if (_carryLength == 4)
                {
                    AddFrame(_carry);
                    _carryLength = 0;
                }

                continue;
            }

            var whole = pcm.Length / 4 * 4;
            var block = pcm[..whole];
            for (var i = 0; i < block.Length && _total < _sampleLimit; i += 4)
            {
                AddFrame(block.Slice(i, 4));
            }

            pcm = pcm[whole..];
        }
    }

    /// <summary>Closes the last (partial) interval and returns the levels.</summary>
    /// <returns>RMS and peak per interval in dBFS.</returns>
    public (List<double> RmsDb, List<double> PeakDb) Complete()
    {
        if (_inInterval > 0)
        {
            Flush();
        }

        return (_rms, _peak);
    }

    private void AddFrame(ReadOnlySpan<byte> frame)
    {
        int l = (short)(frame[0] | (frame[1] << 8));
        int r = (short)(frame[2] | (frame[3] << 8));
        _sumSquares += ((double)l * l) + ((double)r * r);
        _peakAbs = Math.Max(_peakAbs, Math.Max(Math.Abs(l), Math.Abs(r)));
        _inInterval++;
        _total++;
        if (_inInterval == _samplesPerInterval)
        {
            Flush();
        }
    }

    private void Flush()
    {
        var rms = Math.Sqrt(_sumSquares / (2.0 * _inInterval)) / FullScale;
        _rms.Add(ToDb(rms));
        _peak.Add(ToDb(_peakAbs / FullScale));
        _sumSquares = 0;
        _peakAbs = 0;
        _inInterval = 0;
    }
}
