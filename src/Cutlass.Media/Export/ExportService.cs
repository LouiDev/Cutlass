using System.Text;
using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Emit;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Parsing;
using Cutlass.Media.Platform;

namespace Cutlass.Media.Export;

/// <summary>
/// Runs exports one at a time (TECH_BRIEF 9). Each job compiles the window with <c>Purpose=Export</c>, writes its aux
/// files and the <c>g_&lt;sha12&gt;.ffg</c> script into the work directory, runs ffmpeg with stdin open,
/// <c>-progress pipe:1 -nostats</c> and BT.709 tags into <c>name.partial.ext</c> (<c>name.partial-&lt;8 hex&gt;.ext</c>
/// when that name is taken: a file the export did not write is never deleted), and renames it on success. Cancel
/// writes <c>q</c>, waits 3 s, kills the tree and deletes the partial file; a failure deletes it too and reports an
/// <see cref="FfmpegException"/> with the last 30 log lines. A finished file is never deleted: when the
/// rename keeps failing (the target is open in a player) it is kept under a free name such as <c>name (1).ext</c>,
/// which becomes the job's output with a warning. The output and the partial file must not be a media file of the
/// project, compared by file identity (<see cref="FileIdentity"/>).
/// </summary>
public sealed class ExportService : IAsyncDisposable
{
    /// <summary>How often the final rename is tried before the file is kept under a free name.</summary>
    public const int RenameAttempts = 10;

    /// <summary>How long a cancelled ffmpeg gets after <c>q</c> before it is killed.</summary>
    public static readonly TimeSpan QuitGrace = TimeSpan.FromSeconds(3);

    /// <summary>The pause between two rename attempts.</summary>
    public static readonly TimeSpan RenameRetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly ProcessRunner _runner;
    private readonly FfmpegToolset _toolset;
    private readonly FfmpegCapabilities _capabilities;
    private readonly WorkDirectory _work;
    private readonly FontLocator _fonts;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly List<ExportJob> _jobs = [];
    private readonly List<Task> _tasks = [];
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>Creates the service.</summary>
    /// <param name="runner">Starts ffmpeg.</param>
    /// <param name="toolset">The validated ffmpeg.</param>
    /// <param name="capabilities">What the build can do (script syntax, encoders, drawtext).</param>
    /// <param name="work">The work directory every export runs in.</param>
    /// <param name="fonts">Places title fonts into the work directory.</param>
    public ExportService(
        ProcessRunner runner,
        FfmpegToolset toolset,
        FfmpegCapabilities capabilities,
        WorkDirectory work,
        FontLocator fonts)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(toolset);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(fonts);
        _runner = runner;
        _toolset = toolset;
        _capabilities = capabilities;
        _work = work;
        _fonts = fonts;
    }

    /// <summary>Every job queued so far, oldest first.</summary>
    public IReadOnlyList<ExportJob> Jobs
    {
        get
        {
            lock (_gate)
            {
                return [.. _jobs];
            }
        }
    }

    /// <summary>Queues an export. It starts when the exports queued before it have finished.</summary>
    /// <param name="request">The export.</param>
    /// <returns>The job.</returns>
    /// <exception cref="ObjectDisposedException">When the service was disposed.</exception>
    public ExportJob Enqueue(ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var job = new ExportJob(request);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _jobs.Add(job);
            _tasks.Add(Task.Run(() => RunAsync(job)));
        }

        return job;
    }

    /// <summary>Cancels every job and waits for them to finish.</summary>
    /// <returns>A task that completes when every job has finished.</returns>
    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var job in _jobs)
            {
                job.Cancel();
            }

            tasks = [.. _tasks];
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        _oneAtATime.Dispose();
    }

    private async Task RunAsync(ExportJob job)
    {
        try
        {
            await _oneAtATime.WaitAsync(job.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            job.Finish(ExportJobState.Canceled, null);
            return;
        }

        try
        {
            await ExecuteAsync(job).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (job.IsCancellationRequested)
        {
            await DeleteWithRetryAsync(job.PartialPath).ConfigureAwait(false);
            job.Finish(ExportJobState.Canceled, null);
        }
        catch (Exception ex)
        {
            await DeleteWithRetryAsync(job.PartialPath).ConfigureAwait(false);
            job.Finish(ExportJobState.Failed, ex);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task ExecuteAsync(ExportJob job)
    {
        var request = job.Request;
        var token = job.CancellationToken;
        job.MarkRunning();

        var preset = request.Preset;
        var encoding = preset.Resolve(_capabilities) ?? throw new InvalidOperationException(
            $"The {preset.DisplayName} preset is not available: {preset.UnavailableReason(_capabilities)}.");

        var (catalog, fontWarnings) = await ResolveFontsAsync(request, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var render = TimelineCompiler.Compile(request.Project, catalog, request.ToRenderRequest());
        job.SetCompiled(render, [.. fontWarnings, .. render.Warnings]);
        if (!_capabilities.HasDrawtext && render.GraphText.Contains("drawtext=", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("This ffmpeg build has no drawtext filter, so titles cannot be exported.");
        }

        _work.EnsureCreated();
        foreach (var aux in render.AuxFiles)
        {
            _work.WriteFile(aux.FileName, aux.Utf8Content);
        }

        var script = FfmpegArgsBuilder.UseScriptFile(RenderPurpose.Export, render.GraphText, _capabilities.ScriptSyntax)
            ? _work.WriteScript(render.GraphText)
            : null;
        var graphArguments = FfmpegArgsBuilder.Graph(render.GraphText, script, _capabilities.ScriptSyntax);

        var final = request.FinalPath;
        var partial = job.PartialPath;
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        while (File.Exists(partial) || Directory.Exists(partial))
        {
            // Never delete a file the export did not write: pick another partial name instead.
            partial = request.PartialPathFor(Guid.NewGuid().ToString("N")[..8]);
            job.SetPartialPath(partial);
        }

        if (request.Project.Media.FirstOrDefault(m => FileIdentity.SameFile(m.Path, final) || FileIdentity.SameFile(m.Path, partial)) is { } clash)
        {
            throw new InvalidOperationException($"The output would replace {clash.Path}, a media file of this project.");
        }

        var rate = request.Project.Sequence.Settings.Rate;
        var args = FfmpegArgsBuilder.Export(render, encoding, request.Quality, rate, request.Width, request.Height, graphArguments, partial);
        var spec = new ProcessSpec(_toolset.FfmpegPath, args, _work.Path, RedirectStdin: true, ProcessPurpose.Export, BelowNormalPriority: false);
        var totalMicros = TimeMath.FloorMicros(TimeMath.SecondsAt(request.EndFrame - request.StartFrame, rate));

        int exitCode;
        ProgressBlock? last;
        await using (var process = _runner.Start(spec))
        {
            job.SetCommand(process.DisplayCommand);
            var parser = new ProgressParser();
            var reader = ReadProgressAsync(process.Stdout, parser, job, totalMicros);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelled.TrySetResult()))
            {
                if (await Task.WhenAny(process.Exited, cancelled.Task).ConfigureAwait(false) != process.Exited)
                {
                    // Graceful cancel: q on stdin, then a kill after the grace period.
                    await process.StopAsync(QuitGrace).ConfigureAwait(false);
                }
            }

            exitCode = await process.Exited.ConfigureAwait(false);
            await IgnorePipeFailure(reader).ConfigureAwait(false);
            last = parser.Last;
            job.SetLog(process.StderrTail(RunningProcess.StderrCapacity));
            if (!token.IsCancellationRequested && (exitCode != 0 || last?.IsEnd != true || !File.Exists(partial)))
            {
                throw exitCode != 0
                    ? new FfmpegException(exitCode, process.StderrTail(ProcessResult.ExceptionTailLines), process.DisplayCommand)
                    : new FfmpegException(
                        "ffmpeg finished without completing the export.",
                        exitCode,
                        process.StderrTail(ProcessResult.ExceptionTailLines),
                        process.DisplayCommand);
            }
        }

        token.ThrowIfCancellationRequested();
        var (output, warning) = await CommitAsync(partial, final).ConfigureAwait(false);
        if (warning is not null)
        {
            job.SetOutputPath(output, warning);
        }

        job.Finish(ExportJobState.Completed, null);
    }

    /// <summary>
    /// Renames a finished partial file onto the final path, retrying briefly; when that keeps failing, renames it to
    /// the first free <c>name (n).ext</c> next to it, and as a last resort leaves it where it is. Never deletes it.
    /// </summary>
    /// <param name="partial">The finished partial file.</param>
    /// <param name="final">The requested output.</param>
    /// <returns>Where the output ended up, and a warning when that is not <paramref name="final"/>.</returns>
    internal static async Task<(string Output, string? Warning)> CommitAsync(string partial, string final)
    {
        string? reason = null;
        for (var attempt = 0; attempt < RenameAttempts; attempt++)
        {
            try
            {
                File.Move(partial, final, overwrite: true);
                return (final, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                reason = ex.Message;
                if (attempt + 1 < RenameAttempts)
                {
                    await Task.Delay(RenameRetryDelay).ConfigureAwait(false);
                }
            }
        }

        var directory = Path.GetDirectoryName(final) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(final);
        var extension = Path.GetExtension(final);
        reason = (reason ?? "it is in use").TrimEnd('.', ' ');
        for (var n = 1; n <= 999; n++)
        {
            var candidate = Path.Combine(directory, name + " (" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" + extension);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                continue;
            }

            try
            {
                File.Move(partial, candidate, overwrite: false);
                return (candidate, $"{Path.GetFileName(final)} could not be replaced ({reason}), so the export was saved as {Path.GetFileName(candidate)}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!File.Exists(partial))
                {
                    break;
                }
            }
        }

        return (partial, $"{Path.GetFileName(final)} could not be replaced ({reason}), so the export was kept as {Path.GetFileName(partial)}.");
    }

    // Shared with the dialog's "Show ffmpeg command" (ExportCommandPreview), so both resolve fonts the same way.
    private Task<(IMediaCatalog Catalog, IReadOnlyList<string> Warnings)> ResolveFontsAsync(ExportRequest request, CancellationToken token) =>
        ExportFontResolver.ResolveAsync(request, _fonts, token);

    private static async Task ReadProgressAsync(Stream stdout, ProgressParser parser, ExportJob job, long totalMicros)
    {
        using var reader = new StreamReader(stdout, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var buffer = new char[4096];
        ExportProgress? previous = null;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false)) > 0)
        {
            foreach (var block in parser.Feed(buffer.AsSpan(0, read)))
            {
                previous = ExportProgress.FromBlock(block, totalMicros, previous);
                job.ReportRunning(previous);
            }
        }
    }

    private static async Task IgnorePipeFailure(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The pipe broke because the process was stopped.
        }
    }

    // A killed ffmpeg can hold the file for a moment on Windows, so the delete is retried briefly.
    private static async Task DeleteWithRetryAsync(string path)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }
}
