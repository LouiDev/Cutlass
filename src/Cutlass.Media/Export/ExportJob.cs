using System.Diagnostics;
using Cutlass.Compiler;

namespace Cutlass.Media.Export;

/// <summary>
/// One queued export (TECH_BRIEF 9): its state, progress, ETA, speed, error and <see cref="Cancel"/>. Updated from the
/// export task; read from any thread. Progress reaches <see cref="ExportRequest.Progress"/> and
/// <see cref="ProgressChanged"/> at most 10 times a second, plus every state change.
/// </summary>
public sealed class ExportJob
{
    /// <summary>The minimum interval between two running progress reports (10 Hz).</summary>
    public static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    private readonly CancellationTokenSource _cancel = new();
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _sinceReport = new();
    private readonly object _gate = new();
    private ExportProgress _progress = new(ExportJobState.Queued, 0, null, null, 0);
    private Exception? _error;
    private string? _displayCommand;
    private CompiledRender? _render;
    private IReadOnlyList<string> _log = [];
    private IReadOnlyList<string> _warnings = [];
    private string _outputPath;
    private string _partialPath;

    internal ExportJob(ExportRequest request)
    {
        Request = request;
        _outputPath = request.FinalPath;
        _partialPath = request.PartialPath;
    }

    /// <summary>Raised with each (throttled) report, on the export task's thread.</summary>
    public event EventHandler<ExportProgress>? ProgressChanged;

    /// <summary>A unique id.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>The request.</summary>
    public ExportRequest Request { get; }

    /// <summary>
    /// The output path: the requested final path, or, when the finished file could not be renamed onto it (the target
    /// open in a player), the free name it was kept under (a warning says so).
    /// </summary>
    public string OutputPath
    {
        get
        {
            lock (_gate)
            {
                return _outputPath;
            }
        }
    }

    /// <summary>
    /// The path ffmpeg writes to until the export succeeds: <c>name.partial.ext</c>, or <c>name.partial-&lt;8 hex&gt;.ext</c>
    /// when a file already had the plain name.
    /// </summary>
    public string PartialPath
    {
        get
        {
            lock (_gate)
            {
                return _partialPath;
            }
        }
    }

    /// <summary>The latest progress report.</summary>
    public ExportProgress Progress
    {
        get
        {
            lock (_gate)
            {
                return _progress;
            }
        }
    }

    /// <summary>The current state.</summary>
    public ExportJobState State => Progress.State;

    /// <summary>The fraction done, 0 to 1.</summary>
    public double Fraction => Progress.Fraction;

    /// <summary>The estimated time left, or null.</summary>
    public TimeSpan? Eta => Progress.Eta;

    /// <summary>The encoding speed as a multiple of real time, or null.</summary>
    public double? Speed => Progress.Speed;

    /// <summary>Why the export failed (an <see cref="Ffmpeg.FfmpegException"/> for ffmpeg failures), or null.</summary>
    public Exception? Error
    {
        get
        {
            lock (_gate)
            {
                return _error;
            }
        }
    }

    /// <summary>The ffmpeg command line for display ("Show ffmpeg command", error details), once started.</summary>
    public string? DisplayCommand
    {
        get
        {
            lock (_gate)
            {
                return _displayCommand;
            }
        }
    }

    /// <summary>The compiled render, once compiled.</summary>
    public CompiledRender? Render
    {
        get
        {
            lock (_gate)
            {
                return _render;
            }
        }
    }

    /// <summary>The last ffmpeg log lines (stderr, up to 300), once ffmpeg exited.</summary>
    public IReadOnlyList<string> Log
    {
        get
        {
            lock (_gate)
            {
                return _log;
            }
        }
    }

    /// <summary>Non-fatal problems: offline media, fallback fonts, many inputs.</summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_gate)
            {
                return _warnings;
            }
        }
    }

    /// <summary>Completes when the job reached Completed, Failed or Canceled. It never faults; check
    /// <see cref="State"/> and <see cref="Error"/>.</summary>
    public Task Completion => _done.Task;

    /// <summary>True once <see cref="Cancel"/> was called.</summary>
    public bool IsCancellationRequested => _cancel.IsCancellationRequested;

    internal CancellationToken CancellationToken => _cancel.Token;

    /// <summary>
    /// Cancels the job: a queued job never starts; a running one gets <c>q</c>, then a kill after 3 s, and its
    /// partial file is deleted.
    /// </summary>
    public void Cancel()
    {
        try
        {
            _cancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already finished.
        }
    }

    internal void SetCompiled(CompiledRender render, IReadOnlyList<string> warnings)
    {
        lock (_gate)
        {
            _render = render;
            _warnings = warnings;
        }
    }

    internal void SetPartialPath(string path)
    {
        lock (_gate)
        {
            _partialPath = path;
        }
    }

    internal void SetOutputPath(string path, string warning)
    {
        lock (_gate)
        {
            _outputPath = path;
            _warnings = [.. _warnings, warning];
        }
    }

    internal void SetCommand(string displayCommand)
    {
        lock (_gate)
        {
            _displayCommand = displayCommand;
        }
    }

    internal void SetLog(IReadOnlyList<string> log)
    {
        lock (_gate)
        {
            _log = log;
        }
    }

    internal void MarkRunning() => Publish(Progress with { State = ExportJobState.Running }, force: true);

    internal void ReportRunning(ExportProgress progress) => Publish(progress with { State = ExportJobState.Running }, force: false);

    internal void Finish(ExportJobState state, Exception? error)
    {
        lock (_gate)
        {
            _error = error;
        }

        var last = Progress;
        Publish(last with { State = state, Fraction = state == ExportJobState.Completed ? 1 : last.Fraction, Eta = null }, force: true);
        _done.TrySetResult();
    }

    private void Publish(ExportProgress progress, bool force)
    {
        lock (_gate)
        {
            if (!force && _sinceReport.IsRunning && _sinceReport.Elapsed < ReportInterval)
            {
                // Keep the newest values readable, but report at most 10 times a second.
                _progress = progress;
                return;
            }

            _progress = progress;
            _sinceReport.Restart();
        }

        Request.Progress?.Report(progress);
        ProgressChanged?.Invoke(this, progress);
    }
}
