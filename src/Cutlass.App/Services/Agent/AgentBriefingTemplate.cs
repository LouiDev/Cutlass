namespace Cutlass.App.Services.Agent;

/// <summary>A briefing template as loaded (AGENT_INTEGRATION 9.4): the neutral body and the target's tail.</summary>
/// <param name="Body">The body.</param>
/// <param name="Tail">The tail.</param>
/// <param name="BodySource">Where the body came from (a path, or "built-in").</param>
/// <param name="TailSource">Where the tail came from (a path, or "built-in").</param>
public sealed record AgentBriefingTemplate(string Body, string Tail, string BodySource, string TailSource)
{
    /// <summary>The body and the tail joined (<see cref="AgentBriefing.Compose"/>).</summary>
    public string Text => AgentBriefing.Compose(Body, Tail);
}
