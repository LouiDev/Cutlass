namespace Cutlass.Media.Jobs;

/// <summary>The life cycle of a scheduled job.</summary>
public enum JobState
{
    /// <summary>Waiting for a slot.</summary>
    Queued = 0,

    /// <summary>Running.</summary>
    Running = 1,

    /// <summary>Finished successfully.</summary>
    Completed = 2,

    /// <summary>Finished with an error (see <see cref="JobInfo.Error"/>).</summary>
    Failed = 3,

    /// <summary>Cancelled by the user or the shutdown.</summary>
    Canceled = 4,
}
