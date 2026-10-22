namespace Cutlass.Agent.Hosting;

/// <summary>What the user is doing (AGENT_INTEGRATION 2.2 and 3.9). A busy user blocks lock acquisition.</summary>
/// <param name="InGesture">A drag is running.</param>
/// <param name="ModalOpen">A modal dialog is open.</param>
/// <param name="ExportRunning">A user-started export is queued or running.</param>
/// <param name="IsPlaying">A monitor plays back (not busy; it lowers the heavy-call limit).</param>
public sealed record AgentUserState(bool InGesture, bool ModalOpen, bool ExportRunning, bool IsPlaying)
{
    /// <summary>An idle user.</summary>
    public static AgentUserState Idle { get; } = new(false, false, false, false);

    /// <summary><c>gesture</c>, <c>modal</c>, <c>export</c>, or null when the user is not busy.</summary>
    public string? BusyReason => InGesture ? "gesture" : ModalOpen ? "modal" : ExportRunning ? "export" : null;
}
