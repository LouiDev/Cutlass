using System.Collections.Concurrent;
using Cutlass.Media.Preview;

// Not ".Preview": that namespace would hide TimelineKit.Preview(...) in the Export tests, which import it statically.
namespace Cutlass.IntegrationTests.PreviewTests;

/// <summary>
/// A frame sink for tests: copies every presented frame, returns it to the pool and lets tests wait for a request id.
/// </summary>
internal sealed class RecordingSink : IVideoFrameSink
{
    private readonly object _gate = new();
    private readonly List<PresentedFrame> _frames = [];
    private readonly ConcurrentDictionary<long, TaskCompletionSource<PresentedFrame>> _waiters = new();

    /// <summary>Every presented frame, in presentation order.</summary>
    public IReadOnlyList<PresentedFrame> Frames
    {
        get
        {
            lock (_gate)
            {
                return [.. _frames];
            }
        }
    }

    /// <inheritdoc />
    public void Present(VideoFrame frame)
    {
        var copy = new byte[frame.Stride * frame.Height];
        Buffer.BlockCopy(frame.Buffer, 0, copy, 0, copy.Length);
        var presented = new PresentedFrame(frame.RequestId, frame.TimelineFrame, frame.Width, frame.Height, frame.Stride, frame.Pooled, copy);
        frame.Return();
        lock (_gate)
        {
            _frames.Add(presented);
        }

        Waiter(frame.RequestId).TrySetResult(presented);
    }

    /// <summary>Waits until the frame of a request was presented.</summary>
    public Task<PresentedFrame> WaitAsync(long requestId, TimeSpan timeout) => Waiter(requestId).Task.WaitAsync(timeout);

    private TaskCompletionSource<PresentedFrame> Waiter(long requestId) =>
        _waiters.GetOrAdd(requestId, _ => new TaskCompletionSource<PresentedFrame>(TaskCreationOptions.RunContinuationsAsynchronously));
}
