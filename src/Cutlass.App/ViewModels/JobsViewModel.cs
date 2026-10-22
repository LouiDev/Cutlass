using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services;
using Cutlass.Media.Export;
using Cutlass.Media.Jobs;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The jobs popover and the status bar job summary (DESIGN 4.5): exports plus the scheduler's index, thumbnail,
/// waveform and proxy jobs (probe and poster run inside the import). A failed export opens the error dialog (DESIGN 9).
/// </summary>
public sealed partial class JobsViewModel : ObservableObject
{
    private readonly IDialogService? _dialogs;
    private readonly Action<Action> _post;
    private readonly Dictionary<long, MediaRow> _media = [];
    private JobScheduler? _scheduler;
    private int _batchTotal;
    private int _batchDone;

    /// <summary>Creates the view model.</summary>
    /// <param name="dialogs">Shows error dialogs and folders, or null (tests).</param>
    /// <param name="post">Runs an action on the UI thread (tests pass a direct call).</param>
    public JobsViewModel(IDialogService? dialogs, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        _dialogs = dialogs;
        _post = post;
        Summary = "No jobs";
    }

    /// <summary>Raised on the UI thread with every scheduler snapshot (the media bin follows proxy progress).</summary>
    public event EventHandler<JobInfo>? MediaJobChanged;

    /// <summary>Every job of this session, newest first.</summary>
    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];

    /// <summary>The status bar text, for example "Jobs: export 1/2 (43%)".</summary>
    [ObservableProperty]
    public partial string Summary { get; set; }

    /// <summary>True while a job is queued or running.</summary>
    [ObservableProperty]
    public partial bool HasActiveJobs { get; set; }

    /// <summary>True while the popover is open.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>True when there is at least one job to list.</summary>
    public bool HasJobs => Jobs.Count > 0;

    /// <summary>Adds a row for an export job.</summary>
    /// <param name="job">The job.</param>
    /// <returns>The row.</returns>
    public JobItemViewModel AddExport(ExportJob job) => Add(JobItemViewModel.ForExport(job, _dialogs, _post));

    /// <summary>Adds a row and follows its progress.</summary>
    /// <param name="item">The row.</param>
    /// <returns>The row.</returns>
    public JobItemViewModel Add(JobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Jobs.Any(j => j.IsActive))
        {
            _batchTotal = 0;
            _batchDone = 0;
        }

        _batchTotal++;
        Jobs.Insert(0, item);
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(JobItemViewModel.Fraction) or nameof(JobItemViewModel.State))
            {
                UpdateSummary();
            }
        };
        item.Finished += OnItemFinished;
        OnPropertyChanged(nameof(HasJobs));
        UpdateSummary();
        return item;
    }

    /// <summary>
    /// Lists the scheduler's media jobs (TECH_BRIEF 6.7): every snapshot updates or adds its row on the UI thread, and
    /// a row's Cancel cancels the job.
    /// </summary>
    /// <param name="scheduler">The scheduler.</param>
    public void AttachScheduler(JobScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
        scheduler.JobChanged += info => _post(() => Apply(info));
        foreach (var info in scheduler.Jobs.Where(j => !j.IsFinished))
        {
            Apply(info);
        }
    }

    /// <summary>Applies a scheduler snapshot (UI thread): updates the job's row, or adds one for a new job.</summary>
    /// <param name="info">The snapshot.</param>
    public void Apply(JobInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!_media.TryGetValue(info.Id, out var entry))
        {
            if (info.IsFinished && info.State != JobState.Failed)
            {
                // A job that answered from the cache before its row existed needs no row.
                MediaJobChanged?.Invoke(this, info);
                return;
            }

            var id = info.Id;
            var row = JobItemViewModel.ForMediaJob(info, () => _scheduler?.Cancel(id), _dialogs);
            entry = new MediaRow(row, new Stopwatch());
            _media[id] = entry;
            Add(row);
        }

        if (info.State == JobState.Running && !entry.Clock.IsRunning)
        {
            entry.Clock.Start();
        }

        entry.Row.Apply(info, entry.Clock.Elapsed);
        if (info.IsFinished)
        {
            _media.Remove(info.Id);
        }

        MediaJobChanged?.Invoke(this, info);
    }

    private void OnItemFinished(object? sender, EventArgs e)
    {
        _batchDone++;
        UpdateSummary();
        if (sender is JobItemViewModel { IsFailed: true, ShowErrorWhenFailed: true } item && _dialogs is not null)
        {
            _ = _dialogs.ShowErrorAsync(item.CreateErrorDialog());
        }
    }

    private void UpdateSummary()
    {
        var running = Jobs.FirstOrDefault(j => j.State == ExportJobState.Running) ?? Jobs.LastOrDefault(j => j.IsActive);
        HasActiveJobs = running is not null;
        if (running is null)
        {
            var last = Jobs.FirstOrDefault();
            Summary = last is null ? "No jobs" : $"Jobs: {last.Kind.ToLowerInvariant()} {last.StatusText.ToLowerInvariant()}";
            return;
        }

        var position = Math.Min(_batchTotal, _batchDone + 1);
        Summary = string.Create(
            CultureInfo.InvariantCulture,
            $"Jobs: {running.Kind.ToLowerInvariant()} {position}/{_batchTotal} ({Math.Round(running.Fraction * 100):0}%)");
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void ClearFinished()
    {
        _scheduler?.ClearFinished();
        foreach (var done in Jobs.Where(j => j.IsFinished).ToList())
        {
            Jobs.Remove(done);
        }

        OnPropertyChanged(nameof(HasJobs));
        UpdateSummary();
    }

    /// <summary>A scheduler job's row and the time it has been running.</summary>
    /// <param name="Row">The row.</param>
    /// <param name="Clock">Runs from the first Running snapshot (ETA).</param>
    private sealed record MediaRow(JobItemViewModel Row, Stopwatch Clock);
}
