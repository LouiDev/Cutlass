using Cutlass.App.ViewModels;
using Cutlass.Media.Export;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.Tests.App.Shell;

public sealed class JobsViewModelTests
{
    private static JobsViewModel Jobs() => new(dialogs: null, post: a => a());

    private static JobItemViewModel Item(Action? cancel = null) => new("Export", "trip.mp4", "C:/out/trip.mp4", cancel, dialogs: null);

    [Fact]
    public void StartsEmpty()
    {
        var jobs = Jobs();
        Assert.Equal("No jobs", jobs.Summary);
        Assert.False(jobs.HasJobs);
        Assert.False(jobs.HasActiveJobs);
    }

    [Fact]
    public void ProgressUpdatesTheRowAndTheSummary()
    {
        var jobs = Jobs();
        var item = jobs.Add(Item());
        Assert.Equal("Queued", item.StatusText);
        Assert.True(jobs.HasActiveJobs);

        item.Apply(new ExportProgress(ExportJobState.Running, 0.4321, TimeSpan.FromSeconds(42), 2.35, 1_000_000));
        Assert.Equal(ExportJobState.Running, item.State);
        Assert.Equal("43%", item.StatusText);
        Assert.Equal("2.4x", item.SpeedText);
        Assert.Equal("ETA 0:42", item.EtaText);
        Assert.Equal(43.2, item.Percent);
        Assert.Equal("Jobs: export 1/1 (43%)", jobs.Summary);
    }

    [Fact]
    public void FinishingCompletesTheBatch()
    {
        var jobs = Jobs();
        var first = jobs.Add(Item());
        var second = jobs.Add(Item());
        Assert.Same(second, jobs.Jobs[0]);
        first.Apply(new ExportProgress(ExportJobState.Running, 0.5, null, null, 0));
        Assert.Equal("Jobs: export 1/2 (50%)", jobs.Summary);

        first.Finish(ExportJobState.Completed, null, [], "ffmpeg ...");
        Assert.True(first.IsCompleted);
        Assert.Equal(1, first.Fraction);
        Assert.Equal("Done", first.StatusText);
        Assert.False(first.OpenFolderCommand.CanExecute(null)); // no dialog service in tests
        second.Apply(new ExportProgress(ExportJobState.Running, 0.1, null, null, 0));
        Assert.Equal("Jobs: export 2/2 (10%)", jobs.Summary);

        second.Finish(ExportJobState.Canceled, null, [], null);
        Assert.False(jobs.HasActiveJobs);
        Assert.Equal("Jobs: export cancelled", jobs.Summary);

        jobs.ClearFinishedCommand.Execute(null);
        Assert.False(jobs.HasJobs);
        Assert.Equal("No jobs", jobs.Summary);
    }

    [Fact]
    public void ProgressAfterFinishIsIgnoredAndFinishRaisesOnce()
    {
        var item = Item();
        var finished = 0;
        item.Finished += (_, _) => finished++;
        item.Finish(ExportJobState.Failed, new InvalidOperationException("boom"), ["line"], "cmd");
        item.Finish(ExportJobState.Completed, null, [], null);
        item.Apply(new ExportProgress(ExportJobState.Running, 0.9, null, null, 0));
        Assert.Equal(1, finished);
        Assert.True(item.IsFailed);
        Assert.Equal("Failed", item.StatusText);
    }

    [Fact]
    public void CancelRunsOnlyWhileActive()
    {
        var cancelled = 0;
        var item = Item(() => cancelled++);
        Assert.True(item.CancelCommand.CanExecute(null));
        item.CancelCommand.Execute(null);
        Assert.Equal(1, cancelled);
        Assert.Equal("Cancelling", item.StatusText);
        item.Finish(ExportJobState.Canceled, null, [], null);
        Assert.False(item.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void FailedJobsBuildTheErrorDialogFromTheFfmpegError()
    {
        var item = Item();
        var log = Enumerable.Range(1, 40).Select(i => "line " + i).ToList();
        item.Finish(ExportJobState.Failed, new FfmpegException(1, ["tail"], "ffmpeg -i x"), log, "ffmpeg -i x");
        var dialog = item.CreateErrorDialog();
        Assert.Equal("Export failed", dialog.Title);
        Assert.StartsWith("line 11\n", dialog.LogText, StringComparison.Ordinal);
        Assert.EndsWith("line 40", dialog.LogText, StringComparison.Ordinal);
        Assert.Equal("ffmpeg -i x", dialog.Command);
        Assert.Contains("Command:\nffmpeg -i x", dialog.DetailsText, StringComparison.Ordinal);
    }

    [Fact]
    public void AFinishedExportReReadsItsOutputPathAndShowsItsWarnings()
    {
        var item = Item();
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        item.Finish(ExportJobState.Completed, null, [], "ffmpeg", "C:/out/trip (2).mp4", ["The output was kept as trip (2).mp4."]);

        Assert.Equal("C:/out/trip (2).mp4", item.OutputPath);
        Assert.Equal("trip (2).mp4", item.Title);
        Assert.True(item.HasWarnings);
        Assert.Equal("The output was kept as trip (2).mp4.", item.WarningText);
        Assert.Contains(nameof(JobItemViewModel.OutputPath), changed);
        Assert.Contains(nameof(JobItemViewModel.Title), changed);
        Assert.True(item.ShowOpenFolder);
    }

    [Fact]
    public void AFinishWithoutAPathKeepsTheOneKnownAtCreation()
    {
        var item = Item();
        item.Finish(ExportJobState.Completed, null, [], null);
        Assert.Equal("C:/out/trip.mp4", item.OutputPath);
        Assert.Equal("trip.mp4", item.Title);
        Assert.False(item.HasWarnings);
    }

    [Fact]
    public async Task TheExportRowFollowsTheJobsFinalPathAndWarnings()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Out");
        var request = new ExportRequest(
            Cutlass.Tests.Compiler.CompilerKit.WorkedExample(),
            new Cutlass.Tests.Compiler.TestCatalog(),
            Cutlass.Compiler.Export.ExportPresets.H264,
            Cutlass.Compiler.Export.ExportQuality.High,
            1920,
            1080,
            0,
            270,
            true,
            Path.Combine(folder, "final.mp4"));
        var job = new ExportJob(request);
        var item = JobItemViewModel.ForExport(job, dialogs: null, post: a => a());
        Assert.Equal("final.mp4", item.Title);

        // The target was locked, so the service kept the file under a free name and said so.
        var kept = Path.Combine(folder, "final (2).mp4");
        job.SetOutputPath(kept, "final.mp4 was in use; the export was saved as final (2).mp4.");
        var finished = new TaskCompletionSource();
        item.Finished += (_, _) => finished.TrySetResult();
        job.Finish(ExportJobState.Completed, null);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(kept, item.OutputPath);
        Assert.Equal("final (2).mp4", item.Title);
        Assert.Equal(new[] { "final.mp4 was in use; the export was saved as final (2).mp4." }, item.Warnings);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(41.2, "0:42")]
    [InlineData(3725, "1:02:05")]
    public void FormatsEta(double seconds, string expected) =>
        Assert.Equal(expected, JobItemViewModel.FormatEta(TimeSpan.FromSeconds(seconds)));
}
