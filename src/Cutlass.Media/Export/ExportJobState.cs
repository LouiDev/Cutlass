namespace Cutlass.Media.Export;

/// <summary>The life cycle of an <see cref="ExportJob"/>.</summary>
public enum ExportJobState
{
    /// <summary>Waiting for the export before it (one export runs at a time).</summary>
    Queued = 0,

    /// <summary>Compiling or encoding.</summary>
    Running = 1,

    /// <summary>Finished: the output file exists under its final name.</summary>
    Completed = 2,

    /// <summary>Failed: the partial file was deleted and <see cref="ExportJob.Error"/> says why.</summary>
    Failed = 3,

    /// <summary>Cancelled by the user: the partial file was deleted.</summary>
    Canceled = 4,
}
