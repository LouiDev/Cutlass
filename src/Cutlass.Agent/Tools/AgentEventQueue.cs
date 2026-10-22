using Cutlass.Agent.Api.Dto;

namespace Cutlass.Agent.Tools;

/// <summary>
/// The long-poll event queue (AGENT_INTEGRATION 4.7): the newest 1000 events with increasing sequence numbers. A
/// client that asks for events older than the queue holds gets <c>gap</c>. Export progress is throttled to 1 per
/// second per export and playhead moves to 4 per second. Thread-safe.
/// </summary>
public sealed class AgentEventQueue
{
    /// <summary>How many events are kept.</summary>
    public const int Capacity = 1000;

    /// <summary>The longest wait of a poll in milliseconds.</summary>
    public const int MaxTimeoutMs = 25_000;

    private readonly LinkedList<EventDto> _events = new();
    private readonly Dictionary<string, long> _lastExportMs = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _seq;
    private long _lastPlayheadMs = long.MinValue;

    /// <summary>Creates the queue.</summary>
    /// <param name="time">The clock, or null for the system clock.</param>
    public AgentEventQueue(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The newest sequence number (0 before the first event).</summary>
    public long Latest
    {
        get
        {
            lock (_gate)
            {
                return _seq;
            }
        }
    }

    /// <summary>Adds an event; its sequence number and time are assigned here.</summary>
    /// <param name="e">The event.</param>
    public void Add(EventDto e)
    {
        ArgumentNullException.ThrowIfNull(e);
        TaskCompletionSource signal;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            var ms = now.ToUnixTimeMilliseconds();
            if (e.Kind == "exportProgress" && e.ExportId is { } id && e.State is "running" or "queued")
            {
                if (_lastExportMs.TryGetValue(id, out var last) && ms - last < 1000)
                {
                    return;
                }

                _lastExportMs[id] = ms;
            }

            if (e.Kind == "playheadChanged")
            {
                if (ms - _lastPlayheadMs < 250)
                {
                    return;
                }

                _lastPlayheadMs = ms;
            }

            e.Seq = ++_seq;
            e.Time = now;
            _events.AddLast(e);
            while (_events.Count > Capacity)
            {
                _events.RemoveFirst();
            }

            signal = _signal;
            _signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signal.TrySetResult();
    }

    /// <summary>Returns the events after <paramref name="after"/>, waiting up to <paramref name="timeoutMs"/> for one.</summary>
    /// <param name="after">The last sequence number the client saw.</param>
    /// <param name="timeoutMs">The longest wait (capped at 25 s).</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The events, the next cursor and the gap flag.</returns>
    public async Task<EventsDto> WaitAsync(long after, int timeoutMs, CancellationToken ct)
    {
        var deadline = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 0, MaxTimeoutMs));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(deadline);
        while (true)
        {
            Task wait;
            lock (_gate)
            {
                var result = Collect(after);
                if (result.Events.Count > 0 || result.Gap || deadline == TimeSpan.Zero)
                {
                    return result;
                }

                wait = _signal.Task;
            }

            try
            {
                await wait.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lock (_gate)
                {
                    return Collect(after);
                }
            }
        }
    }

    // Call under the lock.
    private EventsDto Collect(long after)
    {
        var first = _events.First?.Value.Seq ?? _seq + 1;
        var gap = after < first - 1 && after < _seq;
        var list = _events.Where(e => e.Seq > after).ToList();
        return new EventsDto { Events = list, Next = list.Count > 0 ? list[^1].Seq : Math.Max(after, 0), Gap = gap };
    }
}
