using Cutlass.App.ViewModels;
using Cutlass.Media.Export;
using Cutlass.Media.Jobs;

namespace Cutlass.Tests.App.Shell;

public sealed class MediaJobsTests
{
    private static JobInfo Info(long id, JobKind kind, JobState state, double fraction = 0, string? error = null) =>
        new(id, kind, "m_1", "beach.mp4", JobPriority.Visible, state, fraction, error);

    [Fact]
    public void SchedulerSnapshotsBecomeRows()
    {
        var jobs = new JobsViewModel(dialogs: null, post: a => a());
        var seen = new List<JobInfo>();
        jobs.MediaJobChanged += (_, info) => seen.Add(info);

        jobs.Apply(Info(1, JobKind.Proxy, JobState.Queued));
        var row = Assert.Single(jobs.Jobs);
        Assert.Equal("Proxy", row.Kind);
        Assert.Equal("beach.mp4", row.Title);
        Assert.Equal(1, row.JobId);
        Assert.Equal("m_1", row.MediaId);
        Assert.Equal("Queued", row.StatusText);
        Assert.True(jobs.HasActiveJobs);

        jobs.Apply(Info(1, JobKind.Proxy, JobState.Running, 0.43));
        Assert.Same(row, Assert.Single(jobs.Jobs));
        Assert.Equal(ExportJobState.Running, row.State);
        Assert.Equal("43%", row.StatusText);
        Assert.Equal("Jobs: proxy 1/1 (43%)", jobs.Summary);

        jobs.Apply(Info(1, JobKind.Proxy, JobState.Completed, 1));
        Assert.True(row.IsCompleted);
        Assert.False(row.ShowOpenFolder);
        Assert.False(jobs.HasActiveJobs);
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void JobsThatFinishBeforeTheirRowNeedNoRowButFailuresDo()
    {
        var jobs = new JobsViewModel(dialogs: null, post: a => a());
        jobs.Apply(Info(5, JobKind.FrameIndex, JobState.Completed, 1));
        Assert.Empty(jobs.Jobs);

        jobs.Apply(Info(6, JobKind.Waveform, JobState.Failed, 0, "ffmpeg failed"));
        var row = Assert.Single(jobs.Jobs);
        Assert.True(row.IsFailed);
        Assert.False(row.ShowErrorWhenFailed);
        Assert.Contains("ffmpeg failed", row.CreateErrorDialog().Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaRowsEstimateTheTimeLeft()
    {
        var row = JobItemViewModel.ForMediaJob(Info(2, JobKind.Thumbnails, JobState.Running), cancel: null, dialogs: null);
        row.Apply(Info(2, JobKind.Thumbnails, JobState.Running, 0.25), TimeSpan.FromSeconds(10));
        Assert.Equal("ETA 0:30", row.EtaText);
        row.Apply(Info(2, JobKind.Thumbnails, JobState.Running, 0.01), TimeSpan.FromSeconds(1));
        Assert.Equal(string.Empty, row.EtaText);
        row.Apply(Info(2, JobKind.Thumbnails, JobState.Canceled), TimeSpan.FromSeconds(2));
        Assert.Equal("Cancelled", row.StatusText);
        Assert.Equal(ExportJobState.Canceled, row.State);
    }
}
