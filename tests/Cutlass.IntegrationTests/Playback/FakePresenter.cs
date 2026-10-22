using System.Diagnostics;
using Cutlass.Media.Preview;

namespace Cutlass.IntegrationTests.Playback;

/// <summary>
/// Stands in for the app's <c>FramePresenter</c> (TECH_BRIEF 10.3): a thread that polls the session about every
/// millisecond to 16 ms (the OS timer decides), reads the clock frame, takes the newest due frame with
/// <see cref="PlaybackSession.TryTakeFrame(long, out VideoFrame?, out int)"/>, records it with a small analysis of its
/// pixels and returns it to the pool.
/// </summary>
internal sealed class FakePresenter : IDisposable
{
    private readonly PlaybackSession _session;
    private readonly Func<byte[], int, int, int> _analyse;
    private readonly List<Presentation> _presented = [];
    private readonly object _gate = new();
    private readonly Thread _thread;
    private volatile bool _stop;
    private long _dropped;

    /// <summary>Starts polling.</summary>
    /// <param name="session">The session.</param>
    /// <param name="analyse">Turns a frame's pixels (buffer, width, height) into a value to record.</param>
    public FakePresenter(PlaybackSession session, Func<byte[], int, int, int> analyse)
    {
        _session = session;
        _analyse = analyse;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Fake presenter" };
        _thread.Start();
    }

    /// <summary>Every presented frame in order.</summary>
    public IReadOnlyList<Presentation> Presented
    {
        get
        {
            lock (_gate)
            {
                return [.. _presented];
            }
        }
    }

    /// <summary>The frames the session dropped for this presenter.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Stops polling and waits for the thread.</summary>
    public void Dispose()
    {
        _stop = true;
        _thread.Join();
    }

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        while (!_stop)
        {
            var target = _session.Position;
            if (_session.TryTakeFrame(target, out var frame, out var dropped))
            {
                var value = _analyse(frame!.Buffer, frame.Width, frame.Height);
                lock (_gate)
                {
                    _presented.Add(new Presentation(frame.TimelineFrame, target, value, frame.Pooled, clock.Elapsed));
                }

                frame.Return();
            }

            Interlocked.Add(ref _dropped, dropped);
            Thread.Sleep(1);
        }
    }

    /// <summary>One presented frame.</summary>
    /// <param name="Frame">The timeline frame the picture is labelled with.</param>
    /// <param name="Target">The clock frame it was taken for.</param>
    /// <param name="Value">The analysis of its pixels.</param>
    /// <param name="Pooled">False for the cached start frame.</param>
    /// <param name="At">When it was taken, since the presenter started.</param>
    internal sealed record Presentation(long Frame, long Target, int Value, bool Pooled, TimeSpan At);
}
