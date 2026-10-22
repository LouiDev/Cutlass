namespace Cutlass.Media.Platform;

/// <summary>
/// One app instance's child processes as persisted in <c>pids-&lt;ownerPid&gt;-&lt;ownerStartTicks&gt;.json</c>
/// (TECH_BRIEF 5.4). The owner identity tells a live instance's file from a crashed one's, and the boot id tells files
/// from before a reboot, whose pids mean nothing any more.
/// </summary>
/// <param name="OwnerPid">The app instance's process id.</param>
/// <param name="OwnerStartTicks">The app instance's start time, in the same unit as
/// <see cref="ChildProcessRecord.StartTimeUtcTicks"/> (ticks since boot on Linux).</param>
/// <param name="BootId">The Linux <c>/proc/sys/kernel/random/boot_id</c> at writing, or null elsewhere.</param>
/// <param name="Children">The registered children.</param>
public sealed record ChildProcessFile(int OwnerPid, long OwnerStartTicks, string? BootId, List<ChildProcessRecord> Children);
