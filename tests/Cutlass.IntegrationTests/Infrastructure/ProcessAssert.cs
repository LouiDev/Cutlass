using System.Diagnostics;

namespace Cutlass.IntegrationTests.Infrastructure;

/// <summary>Assertions about operating system processes.</summary>
public static class ProcessAssert
{
    /// <summary>True when a process with that pid is running and was started at the given time.</summary>
    /// <param name="pid">The pid.</param>
    /// <param name="startTimeUtcTicks">The start time recorded when it was launched, to tell a reused pid apart.</param>
    /// <returns>True while the original process is alive.</returns>
    public static bool IsAlive(int pid, long startTimeUtcTicks)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return false;
            }

            return Math.Abs(process.StartTime.ToUniversalTime().Ticks - startTimeUtcTicks) < TimeSpan.FromMilliseconds(50).Ticks;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>The start time of a running process in UTC ticks.</summary>
    /// <param name="pid">The pid.</param>
    /// <returns>The start time.</returns>
    public static long StartTicks(int pid)
    {
        using var process = Process.GetProcessById(pid);
        return process.StartTime.ToUniversalTime().Ticks;
    }

    /// <summary>Waits until the process is gone, failing after <paramref name="timeout"/>.</summary>
    /// <param name="pid">The pid.</param>
    /// <param name="startTimeUtcTicks">Its recorded start time.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns>A task that completes when the process is gone.</returns>
    public static async Task GoneWithinAsync(int pid, long startTimeUtcTicks, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (IsAlive(pid, startTimeUtcTicks))
        {
            Assert.True(sw.Elapsed < timeout, $"Process {pid} was still running after {timeout}.");
            await Task.Delay(25);
        }
    }
}
