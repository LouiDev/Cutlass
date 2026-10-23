using Cutlass.Agent.Api;
using Cutlass.Agent.Tools.Dispatch;

namespace Cutlass.Agent.Tools;

/// <summary>The long-poll <c>events</c> tool (AGENT_INTEGRATION 4.7).</summary>
internal sealed class EventTools(ToolDispatcher d)
{
    public void Register() => d.Register("events", EventsAsync);

    private async Task<ApiResult> EventsAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.EventsRequest);
        var after = Math.Max(0, req.After ?? 0);
        var timeout = (int)Args.InRange(req.TimeoutMs ?? 0, 0, AgentEventQueue.MaxTimeoutMs, "timeoutMs");
        var events = await d.Events.WaitAsync(after, timeout, ctx.Ct).ConfigureAwait(false);
        return ctx.Ok(events, AgentJsonContext.Relaxed.EventsDto);
    }
}
