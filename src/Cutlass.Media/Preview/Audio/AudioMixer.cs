using System.Collections.Immutable;

namespace Cutlass.Media.Preview.Audio;

/// <summary>
/// Mixes PerTrack playback audio (TECH_BRIEF 7.6 and 8.2): interleaved s16 with one stereo pair per audio track
/// (<c>2N</c> channels, A1 first) down to stereo, applying mute, solo and gain live from a <see cref="MixerState"/>:
/// <c>out = clamp(round(sum(active_t * in_t * 10^(gainDb_t/20)) * master))</c>. A track is active when it is not muted
/// and either nothing is soloed or it is soloed. It also records per-track and master peak levels.
/// <see cref="Mix"/> runs on the audio feeder; <see cref="SetState"/> and <see cref="ReadAndResetPeaks"/> may be called
/// from any thread.
/// </summary>
public sealed class AudioMixer
{
    private const float FullScale = 32768f;

    private readonly ImmutableArray<string> _trackIds;
    private readonly object _peakGate = new();
    private readonly object _stateGate = new();
    private readonly float[] _trackPeaks;

    // State, premixed flag and the derived factors change together: writers replace the record under _stateGate,
    // Mix reads it once per block without locking.
    private Gains _gains;
    private float _masterLeft;
    private float _masterRight;

    /// <summary>Creates a mixer for the given audio tracks.</summary>
    /// <param name="trackIds">The audio track ids in channel-pair order (the sequence's audio tracks, A1 first).</param>
    /// <param name="state">The initial state, or null for <see cref="MixerState.Neutral"/>.</param>
    public AudioMixer(IEnumerable<string> trackIds, MixerState? state = null)
    {
        ArgumentNullException.ThrowIfNull(trackIds);
        _trackIds = [.. trackIds];
        _trackPeaks = new float[Math.Max(1, _trackIds.Length)];
        _gains = Compute(_trackIds, state ?? MixerState.Neutral, premixed: false);
    }

    /// <summary>The number of audio tracks (channel pairs) the mixer expects.</summary>
    public int TrackCount => _trackIds.Length;

    /// <summary>
    /// The interleaved channel count <see cref="Mix"/> expects: <c>2N</c>, or 2 when there are no audio tracks (the
    /// graph then outputs one silent pair) or the input is <see cref="Premixed"/>.
    /// </summary>
    public int InputChannels => Volatile.Read(ref _gains).Channels;

    /// <summary>
    /// True when the input is already a stereo mix (the Mixed fallback of TECH_BRIEF 7.6): mute, solo and gain were
    /// applied in the graph, so only the master factor is applied here.
    /// </summary>
    public bool Premixed
    {
        get => Volatile.Read(ref _gains).Premixed;
        set
        {
            lock (_stateGate)
            {
                var current = Volatile.Read(ref _gains);
                if (current.Premixed != value)
                {
                    Volatile.Write(ref _gains, Compute(_trackIds, current.State, value));
                }
            }
        }
    }

    /// <summary>The current state.</summary>
    public MixerState State => Volatile.Read(ref _gains).State;

    /// <summary>The linear factor of a gain in dB, <c>10^(dB/20)</c>.</summary>
    /// <param name="gainDb">The gain in dB.</param>
    /// <returns>The factor.</returns>
    public static double GainFactor(double gainDb) => Math.Pow(10.0, gainDb / 20.0);

    /// <summary>Replaces the mixer state. The next mixed block uses it.</summary>
    /// <param name="state">The new state.</param>
    public void SetState(MixerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_stateGate)
        {
            Volatile.Write(ref _gains, Compute(_trackIds, state, Volatile.Read(ref _gains).Premixed));
        }
    }

    /// <summary>The linear factor applied to each track now (0 for inactive tracks), in channel-pair order.</summary>
    /// <returns>The factors.</returns>
    public IReadOnlyList<double> TrackFactors() => [.. Volatile.Read(ref _gains).Factors];

    /// <summary>Mixes <paramref name="frames"/> frames of <see cref="InputChannels"/> channels down to stereo.</summary>
    /// <param name="in2N">The interleaved input, at least <c>frames * InputChannels</c> samples.</param>
    /// <param name="stereo">The interleaved stereo output, at least <c>frames * 2</c> samples.</param>
    /// <param name="frames">The number of frames.</param>
    public void Mix(ReadOnlySpan<short> in2N, Span<short> stereo, int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        var gains = Volatile.Read(ref _gains);
        var channels = gains.Channels;
        if (in2N.Length < frames * channels)
        {
            throw new ArgumentException($"The input holds {in2N.Length} samples, but {frames} frames of {channels} channels need {frames * channels}.", nameof(in2N));
        }

        if (stereo.Length < frames * 2)
        {
            throw new ArgumentException($"The output holds {stereo.Length} samples, but {frames} stereo frames need {frames * 2}.", nameof(stereo));
        }

        var factors = gains.Factors;
        var pairs = factors.Length;
        Span<float> trackPeaks = pairs <= 64 ? stackalloc float[pairs] : new float[pairs];
        trackPeaks.Clear();
        var masterLeft = 0f;
        var masterRight = 0f;
        var master = gains.Master;
        for (var i = 0; i < frames; i++)
        {
            var row = i * channels;
            double left = 0;
            double right = 0;
            for (var t = 0; t < pairs; t++)
            {
                var f = factors[t];
                if (f == 0)
                {
                    continue;
                }

                var l = in2N[row + (2 * t)] * f;
                var r = in2N[row + (2 * t) + 1] * f;
                left += l;
                right += r;
                var peak = (float)Math.Max(Math.Abs(l), Math.Abs(r));
                if (peak > trackPeaks[t])
                {
                    trackPeaks[t] = peak;
                }
            }

            var outLeft = Clamp(left * master);
            var outRight = Clamp(right * master);
            stereo[2 * i] = outLeft;
            stereo[(2 * i) + 1] = outRight;
            masterLeft = Math.Max(masterLeft, Math.Abs((float)outLeft));
            masterRight = Math.Max(masterRight, Math.Abs((float)outRight));
        }

        lock (_peakGate)
        {
            for (var t = 0; t < pairs && t < _trackPeaks.Length; t++)
            {
                _trackPeaks[t] = Math.Max(_trackPeaks[t], trackPeaks[t] / FullScale);
            }

            _masterLeft = Math.Max(_masterLeft, masterLeft / FullScale);
            _masterRight = Math.Max(_masterRight, masterRight / FullScale);
        }
    }

    /// <summary>Returns the peaks since the last call and resets them.</summary>
    /// <returns>Per-track peaks (one per audio track) and the master peaks, linear in 0..1.</returns>
    public MixerPeaks ReadAndResetPeaks()
    {
        lock (_peakGate)
        {
            var tracks = ImmutableArray.Create(_trackPeaks, 0, _trackIds.Length);
            var result = new MixerPeaks(tracks, Math.Min(1f, _masterLeft), Math.Min(1f, _masterRight));
            Array.Clear(_trackPeaks);
            _masterLeft = 0;
            _masterRight = 0;
            return result;
        }
    }

    // round() is half away from zero (unspecified in the brief; the difference is at most one LSB on exact halves).
    private static short Clamp(double value) =>
        (short)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);

    private static Gains Compute(ImmutableArray<string> trackIds, MixerState state, bool premixed)
    {
        if (premixed || trackIds.Length == 0)
        {
            // One stereo pair at unity: the graph already mixed it, or it is the silent pair of a timeline without
            // audio tracks.
            return new Gains(state, [1.0], 2, state.Master, premixed);
        }

        var factors = new double[trackIds.Length];
        for (var t = 0; t < trackIds.Length; t++)
        {
            var id = trackIds[t];
            factors[t] = state.IsActive(id) ? GainFactor(state.Find(id)?.GainDb ?? 0.0) : 0.0;
        }

        return new Gains(state, factors, 2 * trackIds.Length, state.Master, premixed);
    }

    private sealed record Gains(MixerState State, double[] Factors, int Channels, double Master, bool Premixed);
}
