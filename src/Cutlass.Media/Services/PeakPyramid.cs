using System.Buffers.Binary;

namespace Cutlass.Media.Services;

/// <summary>
/// A waveform as min/max peaks at power-of-two decimations (TECH_BRIEF 6.5). Level 0 holds one (min, max) pair per
/// 160-sample block of 16 kHz mono audio, which is 100 peaks per second; each further level halves the previous one
/// (a lone last peak is carried over) until a level has one peak. Immutable and thread-safe once built.
/// </summary>
/// <remarks>
/// The waveform is mono because the decode downmixes with <c>-ac 1</c> (TECH_BRIEF 6.5), and <c>peaks.bin</c> has no
/// channel field. Positions passed to <see cref="PeaksFor"/> are 48 kHz source samples (<see cref="PositionRate"/>),
/// the rate Cutlass uses for every audio position, measured from the file's start.
/// </remarks>
public sealed class PeakPyramid
{
    /// <summary>The magic of <c>peaks.bin</c>.</summary>
    public const string Magic = "CTLP";

    /// <summary>The current <c>peaks.bin</c> format version.</summary>
    public const ushort FormatVersion = 1;

    /// <summary>The level 0 density.</summary>
    public const int DefaultPeaksPerSecond = 100;

    /// <summary>The decode sample rate.</summary>
    public const int DecodeSampleRate = 16000;

    /// <summary>Decoded samples per level 0 peak (16000 / 100).</summary>
    public const int BlockSamples = DecodeSampleRate / DefaultPeaksPerSecond;

    /// <summary>The sample rate of the positions <see cref="PeaksFor"/> takes.</summary>
    public const int PositionRate = 48000;

    private const int HeaderBytes = 4 + 2 + 4 + 4;

    private readonly short[][] _levels;

    /// <summary>Creates a pyramid from its levels.</summary>
    /// <param name="peaksPerSecond">The level 0 density.</param>
    /// <param name="levels">Each level as interleaved (min, max) pairs; level 0 first.</param>
    /// <exception cref="ArgumentException">When there is no level or a level has an odd length.</exception>
    public PeakPyramid(int peaksPerSecond, IReadOnlyList<short[]> levels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(peaksPerSecond);
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count == 0)
        {
            throw new ArgumentException("A peak pyramid needs at least one level.", nameof(levels));
        }

        if (levels.Any(l => l is null || l.Length % 2 != 0))
        {
            throw new ArgumentException("Every level holds (min, max) pairs.", nameof(levels));
        }

        PeaksPerSecond = peaksPerSecond;
        _levels = [.. levels.Select(l => (short[])l.Clone())];
    }

    /// <summary>The level 0 density (100).</summary>
    public int PeaksPerSecond { get; }

    /// <summary>The number of levels.</summary>
    public int LevelCount => _levels.Length;

    /// <summary>The audio duration covered by level 0, in seconds (rounded up to whole peaks).</summary>
    public double DurationSeconds => (double)Count(0) / PeaksPerSecond;

    /// <summary>
    /// Builds the pyramid from level 0: level <c>k+1</c> peak <c>j</c> covers level <c>k</c> peaks <c>2j</c> and
    /// <c>2j+1</c>.
    /// </summary>
    /// <param name="peaksPerSecond">The level 0 density.</param>
    /// <param name="level0">Interleaved (min, max) pairs.</param>
    /// <returns>The pyramid.</returns>
    public static PeakPyramid FromLevel0(int peaksPerSecond, ReadOnlySpan<short> level0)
    {
        if (level0.Length % 2 != 0)
        {
            throw new ArgumentException("Level 0 holds (min, max) pairs.", nameof(level0));
        }

        var levels = new List<short[]> { level0.ToArray() };
        while (levels[^1].Length > 2)
        {
            var previous = levels[^1];
            var count = previous.Length / 2;
            var next = new short[((count + 1) / 2) * 2];
            for (var j = 0; j < next.Length / 2; j++)
            {
                var a = 2 * j;
                var min = previous[2 * a];
                var max = previous[(2 * a) + 1];
                if (a + 1 < count)
                {
                    min = Math.Min(min, previous[2 * (a + 1)]);
                    max = Math.Max(max, previous[(2 * (a + 1)) + 1]);
                }

                next[2 * j] = min;
                next[(2 * j) + 1] = max;
            }

            levels.Add(next);
        }

        return new PeakPyramid(peaksPerSecond, levels);
    }

    /// <summary>Builds the pyramid from 16 kHz mono samples (min/max over 160-sample blocks, the last one partial).</summary>
    /// <param name="samples">The samples.</param>
    /// <returns>The pyramid.</returns>
    public static PeakPyramid FromSamples(ReadOnlySpan<short> samples)
    {
        var builder = new Builder();
        builder.Append(samples);
        return builder.Build();
    }

    /// <summary>Reads a <c>peaks.bin</c> stream (see <see cref="WriteTo"/>).</summary>
    /// <param name="stream">The source.</param>
    /// <returns>The pyramid, or null when the data is not a valid <c>peaks.bin</c> of the current version.</returns>
    public static PeakPyramid? ReadFrom(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[HeaderBytes];
        if (!TryReadExactly(stream, header)
            || !header.AsSpan(0, 4).SequenceEqual("CTLP"u8)
            || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4)) != FormatVersion)
        {
            return null;
        }

        var pps = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(6));
        var levelCount = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(10));
        if (pps <= 0 || levelCount <= 0 || levelCount > 64)
        {
            return null;
        }

        var levels = new List<short[]>(levelCount);
        var countBytes = new byte[4];
        for (var l = 0; l < levelCount; l++)
        {
            if (!TryReadExactly(stream, countBytes))
            {
                return null;
            }

            var n = BinaryPrimitives.ReadInt32LittleEndian(countBytes);
            if (n < 0 || (stream.CanSeek && stream.Length - stream.Position < 4L * n))
            {
                return null;
            }

            var bytes = new byte[4L * n];
            if (!TryReadExactly(stream, bytes))
            {
                return null;
            }

            var level = new short[2 * n];
            for (var i = 0; i < level.Length; i++)
            {
                level[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(2 * i));
            }

            levels.Add(level);
        }

        return new PeakPyramid(pps, levels);
    }

    /// <summary>Reads a <c>peaks.bin</c> file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The pyramid, or null when the file is missing, unreadable or invalid.</returns>
    public static PeakPyramid? ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            return ReadFrom(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The number of peaks at a level.</summary>
    /// <param name="level">The level, 0 being the finest.</param>
    /// <returns>The count.</returns>
    public int Count(int level) => _levels[level].Length / 2;

    /// <summary>One peak.</summary>
    /// <param name="level">The level.</param>
    /// <param name="index">The peak index.</param>
    /// <returns>The minimum and maximum sample.</returns>
    public (short Min, short Max) Peak(int level, int index)
    {
        var data = _levels[level];
        return (data[2 * index], data[(2 * index) + 1]);
    }

    /// <summary>
    /// The level to draw from when one bin spans <paramref name="level0PeaksPerBin"/> level 0 peaks: the coarsest
    /// level that still has at least one peak per bin, which is the level nearest one peak per pixel (DESIGN 5.1)
    /// without gaps.
    /// </summary>
    /// <param name="level0PeaksPerBin">Level 0 peaks per bin.</param>
    /// <returns>The level.</returns>
    public int LevelFor(double level0PeaksPerBin)
    {
        var level = 0;
        while (level + 1 < LevelCount && (1L << (level + 1)) <= level0PeaksPerBin)
        {
            level++;
        }

        return level;
    }

    /// <summary>
    /// The min/max envelope of a source range in <paramref name="bins"/> equal bins (one per pixel), from the level
    /// <see cref="LevelFor"/> picks. Each bin covers every peak it overlaps, so short transients never vanish.
    /// Bins outside the audio are (0, 0).
    /// </summary>
    /// <param name="startSample">The range start, in 48 kHz samples from the file's start.</param>
    /// <param name="endSample">The range end (exclusive).</param>
    /// <param name="bins">The number of bins.</param>
    /// <returns>One (min, max) pair per bin; empty when the range or the bin count is empty.</returns>
    public (short Min, short Max)[] PeaksFor(long startSample, long endSample, int bins)
    {
        if (bins <= 0 || endSample <= startSample)
        {
            return [];
        }

        var result = new (short Min, short Max)[bins];
        var span = endSample - startSample;
        var level0Unit = (double)PositionRate / PeaksPerSecond;
        var level = LevelFor(span / level0Unit / bins);
        var count = Count(level);
        var data = _levels[level];

        // Peak i of this level covers samples [i * unit, (i + 1) * unit), unit = 480 * 2^level at 48 kHz.
        var unitNum = (long)PositionRate << level;
        var unitDen = (long)PeaksPerSecond;

        // Clip each bin to the audio, so a coarse last peak never paints past the end.
        var audioEnd = (long)((Int128)Count(0) * PositionRate / PeaksPerSecond);
        for (var b = 0; b < bins; b++)
        {
            var a = startSample + (long)((Int128)span * b / bins);
            var e = startSample + (long)((Int128)span * (b + 1) / bins);
            if (e <= 0 || a >= audioEnd)
            {
                continue;
            }

            a = Math.Max(0, a);
            e = Math.Min(audioEnd, e);

            var first = FloorDiv((Int128)a * unitDen, unitNum);
            var last = CeilDiv((Int128)e * unitDen, unitNum);
            if (last <= first)
            {
                last = first + 1;
            }

            first = Math.Max(first, 0);
            last = Math.Min(last, count);
            if (first >= last)
            {
                continue;
            }

            short min = short.MaxValue, max = short.MinValue;
            for (var i = first; i < last; i++)
            {
                min = Math.Min(min, data[2 * i]);
                max = Math.Max(max, data[(2 * i) + 1]);
            }

            result[b] = (min, max);
        }

        return result;
    }

    /// <summary>
    /// Writes <c>peaks.bin</c> (TECH_BRIEF 6.1), little-endian: <c>"CTLP" u16 ver, i32 peaksPerSecond, i32 levels</c>,
    /// then per level <c>i32 n, (i16 min, i16 max)[n]</c>.
    /// </summary>
    /// <param name="stream">The destination.</param>
    public void WriteTo(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[HeaderBytes];
        System.Text.Encoding.ASCII.GetBytes(Magic, header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(6), PeaksPerSecond);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10), LevelCount);
        stream.Write(header);
        foreach (var level in _levels)
        {
            var bytes = new byte[4 + (2 * level.Length)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, level.Length / 2);
            for (var i = 0; i < level.Length; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4 + (2 * i)), level[i]);
            }

            stream.Write(bytes);
        }
    }

    private static int FloorDiv(Int128 a, long b) => (int)Math.Clamp((long)(a >= 0 ? a / b : -((-a + b - 1) / b)), int.MinValue, int.MaxValue);

    private static int CeilDiv(Int128 a, long b) => (int)Math.Clamp((long)(a >= 0 ? (a + b - 1) / b : -(-a / b)), int.MinValue, int.MaxValue);

    private static bool TryReadExactly(Stream stream, byte[] buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    /// <summary>
    /// Accumulates 16 kHz mono samples into level 0 peaks as they stream in (min/max per 160-sample block), so the
    /// decode never holds the whole audio. Not thread-safe.
    /// </summary>
    public sealed class Builder
    {
        private readonly List<short> _level0 = [];
        private short _min = short.MaxValue;
        private short _max = short.MinValue;
        private int _inBlock;

        /// <summary>The number of samples appended.</summary>
        public long SampleCount { get; private set; }

        /// <summary>Appends samples.</summary>
        /// <param name="samples">The samples.</param>
        public void Append(ReadOnlySpan<short> samples)
        {
            foreach (var sample in samples)
            {
                if (sample < _min)
                {
                    _min = sample;
                }

                if (sample > _max)
                {
                    _max = sample;
                }

                if (++_inBlock == BlockSamples)
                {
                    Flush();
                }
            }

            SampleCount += samples.Length;
        }

        /// <summary>Appends little-endian s16 bytes (an even count).</summary>
        /// <param name="bytes">The bytes.</param>
        public void AppendBytes(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length % 2 != 0)
            {
                throw new ArgumentException("s16le data has an even byte count.", nameof(bytes));
            }

            Span<short> chunk = stackalloc short[256];
            while (!bytes.IsEmpty)
            {
                var n = Math.Min(chunk.Length, bytes.Length / 2);
                for (var i = 0; i < n; i++)
                {
                    chunk[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(2 * i)..]);
                }

                Append(chunk[..n]);
                bytes = bytes[(2 * n)..];
            }
        }

        /// <summary>Finishes the last (partial) block and builds the pyramid.</summary>
        /// <returns>The pyramid.</returns>
        public PeakPyramid Build()
        {
            if (_inBlock > 0)
            {
                Flush();
            }

            return FromLevel0(DefaultPeaksPerSecond, _level0.ToArray());
        }

        private void Flush()
        {
            _level0.Add(_min);
            _level0.Add(_max);
            _min = short.MaxValue;
            _max = short.MinValue;
            _inBlock = 0;
        }
    }
}
