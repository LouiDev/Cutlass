namespace Cutlass.Media.Jobs;

/// <summary>
/// A unit of background work for one media item, run by the <see cref="JobScheduler"/>. The scheduler deduplicates
/// jobs by media id and kind, so enqueueing the same work twice runs it once.
/// </summary>
public sealed class MediaJob
{
    /// <summary>Creates a job.</summary>
    /// <param name="kind">What the job does.</param>
    /// <param name="mediaId">The media id.</param>
    /// <param name="mediaName">The display name.</param>
    /// <param name="work">
    /// The work. It reports progress from 0 to 1, honours the token and throws to fail. It runs on a pool thread.
    /// </param>
    public MediaJob(JobKind kind, string mediaId, string mediaName, Func<IProgress<double>, CancellationToken, Task> work)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        ArgumentNullException.ThrowIfNull(mediaName);
        ArgumentNullException.ThrowIfNull(work);
        Kind = kind;
        MediaId = mediaId;
        MediaName = mediaName;
        Work = work;
    }

    /// <summary>What the job does.</summary>
    public JobKind Kind { get; }

    /// <summary>The media id.</summary>
    public string MediaId { get; }

    /// <summary>The display name.</summary>
    public string MediaName { get; }

    /// <summary>The work.</summary>
    public Func<IProgress<double>, CancellationToken, Task> Work { get; }
}
