using System.Collections.Concurrent;
using Cutlass.Media.Jobs;

namespace Cutlass.Tests.Media.Cache;

/// <summary>The job scheduler with fake jobs: priority, per-media order, dedupe, concurrency, cancellation (TECH_BRIEF 6.7).</summary>
public sealed class JobSchedulerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task VisibleJobsRunBeforeBackgroundAndKindsRunInMediaOrder()
    {
        await using var scheduler = new JobScheduler(concurrency: 1);
        var order = new ConcurrentQueue<string>();
        var gate = new TaskCompletionSource();

        // The blocker holds the only slot while the rest is queued.
        scheduler.Enqueue(new MediaJob(JobKind.FrameIndex, "m_block", "block", async (_, ct) => await gate.Task.WaitAsync(ct)));
        scheduler.Enqueue(Recording(JobKind.Proxy, "m_a", order), JobPriority.Background);
        scheduler.Enqueue(Recording(JobKind.Waveform, "m_a", order), JobPriority.Background);
        scheduler.Enqueue(Recording(JobKind.Thumbnails, "m_b", order), JobPriority.Visible);
        scheduler.Enqueue(Recording(JobKind.FrameIndex, "m_c", order), JobPriority.Background);
        scheduler.Enqueue(Recording(JobKind.Thumbnails, "m_a", order), JobPriority.Background);

        var queued = scheduler.Jobs.Where(j => j.State == JobState.Queued).Select(j => j.MediaId + ":" + j.Kind).ToList();
        Assert.Equal(new[] { "m_b:Thumbnails", "m_c:FrameIndex", "m_a:Thumbnails", "m_a:Waveform", "m_a:Proxy" }, queued);

        gate.SetResult();
        await scheduler.WhenIdleAsync().WaitAsync(Wait);

        Assert.Equal(new[] { "m_b:Thumbnails", "m_c:FrameIndex", "m_a:Thumbnails", "m_a:Waveform", "m_a:Proxy" }, order);
    }

    [Fact]
    public async Task TheSameMediaAndKindIsQueuedOnceAndAMoreUrgentRequestRaisesIt()
    {
        await using var scheduler = new JobScheduler(concurrency: 1);
        var gate = new TaskCompletionSource();
        var runs = 0;
        scheduler.Enqueue(new MediaJob(JobKind.Proxy, "m_block", "block", async (_, ct) => await gate.Task.WaitAsync(ct)));

        var first = scheduler.Enqueue(Counting(JobKind.Waveform, "m_a", () => Interlocked.Increment(ref runs)), JobPriority.Background);
        var second = scheduler.Enqueue(Counting(JobKind.Waveform, "m_a", () => Interlocked.Increment(ref runs)), JobPriority.Visible);
        var other = scheduler.Enqueue(Counting(JobKind.Thumbnails, "m_a", () => Interlocked.Increment(ref runs)), JobPriority.Background);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(JobPriority.Visible, second.Priority);
        Assert.NotEqual(first.Id, other.Id);
        Assert.Equal(3, scheduler.ActiveCount);

        // A running job is deduplicated too.
        var running = scheduler.Enqueue(new MediaJob(JobKind.Proxy, "m_block", "block", (_, _) => Task.CompletedTask));
        Assert.Equal(JobState.Running, running.State);

        gate.SetResult();
        await scheduler.WhenIdleAsync().WaitAsync(Wait);
        Assert.Equal(2, runs);

        // Once finished, the same work can be queued again (it answers from the cache in real jobs).
        scheduler.Enqueue(Counting(JobKind.Waveform, "m_a", () => Interlocked.Increment(ref runs)));
        await scheduler.WhenIdleAsync().WaitAsync(Wait);
        Assert.Equal(3, runs);
    }

    [Fact]
    public async Task ConcurrencyIsTwoAndOneDuringPlayback()
    {
        await using var scheduler = new JobScheduler();
        var gate = new TaskCompletionSource();
        var running = 0;
        var peak = 0;
        for (var i = 0; i < 6; i++)
        {
            scheduler.Enqueue(new MediaJob(JobKind.Thumbnails, "m_" + i, "x", async (_, ct) =>
            {
                var now = Interlocked.Increment(ref running);
                InterlockedMax(ref peak, now);
                await gate.Task.WaitAsync(ct);
                Interlocked.Decrement(ref running);
            }));
        }

        Assert.Equal(2, scheduler.Jobs.Count(j => j.State == JobState.Running));
        // Both running jobs must have entered their work before the gate opens; otherwise the first can finish before
        // the second starts and the peak reads 1 (a race in the test, not in the scheduler).
        await WaitForAsync(() => Volatile.Read(ref running) == 2);
        scheduler.IsPlaybackActive = true;
        Assert.Equal(1, scheduler.EffectiveConcurrency);
        gate.SetResult();
        await scheduler.WhenIdleAsync().WaitAsync(Wait);
        Assert.Equal(2, peak);

        var gate2 = new TaskCompletionSource();
        peak = 0;
        for (var i = 0; i < 3; i++)
        {
            scheduler.Enqueue(new MediaJob(JobKind.Waveform, "m_" + i, "x", async (_, ct) =>
            {
                InterlockedMax(ref peak, Interlocked.Increment(ref running));
                await gate2.Task.WaitAsync(ct);
                Interlocked.Decrement(ref running);
            }));
        }

        Assert.Equal(1, scheduler.Jobs.Count(j => j.State == JobState.Running));
        gate2.SetResult();
        await scheduler.WhenIdleAsync().WaitAsync(Wait);
        Assert.Equal(1, peak);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for the jobs to start");
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task CancellingAQueuedOrRunningJobEndsItAsCanceled()
    {
        await using var scheduler = new JobScheduler(concurrency: 1);
        var started = new TaskCompletionSource();
        var running = scheduler.Enqueue(new MediaJob(JobKind.Proxy, "m_a", "a", async (_, ct) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }));
        var queued = scheduler.Enqueue(new MediaJob(JobKind.Waveform, "m_b", "b", (_, _) => Task.CompletedTask));
        await started.Task.WaitAsync(Wait);

        Assert.True(scheduler.Cancel(queued.Id));
        Assert.True(scheduler.Cancel(running.Id));
        Assert.False(scheduler.Cancel(12345));
        await scheduler.WhenIdleAsync().WaitAsync(Wait);

        var jobs = scheduler.Jobs;
        Assert.Equal(JobState.Canceled, jobs.Single(j => j.Id == running.Id).State);
        Assert.Equal(JobState.Canceled, jobs.Single(j => j.Id == queued.Id).State);
    }

    [Fact]
    public async Task FailuresAreReportedWithTheirMessageAndDoNotStopTheQueue()
    {
        await using var scheduler = new JobScheduler(concurrency: 1);
        var finished = new ConcurrentQueue<JobInfo>();
        scheduler.JobFinished += finished.Enqueue;
        var failing = scheduler.Enqueue(new MediaJob(JobKind.Thumbnails, "m_a", "a", (_, _) => throw new InvalidOperationException("ffmpeg exploded")));
        var ok = scheduler.Enqueue(new MediaJob(JobKind.Waveform, "m_a", "a", (_, _) => Task.CompletedTask));

        await scheduler.WhenIdleAsync().WaitAsync(Wait);

        var failed = finished.Single(j => j.Id == failing.Id);
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal("ffmpeg exploded", failed.Error);
        Assert.True(failed.IsFinished);
        var done = finished.Single(j => j.Id == ok.Id);
        Assert.Equal(JobState.Completed, done.State);
        Assert.Equal(1.0, done.Fraction);
    }

    [Fact]
    public async Task ProgressAndStatesArePublishedInOrderAndArtifactReadyGoesToTheContext()
    {
        var context = new RecordingContext();
        await using var scheduler = new JobScheduler(concurrency: 1, artifactContext: context);
        var changes = new ConcurrentQueue<JobInfo>();
        scheduler.JobChanged += changes.Enqueue;
        var ready = new ConcurrentQueue<(string, JobKind)>();
        scheduler.ArtifactReady += (id, kind) => ready.Enqueue((id, kind));

        var job = scheduler.Enqueue(new MediaJob(JobKind.Waveform, "m_a", "a", (p, _) =>
        {
            p.Report(0.25);
            p.Report(0.5);
            return Task.CompletedTask;
        }));
        await scheduler.WhenIdleAsync().WaitAsync(Wait);

        var states = changes.Where(c => c.Id == job.Id).Select(c => c.State).ToList();
        Assert.Equal(JobState.Queued, states[0]);
        Assert.Equal(JobState.Running, states[1]);
        Assert.Equal(JobState.Completed, states[^1]);
        var fractions = changes.Where(c => c.Id == job.Id).Select(c => c.Fraction).ToList();
        Assert.Equal(fractions.Order(), fractions);

        // ArtifactReady waits in the context until the "UI thread" runs it.
        Assert.Empty(ready);
        Assert.Equal(1, context.Run());
        Assert.Equal(("m_a", JobKind.Waveform), Assert.Single(ready));
    }

    [Fact]
    public async Task AnExportCancelsARunningProxyAndRequeuesItUntilTheExportEnds()
    {
        await using var scheduler = new JobScheduler();
        var attempts = 0;
        var firstStarted = new TaskCompletionSource();
        var proxy = scheduler.Enqueue(new MediaJob(JobKind.Proxy, "m_a", "a", async (_, ct) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstStarted.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        }));
        await firstStarted.Task.WaitAsync(Wait);

        scheduler.IsExportActive = true;
        await WaitUntil(() => scheduler.Jobs.Single(j => j.Id == proxy.Id).State == JobState.Queued);

        // Other work still runs during the export; the proxy waits.
        var other = scheduler.Enqueue(new MediaJob(JobKind.Waveform, "m_b", "b", (_, _) => Task.CompletedTask));
        await WaitUntil(() => scheduler.Jobs.Single(j => j.Id == other.Id).State == JobState.Completed);
        Assert.Equal(JobState.Queued, scheduler.Jobs.Single(j => j.Id == proxy.Id).State);

        scheduler.IsExportActive = false;
        await scheduler.WhenIdleAsync().WaitAsync(Wait);
        Assert.Equal(2, attempts);
        Assert.Equal(JobState.Completed, scheduler.Jobs.Single(j => j.Id == proxy.Id).State);
    }

    [Fact]
    public async Task PrioritizeAndCancelMediaAffectOnlyThatMedia()
    {
        await using var scheduler = new JobScheduler(concurrency: 1);
        var gate = new TaskCompletionSource();
        var order = new ConcurrentQueue<string>();
        scheduler.Enqueue(new MediaJob(JobKind.FrameIndex, "m_block", "block", async (_, ct) => await gate.Task.WaitAsync(ct)));
        scheduler.Enqueue(Recording(JobKind.Waveform, "m_a", order));
        scheduler.Enqueue(Recording(JobKind.Waveform, "m_b", order));
        scheduler.Enqueue(Recording(JobKind.Thumbnails, "m_c", order));

        scheduler.Prioritize("m_b", JobPriority.Visible);
        Assert.Equal(1, scheduler.CancelMedia("m_c"));
        gate.SetResult();
        await scheduler.WhenIdleAsync().WaitAsync(Wait);

        Assert.Equal(new[] { "m_b:Waveform", "m_a:Waveform" }, order);
    }

    [Fact]
    public async Task DisposeCancelsEverythingAndRefusesNewJobs()
    {
        var scheduler = new JobScheduler(concurrency: 1);
        var started = new TaskCompletionSource();
        var canceled = false;
        scheduler.Enqueue(new MediaJob(JobKind.Proxy, "m_a", "a", async (_, ct) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
                throw;
            }
        }));
        await started.Task.WaitAsync(Wait);

        await scheduler.DisposeAsync().AsTask().WaitAsync(Wait);

        Assert.True(canceled);
        Assert.Throws<ObjectDisposedException>(() => scheduler.Enqueue(new MediaJob(JobKind.Waveform, "m_b", "b", (_, _) => Task.CompletedTask)));
    }

    private static MediaJob Recording(JobKind kind, string mediaId, ConcurrentQueue<string> order) =>
        new(kind, mediaId, mediaId, (_, _) =>
        {
            order.Enqueue(mediaId + ":" + kind);
            return Task.CompletedTask;
        });

    private static MediaJob Counting(JobKind kind, string mediaId, Action onRun) =>
        new(kind, mediaId, mediaId, (_, _) =>
        {
            onRun();
            return Task.CompletedTask;
        });

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not come true in time.");
            await Task.Delay(10);
        }
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posted = new();

        public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

        public int Run()
        {
            var count = 0;
            while (_posted.TryDequeue(out var item))
            {
                item.Callback(item.State);
                count++;
            }

            return count;
        }
    }
}
