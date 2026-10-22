namespace Cutlass.Media.Platform;

/// <summary>What a startup sweep does with another instance's pid file (TECH_BRIEF 5.4).</summary>
public enum OrphanSweepAction
{
    /// <summary>The owner still runs (or it is this instance): leave the file and its children alone.</summary>
    Keep,

    /// <summary>The file is from before a reboot (or unreadable): its pids are meaningless; delete it, kill nothing.</summary>
    DeleteOnly,

    /// <summary>The owner is gone: kill its surviving children, then delete the file.</summary>
    KillAndDelete,
}
