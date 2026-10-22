using System.ComponentModel;
using System.Diagnostics;
using Cutlass.Media.Platform;

namespace Cutlass.Media.Ffmpeg;

/// <summary>
/// A started ffmpeg or ffprobe child (TECH_BRIEF 5.3). Stderr is drained continuously into a ring of the last 300
/// lines; stdout is exposed as the raw byte stream, which the owner must read (or the child blocks once the pipe is
/// full). Disposing kills the process if it is still running.
/// </summary>
public sealed class RunningProcess : IAsyncDisposable
{
    /// <summary>How many stderr lines the ring keeps.</summary>
    public const int StderrCapacity = 300;

    /// <summary>The default wait between <c>q</c> and the kill when stopping (TECH_BRIEF 9: 3 s).</summary>
    public static readonly TimeSpan DefaultQuitGrace = TimeSpan.FromSeconds(3);

    private const int MaxLineLength = 4096;

    private readonly Process _process;
    private readonly ChildProcessRegistry _registry;
    private readonly Queue<string> _stderr = new(StderrCapacity);
    private readonly object _stderrGate = new();
    private readonly Task _stderrDrain;
    private int _disposed;

    internal RunningProcess(Process process, ProcessSpec spec, string displayCommand, ChildProcessRegistry registry)
    {
        _process = process;
        _registry = registry;
        Spec = spec;
        DisplayCommand = displayCommand;
        Pid = process.Id;
        Stdout = process.StandardOutput.BaseStream;
        _stderrDrain = Task.Run(DrainStderrAsync);
        Exited = WaitForExitAsync();
    }

    /// <summary>The process id.</summary>
    public int Pid { get; }

    /// <summary>The command line, for display only (POSIX quoting on Linux, double quotes on Windows).</summary>
    public string DisplayCommand { get; }

    /// <summary>The spec the process was started from.</summary>
    public ProcessSpec Spec { get; }

    /// <summary>The raw stdout bytes (<c>StandardOutput.BaseStream</c>).</summary>
    public Stream Stdout { get; }

    /// <summary>Completes with the exit code once the process exited and stderr was drained.</summary>
    public Task<int> Exited { get; }

    /// <summary>True once the process has exited.</summary>
    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    /// <summary>The last stderr lines, oldest first.</summary>
    /// <param name="lines">How many lines (at most <see cref="StderrCapacity"/> are kept).</param>
    /// <returns>A snapshot of the tail.</returns>
    public IReadOnlyList<string> StderrTail(int lines)
    {
        lock (_stderrGate)
        {
            var skip = Math.Max(0, _stderr.Count - Math.Max(0, lines));
            return [.. _stderr.Skip(skip)];
        }
    }

    /// <summary>Asks ffmpeg to stop gracefully by writing <c>q</c> to stdin (exports only).</summary>
    /// <returns>A task that completes once the request was written (or could not be).</returns>
    public async Task RequestQuitAsync()
    {
        if (!Spec.RedirectStdin || HasExited)
        {
            return;
        }

        try
        {
            var stdin = _process.StandardInput;
            await stdin.WriteAsync("q\n").ConfigureAwait(false);
            await stdin.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The process is already closing its stdin; the kill fallback handles the rest.
        }
    }

    /// <summary>Kills the process and its tree, then waits (up to 5 s) for it to be gone.</summary>
    /// <returns>A task that completes when the process exited.</returns>
    public async Task KillAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already exited.
        }

        await Task.WhenAny(Exited, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the process the way a cancel does (TECH_BRIEF 9): when stdin is open, write <c>q</c> and wait up to
    /// <paramref name="grace"/>; then kill whatever is still running.
    /// </summary>
    /// <param name="grace">How long to wait after <c>q</c>; null means <see cref="DefaultQuitGrace"/>.</param>
    /// <returns>True when the process stopped by itself after <c>q</c>, false when it had to be killed.</returns>
    public async Task<bool> StopAsync(TimeSpan? grace = null)
    {
        if (HasExited)
        {
            await Exited.ConfigureAwait(false);
            return true;
        }

        if (Spec.RedirectStdin)
        {
            await RequestQuitAsync().ConfigureAwait(false);
            var finished = await Task.WhenAny(Exited, Task.Delay(grace ?? DefaultQuitGrace)).ConfigureAwait(false);
            if (finished == Exited)
            {
                return true;
            }
        }

        await KillAsync().ConfigureAwait(false);
        return false;
    }

    /// <summary>Kills the process if it is still running and releases its handles.</summary>
    /// <returns>A task that completes when done.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (!HasExited)
        {
            await KillAsync().ConfigureAwait(false);
        }

        _registry.Unregister(Pid);
        try
        {
            if (Spec.RedirectStdin)
            {
                _process.StandardInput.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // The pipe is already broken.
        }

        _process.Dispose();
    }

    private async Task DrainStderrAsync()
    {
        try
        {
            var reader = _process.StandardError;
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > MaxLineLength)
                {
                    line = line[..MaxLineLength];
                }

                lock (_stderrGate)
                {
                    if (_stderr.Count == StderrCapacity)
                    {
                        _stderr.Dequeue();
                    }

                    _stderr.Enqueue(line);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The pipe closed with the process.
        }
    }

    private async Task<int> WaitForExitAsync()
    {
        await _process.WaitForExitAsync().ConfigureAwait(false);

        // Read the exit code before waiting for the drain, so a later Dispose cannot make ExitCode throw.
        var exitCode = _process.ExitCode;
        await _stderrDrain.ConfigureAwait(false);
        _registry.Unregister(Pid);
        return exitCode;
    }
}
