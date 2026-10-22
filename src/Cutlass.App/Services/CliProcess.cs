using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Cutlass.App.Services;

/// <summary>
/// The process helper for small command line tools (AGENT_INTEGRATION 9.3: an agent client's own CLI that adds,
/// checks and removes the MCP server). Like <c>ProcessRunner</c> it uses <c>ArgumentList</c> and <c>CreateNoWindow</c> and drains
/// stdout and stderr, but it is deliberately separate: ffmpeg and ffprobe never run through it. The tool is looked up
/// on PATH first (on Windows also as <c>.exe</c>, <c>.cmd</c> and <c>.bat</c>; a batch file runs through
/// <c>cmd.exe /c</c>), a missing tool gives <see cref="CliResult.NotFound"/>, stdin is closed at once (a prompt reads
/// end of file instead of hanging) and the process tree is killed on timeout or cancellation. Any thread; the work
/// runs on the thread pool and the caller awaits it, so the UI thread never blocks.
/// </summary>
public sealed class CliProcess : ICliRunner
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Finds a tool on PATH. On Windows a bare name without an extension also matches <c>name.exe</c>,
    /// <c>name.cmd</c> and <c>name.bat</c>, in that order. A name with a folder is checked as it is.
    /// </summary>
    /// <param name="command">The tool's name or a path.</param>
    /// <param name="pathVariable">The PATH value (null reads the environment).</param>
    /// <param name="windows">True for the Windows rules (null: the current platform).</param>
    /// <returns>The full path, or null when the tool is not found.</returns>
    public static string? Resolve(string command, string? pathVariable = null, bool? windows = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var isWindows = windows ?? OperatingSystem.IsWindows();
        string[] extensions = isWindows && !Path.HasExtension(command) ? [".exe", ".cmd", ".bat", string.Empty] : [string.Empty];
        if (command.Contains('/', StringComparison.Ordinal) || command.Contains('\\', StringComparison.Ordinal))
        {
            return extensions.Select(e => command + e).FirstOrDefault(File.Exists);
        }

        var path = pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = isWindows ? ';' : ':';
        foreach (var folder in path.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(folder.Trim('"'), command + extension);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry.
                    break;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public Task<CliResult> RunAsync(string command, IReadOnlyList<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);
        return Task.Run(() => RunCoreAsync(command, arguments, workingDirectory, timeout, cancellationToken), cancellationToken);
    }

    private static async Task<CliResult> RunCoreAsync(string command, IReadOnlyList<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (Resolve(command) is not { } file)
        {
            return CliResult.Missing(command + " was not found on PATH.");
        }

        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        var extension = Path.GetExtension(file);
        if (OperatingSystem.IsWindows() &&
            (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            // A batch file (an npm shim) only runs through the command interpreter.
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(file);
        }
        else
        {
            psi.FileName = file;
        }

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                return CliResult.Missing(command + " could not be started.");
            }
        }
        catch (Win32Exception ex)
        {
            return CliResult.Missing(command + " could not be started: " + ex.Message);
        }

        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            var partialOut = await CollectAsync(stdout).ConfigureAwait(false);
            var partialErr = await CollectAsync(stderr).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new CliResult(-1, partialOut, partialErr, TimedOut: true);
        }

        return new CliResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // It ended on its own in the meantime.
        }
    }

    private static async Task<string> CollectAsync(Task<string> read)
    {
        try
        {
            return await read.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException)
        {
            return string.Empty;
        }
    }
}
