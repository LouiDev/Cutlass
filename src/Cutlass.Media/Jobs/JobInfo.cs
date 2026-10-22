namespace Cutlass.Media.Jobs;

/// <summary>
/// An immutable snapshot of one scheduled job (TECH_BRIEF 6.7): kind, media, fraction, state and error. The scheduler
/// publishes a new snapshot through <see cref="JobScheduler.JobChanged"/> whenever something changes; compare by
/// <see cref="Id"/> to update a row.
/// </summary>
/// <param name="Id">The job id, unique per scheduler.</param>
/// <param name="Kind">What the job does.</param>
/// <param name="MediaId">The media id.</param>
/// <param name="MediaName">The media's display name (the file name).</param>
/// <param name="Priority">The current priority.</param>
/// <param name="State">The state.</param>
/// <param name="Fraction">Progress from 0 to 1.</param>
/// <param name="Error">The failure message, or null.</param>
public sealed record JobInfo(
    long Id,
    JobKind Kind,
    string MediaId,
    string MediaName,
    JobPriority Priority,
    JobState State,
    double Fraction,
    string? Error)
{
    /// <summary>True when the job has finished (completed, failed or cancelled).</summary>
    public bool IsFinished => State is JobState.Completed or JobState.Failed or JobState.Canceled;
}
