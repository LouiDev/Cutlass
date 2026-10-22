using System.Diagnostics;

namespace Cutlass.IntegrationTests.Infrastructure;

/// <summary>
/// Runs a tool for test setup (generating fixture media) and verification (ffprobe of results), following the same
/// rules as the app's process layer: <c>ArgumentList</c> only, no shell, no window, stdout and stderr drained,
/// and the whole tree killed on timeout. Product code never uses it; it goes through <c>ProcessRunner</c> (M2).
/// </summary>
public static class TestProcess
{
    /// <summary>Runs a process to completion.</summary>
    /// <param name="fileName">The executable.</param>
    /// <param name="args">The arguments, passed through <see cref="ProcessStartInfo.ArgumentList"/>.</param>
    /// <param name="workingDirectory">The working directory.</param>
    /// <param name="timeout">Kill the process tree after this long.</param>
    /// <returns>The exit code, stdout and stderr.</returns>
    /// <exception cref="TimeoutException">When the process exceeded <paramref name="timeout"/>.</exception>
    public static async Task<TestProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> args,
        string workingDirectory,
        TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdout = ReadAllBytesAsync(process.StandardOutput.BaseStream);
        var stderr = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{Path.GetFileName(fileName)} did not finish within {timeout}.");
        }

        return new TestProcessResult(
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
