using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Cutlass.Media.Platform;

namespace Cutlass.Media.Ffmpeg;

/// <summary>
/// The only place Cutlass starts a process (TECH_BRIEF 5.3 and 5.4). It uses <c>ArgumentList</c>, no shell and no
/// window, always redirects stdout and stderr (stdin only when the spec asks), drains stderr, lowers the priority of
/// background jobs, and puts every child into the Windows Job Object or the pid registry.
/// </summary>
public sealed class ProcessRunner
{
    private readonly WindowsJobObject? _job;

    /// <summary>Creates a runner.</summary>
    /// <param name="registry">Tracks children for shutdown and orphan cleanup.</param>
    /// <param name="job">The kill-on-close job every child joins (Windows only), or null.</param>
    public ProcessRunner(ChildProcessRegistry registry, WindowsJobObject? job)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Registry = registry;
        _job = job;
    }

    /// <summary>The registry every child is recorded in.</summary>
    public ChildProcessRegistry Registry { get; }

    /// <summary>True when children are assigned to a Windows Job Object.</summary>
    public bool UsesJobObject => _job is not null;

    /// <summary>Creates a runner for this OS: with a Job Object on Windows, registry only elsewhere.</summary>
    /// <param name="registry">The registry.</param>
    /// <returns>The runner.</returns>
    public static ProcessRunner CreateDefault(ChildProcessRegistry registry) =>
        new(registry, OperatingSystem.IsWindows() ? WindowsJobObject.TryCreate() : null);

    /// <summary>
    /// Formats a command for display only: POSIX single quotes when <paramref name="posix"/> is true, Windows double
    /// quotes otherwise. Never execute the result.
    /// </summary>
    /// <param name="fileName">The executable.</param>
    /// <param name="args">The arguments.</param>
    /// <param name="posix">True for POSIX quoting.</param>
    /// <returns>The display text.</returns>
    public static string FormatDisplayCommand(string fileName, IEnumerable<string> args, bool posix)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(args);
        var sb = new StringBuilder(Quote(fileName, posix));
        foreach (var arg in args)
        {
            sb.Append(' ').Append(Quote(arg, posix));
        }

        return sb.ToString();
    }

    /// <summary>Starts a process.</summary>
    /// <param name="spec">What to run.</param>
    /// <returns>The running process. The caller owns it and must dispose it.</returns>
    /// <exception cref="FfmpegException">When the executable cannot be started.</exception>
    public RunningProcess Start(ProcessSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var display = FormatDisplayCommand(spec.FileName, spec.Args, posix: !OperatingSystem.IsWindows());
        var psi = new ProcessStartInfo(spec.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = spec.RedirectStdin,
            WorkingDirectory = spec.WorkingDirectory,
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        if (spec.RedirectStdin)
        {
            psi.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        foreach (var arg in spec.Args)
        {
            psi.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            process.Dispose();
            throw new FfmpegException(
                $"Could not start {Path.GetFileName(spec.FileName)}: {ex.Message}",
                FfmpegException.NoExitCode,
                [],
                display,
                ex);
        }

        if (OperatingSystem.IsWindows() && _job is not null)
        {
            _job.TryAssign(process);
        }

        Registry.Register(process);

        if (spec.BelowNormalPriority)
        {
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Priority is a courtesy; the job still runs at normal priority.
            }
        }

        return new RunningProcess(process, spec, display, Registry);
    }

    /// <summary>
    /// Runs a process to completion, collecting stdout. Cancellation stops it like <see cref="RunningProcess.StopAsync"/>
    /// (<c>q</c> first when stdin is open, then a kill) and throws <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="spec">What to run.</param>
    /// <param name="timeout">Kill the process after this long, or null for no limit.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The exit code, stdout and the stderr tail. A non-zero exit code is not an error here; see
    /// <see cref="ProcessResult.EnsureSuccess"/>.</returns>
    /// <exception cref="FfmpegException">When the process cannot start or exceeds <paramref name="timeout"/>.</exception>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/> fires.</exception>
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var running = Start(spec);
        using var buffer = new MemoryStream();
        var stdoutCopy = running.Stdout.CopyToAsync(buffer, CancellationToken.None);

        using var timeoutCts = timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await running.Exited.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await running.StopAsync().ConfigureAwait(false);
            await IgnoreFailure(stdoutCopy).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw new FfmpegException(
                $"{Path.GetFileName(spec.FileName)} did not finish within {timeout?.TotalSeconds:0.#} s and was stopped.",
                FfmpegException.NoExitCode,
                running.StderrTail(ProcessResult.ExceptionTailLines),
                running.DisplayCommand);
        }

        await IgnoreFailure(stdoutCopy).ConfigureAwait(false);
        var exitCode = await running.Exited.ConfigureAwait(false);
        return new ProcessResult(exitCode, buffer.ToArray(), running.StderrTail(RunningProcess.StderrCapacity), running.DisplayCommand);
    }

    private static async Task IgnoreFailure(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The pipe broke because the process was stopped.
        }
    }

    private static string Quote(string arg, bool posix)
    {
        if (arg.Length > 0 && arg.All(c => char.IsAsciiLetterOrDigit(c) || "-_./:=+,@%".Contains(c, StringComparison.Ordinal)))
        {
            return arg;
        }

        if (posix)
        {
            return "'" + arg.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        }

        return "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
