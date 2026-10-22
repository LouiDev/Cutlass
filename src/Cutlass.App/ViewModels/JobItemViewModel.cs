using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services;
using Cutlass.Media.Export;
using Cutlass.Media.Jobs;

namespace Cutlass.App.ViewModels;

/// <summary>
/// One row of the jobs popover (DESIGN 4.5): progress, speed, ETA, cancel, open folder and, on failure, Details.
/// Updates arrive through <see cref="Apply"/> and <see cref="Finish"/> on the UI thread. A finished export re-reads its
/// output path (it may have landed as <c>name (n).ext</c> when the target was locked) and shows its warnings.
/// </summary>
public sealed partial class JobItemViewModel : ObservableObject
{
    private readonly Action? _cancel;
    private readonly IDialogService? _dialogs;
    private string _title;
    private string? _outputPath;

    /// <summary>Creates a row.</summary>
    /// <param name="kind">The job kind shown first, for example "Export".</param>
    /// <param name="title">The job title, for example the output file name.</param>
    /// <param name="outputPath">The file the job writes, or null.</param>
    /// <param name="cancel">Cancels the job, or null when it cannot be cancelled.</param>
    /// <param name="dialogs">Opens folders and the error dialog, or null (tests).</param>
    public JobItemViewModel(string kind, string title, string? outputPath, Action? cancel, IDialogService? dialogs)
    {
        Kind = kind;
        _title = title;
        _outputPath = outputPath;
        _cancel = cancel;
        _dialogs = dialogs;
        State = ExportJobState.Queued;
        StatusText = "Queued";
    }

    /// <summary>Raised once the job reached Completed, Failed or Canceled.</summary>
    public event EventHandler? Finished;

    /// <summary>The job kind, for example "Export".</summary>
    public string Kind { get; }

    /// <summary>The job title (for an export, the output file name, updated when the export landed elsewhere).</summary>
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>
    /// The file the job writes, or null. For an export it is re-read on completion, since the file may have been kept
    /// under a free name when the target was locked.
    /// </summary>
    public string? OutputPath
    {
        get => _outputPath;
        private set
        {
            if (SetProperty(ref _outputPath, value))
            {
                OnPropertyChanged(nameof(ShowOpenFolder));
                OnPropertyChanged(nameof(HasFooter));
                OpenFolderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Non-fatal problems the job reported (offline media, fallback fonts, a renamed output), shown under the row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    [NotifyPropertyChangedFor(nameof(WarningText))]
    public partial IReadOnlyList<string> Warnings { get; set; } = [];

    /// <summary>True when the job reported warnings.</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>The warnings, one per line.</summary>
    public string WarningText => string.Join(Environment.NewLine, Warnings);

    /// <summary>The scheduler job id, or -1 for an export.</summary>
    public long JobId { get; init; } = -1;

    /// <summary>The media a scheduler job works on, or null for an export.</summary>
    public string? MediaId { get; init; }

    /// <summary>True when a failure opens the error dialog at once (exports); media jobs only offer Details.</summary>
    public bool ShowErrorWhenFailed { get; init; } = true;

    /// <summary>The error of a failed job.</summary>
    public Exception? Error { get; private set; }

    /// <summary>The ffmpeg log of a finished job.</summary>
    public IReadOnlyList<string> Log { get; private set; } = [];

    /// <summary>The ffmpeg command, once known.</summary>
    public string? Command { get; private set; }

    /// <summary>The job state.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(ShowOpenFolder))]
    [NotifyPropertyChangedFor(nameof(HasFooter))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(DetailsCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    public partial ExportJobState State { get; set; }

    /// <summary>The fraction done, 0 to 1.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Percent))]
    public partial double Fraction { get; set; }

    /// <summary>"Queued", "43%", "Done", "Failed" or "Cancelled".</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; }

    /// <summary>The speed, for example "2.4x", or empty.</summary>
    [ObservableProperty]
    public partial string SpeedText { get; set; } = string.Empty;

    /// <summary>The time left, for example "ETA 0:42", or empty.</summary>
    [ObservableProperty]
    public partial string EtaText { get; set; } = string.Empty;

    /// <summary>The fraction as a percentage for progress bars.</summary>
    public double Percent => Math.Round(Fraction * 100, 1);

    /// <summary>True while queued or running.</summary>
    public bool IsActive => State is ExportJobState.Queued or ExportJobState.Running;

    /// <summary>True once completed, failed or cancelled.</summary>
    public bool IsFinished => !IsActive;

    /// <summary>True when the job failed.</summary>
    public bool IsFailed => State == ExportJobState.Failed;

    /// <summary>True when the job completed.</summary>
    public bool IsCompleted => State == ExportJobState.Completed;

    /// <summary>True when the job completed and wrote a file (Open folder shows).</summary>
    public bool ShowOpenFolder => IsCompleted && OutputPath is not null;

    /// <summary>
    /// True when the row needs its second line (speed, ETA and the Cancel, Open folder or Details button). Finished
    /// media jobs show only their title line, which keeps the popover dense (DESIGN 8).
    /// </summary>
    public bool HasFooter => IsActive || ShowOpenFolder || IsFailed;

    /// <summary>Creates a row that follows an export job, marshalling its updates through <paramref name="post"/>.</summary>
    /// <param name="job">The job.</param>
    /// <param name="dialogs">The dialog service.</param>
    /// <param name="post">Runs an action on the UI thread.</param>
    /// <returns>The row.</returns>
    public static JobItemViewModel ForExport(ExportJob job, IDialogService? dialogs, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(post);
        var item = new JobItemViewModel("Export", Path.GetFileName(job.OutputPath), job.OutputPath, job.Cancel, dialogs);
        job.ProgressChanged += (_, progress) => post(() => item.Apply(progress));
        job.Completion.ContinueWith(
            _ => post(() => item.Finish(job.State, job.Error, job.Log, job.DisplayCommand, job.OutputPath, job.Warnings)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return item;
    }

    /// <summary>Creates a row for a scheduler job (index, thumbnails, waveform, proxy); feed it with <see cref="Apply(JobInfo, TimeSpan)"/>.</summary>
    /// <param name="info">The first snapshot.</param>
    /// <param name="cancel">Cancels the job.</param>
    /// <param name="dialogs">The dialog service, or null (tests).</param>
    /// <returns>The row.</returns>
    public static JobItemViewModel ForMediaJob(JobInfo info, Action? cancel, IDialogService? dialogs)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new JobItemViewModel(KindName(info.Kind), info.MediaName, null, cancel, dialogs)
        {
            JobId = info.Id,
            MediaId = info.MediaId,
            ShowErrorWhenFailed = false,
        };
    }

    /// <summary>The display name of a scheduler job kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>For example "Index" or "Proxy".</returns>
    public static string KindName(JobKind kind) => kind switch
    {
        JobKind.FrameIndex => "Index",
        JobKind.Thumbnails => "Thumbnails",
        JobKind.Waveform => "Waveform",
        _ => "Proxy",
    };

    /// <summary>Maps a scheduler state onto the row states (which share the export job's values).</summary>
    /// <param name="state">The scheduler state.</param>
    /// <returns>The row state.</returns>
    public static ExportJobState MapState(JobState state) => state switch
    {
        JobState.Queued => ExportJobState.Queued,
        JobState.Running => ExportJobState.Running,
        JobState.Completed => ExportJobState.Completed,
        JobState.Failed => ExportJobState.Failed,
        _ => ExportJobState.Canceled,
    };

    /// <summary>
    /// Applies a scheduler snapshot: progress while active (with an ETA estimated from the progress so far), then the
    /// final state once.
    /// </summary>
    /// <param name="info">The snapshot.</param>
    /// <param name="elapsed">The time since the job was first seen running.</param>
    public void Apply(JobInfo info, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (IsFinished)
        {
            return;
        }

        var state = MapState(info.State);
        if (info.IsFinished)
        {
            Finish(state, info.Error is { } error ? new InvalidOperationException(error) : null, null, null);
            return;
        }

        State = state;
        Fraction = Math.Clamp(info.Fraction, 0, 1);
        StatusText = state == ExportJobState.Queued
            ? "Queued"
            : (Fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

        // Unspecified how media jobs estimate: linear from the progress so far, once it is meaningful.
        EtaText = state == ExportJobState.Running && Fraction >= 0.05 && Fraction < 1 && elapsed > TimeSpan.Zero
            ? "ETA " + FormatEta(elapsed * ((1 - Fraction) / Fraction))
            : string.Empty;
    }

    /// <summary>Formats a time left as <c>m:ss</c> or <c>h:mm:ss</c>.</summary>
    /// <param name="eta">The time left.</param>
    /// <returns>The text.</returns>
    public static string FormatEta(TimeSpan eta)
    {
        var total = (long)Math.Ceiling(Math.Max(0, eta.TotalSeconds));
        var h = total / 3600;
        var m = total / 60 % 60;
        var s = total % 60;
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{m}:{s:00}");
    }

    /// <summary>Applies a progress report. Ignored once the job finished.</summary>
    /// <param name="progress">The report.</param>
    public void Apply(ExportProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (IsFinished)
        {
            return;
        }

        State = progress.State is ExportJobState.Queued or ExportJobState.Running ? progress.State : ExportJobState.Running;
        Fraction = Math.Clamp(progress.Fraction, 0, 1);
        SpeedText = progress.Speed is { } speed ? speed.ToString("0.0", CultureInfo.InvariantCulture) + "x" : string.Empty;
        EtaText = progress.Eta is { } eta ? "ETA " + FormatEta(eta) : string.Empty;
        StatusText = State == ExportJobState.Queued
            ? "Queued"
            : (Fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>Records the final state. Raises <see cref="Finished"/> once.</summary>
    /// <param name="state">Completed, Failed or Canceled.</param>
    /// <param name="error">The error of a failed job.</param>
    /// <param name="log">The ffmpeg log.</param>
    /// <param name="command">The ffmpeg command.</param>
    /// <param name="outputPath">
    /// The file the job finally wrote, or null to keep the one known at creation. An export may land as
    /// <c>name (n).ext</c> when the target was locked; a completed row then shows and opens that file.
    /// </param>
    /// <param name="warnings">The job's non-fatal warnings, or null for none.</param>
    public void Finish(
        ExportJobState state,
        Exception? error,
        IReadOnlyList<string>? log,
        string? command,
        string? outputPath = null,
        IReadOnlyList<string>? warnings = null)
    {
        if (IsFinished || state is ExportJobState.Queued or ExportJobState.Running)
        {
            return;
        }

        Error = error;
        Log = log ?? [];
        Command = command;
        Warnings = warnings ?? [];
        if (outputPath is not null && !string.Equals(outputPath, OutputPath, StringComparison.Ordinal))
        {
            OutputPath = outputPath;
            Title = Path.GetFileName(outputPath);
        }
        if (state == ExportJobState.Completed)
        {
            Fraction = 1;
        }

        SpeedText = string.Empty;
        EtaText = string.Empty;
        StatusText = state switch
        {
            ExportJobState.Completed => "Done",
            ExportJobState.Failed => "Failed",
            _ => "Cancelled",
        };
        State = state;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The error dialog for a failed job.</summary>
    /// <returns>The dialog view model.</returns>
    public ErrorDialogViewModel CreateErrorDialog() =>
        Error is { } error
            ? ErrorDialogViewModel.FromException(Kind + " failed", error, Log, Command)
            : new ErrorDialogViewModel(Kind + " failed", Kind + " " + Title + " failed.", Log, Command);

    private bool CanCancel() => IsActive && _cancel is not null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancel?.Invoke();
        StatusText = "Cancelling";
    }

    private bool CanShowDetails() => IsFailed && _dialogs is not null;

    [RelayCommand(CanExecute = nameof(CanShowDetails))]
    private Task DetailsAsync() => _dialogs?.ShowErrorAsync(CreateErrorDialog()) ?? Task.CompletedTask;

    private bool CanOpenFolder() => IsCompleted && OutputPath is not null && _dialogs is not null;

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private Task OpenFolderAsync() =>
        OutputPath is { } path && _dialogs is not null ? _dialogs.RevealInFileManagerAsync(path) : Task.CompletedTask;
}
