using Cutlass.Agent.Hosting;
using Cutlass.Agent.Lock;
using Cutlass.Core.Model;

namespace Cutlass.Agent.Tools;

/// <summary>What a read tool takes from the UI thread in one hop, to work on off the UI thread.</summary>
/// <param name="Project">The current project.</param>
/// <param name="ProjectPath">The project file, or null.</param>
/// <param name="Dirty">Unsaved changes.</param>
/// <param name="Playhead">The user's playhead.</param>
/// <param name="Lock">The lock state.</param>
/// <param name="LockMine">True when the caller's lock id holds the lock.</param>
/// <param name="BatchLabel">The open group label, or null.</param>
/// <param name="User">What the user is doing.</param>
/// <param name="Revision">The revision.</param>
public sealed record HostSnapshot(
    Project Project,
    string? ProjectPath,
    bool Dirty,
    long Playhead,
    AgentLockState Lock,
    bool LockMine,
    string? BatchLabel,
    AgentUserState User,
    long Revision);
