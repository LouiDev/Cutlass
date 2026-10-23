using Cutlass.Core.Time;
using Cutlass.Media.Audio;

namespace Cutlass.Media.Preview.Audio;

/// <summary>
/// The played samples the playback clock is published from (TECH_BRIEF 8.2): the output's
/// <see cref="IAudioOutput.PlayedSamples"/> while it is available, else a Stopwatch continuation from the last real
/// position, the way <see cref="NullAudioOutput"/> plays. An output lost during a run (a disconnected device) therefore
/// keeps the video at real time instead of racing ahead. Used by the feeder thread only.
/// </summary>
internal sealed class AudioClockSource
{
    private readonly IAudioOutput _output;
    private readonly bool _wasAvailable;
    private long _lastGood;
    private long _anchorSamples;
    private long _anchorTimestamp;
    private bool _started;
    private bool _lostUnreported;

    /// <summary>Creates the source.</summary>
    /// <param name="output">The audio output.</param>
    public AudioClockSource(IAudioOutput output)
    {
        _output = output;

        // An output unavailable from the start (NullAudioOutput) already plays on a Stopwatch and is fed as usual.
        _wasAvailable = output.IsAvailable;
    }

    /// <summary>True once the clock runs on the Stopwatch because the output became unavailable during the run.</summary>
    public bool FellBack { get; private set; }

    /// <summary>Checks the output and falls back when it became unavailable.</summary>
    /// <param name="timestamp">The <c>Stopwatch</c> timestamp now.</param>
    public void Poll(long timestamp)
    {
        if (_wasAvailable && !FellBack && !_output.IsAvailable)
        {
            FallBack(timestamp);
        }
    }

    /// <summary>Marks the start of playback: the Stopwatch clock of an unavailable output counts from here.</summary>
    /// <param name="timestamp">The <c>Stopwatch</c> timestamp of the start.</param>
    public void Start(long timestamp)
    {
        _started = true;
        if (FellBack)
        {
            _anchorSamples = _lastGood;
            _anchorTimestamp = timestamp;
        }
    }

    /// <summary>True exactly once after the output was lost during the run.</summary>
    /// <returns>Whether to report the loss now.</returns>
    public bool TakeLost()
    {
        var lost = _lostUnreported;
        _lostUnreported = false;
        return lost;
    }

    /// <summary>The played samples at a moment.</summary>
    /// <param name="timestamp">The <c>Stopwatch</c> timestamp now.</param>
    /// <param name="frequency">Stopwatch ticks per second.</param>
    /// <returns>The samples played since the start.</returns>
    public long PlayedSamples(long timestamp, long frequency)
    {
        if (!FellBack)
        {
            var played = _output.PlayedSamples;
            if (!_wasAvailable || _output.IsAvailable)
            {
                _lastGood = Math.Max(_lastGood, played);
                return _lastGood;
            }

            // Lost during the read: the value may be garbage, keep the last real one.
            FallBack(timestamp);
        }

        if (!_started)
        {
            return _lastGood;
        }

        var elapsed = Math.Max(0, timestamp - _anchorTimestamp);
        return _anchorSamples + (long)((Int128)elapsed * TimeMath.SampleRate / frequency);
    }

    private void FallBack(long timestamp)
    {
        FellBack = true;
        _lostUnreported = true;
        _anchorSamples = _lastGood;
        _anchorTimestamp = timestamp;
    }
}
