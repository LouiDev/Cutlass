namespace Cutlass.Media.Platform;

/// <summary>
/// One child process as recorded in an instance's <c>pids-&lt;ownerPid&gt;-&lt;ownerStartTicks&gt;.json</c>
/// (TECH_BRIEF 5.4).
/// </summary>
/// <param name="Pid">The process id.</param>
/// <param name="StartTimeUtcTicks">The process start time in UTC ticks, which tells a reused pid apart.</param>
/// <param name="Executable">The executable path the process was started from.</param>
public sealed record ChildProcessRecord(int Pid, long StartTimeUtcTicks, string Executable);
