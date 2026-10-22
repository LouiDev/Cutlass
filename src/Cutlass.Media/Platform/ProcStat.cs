using System.Globalization;

namespace Cutlass.Media.Platform;

/// <summary>
/// Reads a process's start time and name from Linux <c>/proc/&lt;pid&gt;/stat</c> (TECH_BRIEF 5.4). The start time is
/// field 22, in clock ticks since boot, so it is stable across app sessions; <c>Process.StartTime</c> instead adds an
/// estimated boot time that shifts with wall clock corrections, which could make the orphan sweep miss a match.
/// </summary>
public static class ProcStat
{
    /// <summary>The kernel's USER_HZ: clock ticks per second in <c>/proc</c> (100 on every mainstream Linux).</summary>
    public const long ClockTicksPerSecond = 100;

    /// <summary>
    /// Parses one <c>/proc/&lt;pid&gt;/stat</c> line. The name (field 2) is in parentheses and may itself contain
    /// spaces and parentheses, so the fields after it are counted from the last <c>)</c>.
    /// </summary>
    /// <param name="line">The file content.</param>
    /// <param name="startTicks">The start time since boot in <see cref="TimeSpan"/> ticks.</param>
    /// <param name="name">The process name (comm, at most 15 characters).</param>
    /// <returns>True when the line could be read.</returns>
    public static bool TryParse(string? line, out long startTicks, out string name)
    {
        startTicks = 0;
        name = string.Empty;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var open = line.IndexOf('(', StringComparison.Ordinal);
        var close = line.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return false;
        }

        // After ") " come field 3 (state) onwards; starttime is field 22, so index 19 of the rest.
        var rest = line[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (rest.Length < 20 || !long.TryParse(rest[19], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks < 0)
        {
            return false;
        }

        name = line[(open + 1)..close];
        startTicks = ticks * (TimeSpan.TicksPerSecond / ClockTicksPerSecond);
        return true;
    }

    /// <summary>Reads a live process's start time since boot and name, or null when it does not exist.</summary>
    /// <param name="pid">The process id.</param>
    /// <returns>The start time (ticks since boot) and name, or null.</returns>
    public static (long StartTicks, string Name)? Read(int pid)
    {
        try
        {
            var text = File.ReadAllText("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/stat");
            return TryParse(text, out var ticks, out var name) ? (ticks, name) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
