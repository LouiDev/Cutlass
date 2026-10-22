namespace Cutlass.App.Services.Agent;

/// <summary>
/// The banner's and the panel's buttons (AGENT_INTEGRATION 3.4, 3.5 and 6.2) as the agent view model sees them.
/// <see cref="AppAgentHost"/> implements them over the agent lock; tests use a fake. UI thread only.
/// </summary>
public interface IAgentLockControls
{
    /// <summary>Take over: commits the open batch and revokes the lock at once.</summary>
    /// <returns>True when an agent held the lock.</returns>
    bool TakeOver();

    /// <summary>Pause: mutating agent calls wait and the lease stops running down.</summary>
    /// <returns>True when the lock was held and is now paused.</returns>
    bool Pause();

    /// <summary>Resume: restores the lease (at least 30 s) and runs the waiting calls.</summary>
    /// <returns>True when the lock was paused.</returns>
    bool Resume();

    /// <summary>Answers the "ask first" prompt.</summary>
    /// <param name="allow">True for Allow, false for Deny.</param>
    /// <returns>True when a prompt was waiting.</returns>
    bool Answer(bool allow);
}
