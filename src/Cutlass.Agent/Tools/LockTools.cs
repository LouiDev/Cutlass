using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto;
using Cutlass.Agent.Lock;

namespace Cutlass.Agent.Tools;

/// <summary>The lock tools (AGENT_INTEGRATION 3.2 to 3.5): acquire, renew, release and get.</summary>
internal sealed class LockTools(ToolDispatcher d)
{
    public static LockStateDto ToDto(AgentLockState s, bool mine) => new()
    {
        Phase = EnumWire.Name(s.Phase),
        AgentName = s.AgentName,
        Reason = s.Reason,
        Since = s.SinceMs is { } since ? DateTimeOffset.FromUnixTimeMilliseconds(since) : null,
        LeaseExpiresAt = s.LeaseExpiresAtMs is { } exp ? DateTimeOffset.FromUnixTimeMilliseconds(exp) : null,
        LeaseSeconds = s.LeaseSeconds,
        Mine = mine,
        Pending = s.PendingAgent,
        LastTool = s.LastTool,
        Batch = s.BatchLabel,
        Holder = s.Holder,
    };

    public void Register()
    {
        d.Register("lock.acquire", AcquireAsync);
        d.Register("lock.renew", RenewAsync);
        d.Register("lock.release", ReleaseAsync);
        d.Register("lock.get", GetAsync);
    }

    private static LockGrantDto Grant(string? lockId, AgentLockState s) => new()
    {
        LockId = lockId,
        Phase = EnumWire.Name(s.Phase),
        LeaseSeconds = s.LeaseSeconds,
        LeaseExpiresAt = s.LeaseExpiresAtMs is { } exp ? DateTimeOffset.FromUnixTimeMilliseconds(exp) : null,
    };

    private async Task<ApiResult> AcquireAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.AcquireLockRequest);
        var name = (req.AgentName ?? ctx.Call.AgentName)?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw ApiException.InvalidArgument("agentName", "'agentName' is required: the user sees it in the banner.");
        }

        if (name.Length > 64)
        {
            name = name[..64];
        }

        var reason = (req.Reason ?? string.Empty).Trim();
        if (reason.Length > 200)
        {
            reason = reason[..200];
        }

        var host = ctx.Host;
        var request = new AcquireRequest(name, reason, Math.Max(0, req.WaitMs ?? 0), req.LeaseSeconds, ctx.Call.LockId);
        var outcome = await host.Ui.InvokeAsync(() => host.Lock.Acquire(request, host.UserState, d.NowMs), ctx.Ct).ConfigureAwait(false);
        if (outcome.Status == AcquireStatus.Waiting && outcome.Wait is { } wait)
        {
            var options = await host.Ui.InvokeAsync(() => host.Lock.Options, ctx.Ct).ConfigureAwait(false);
            // The lock's own tick resolves the wait at its deadline; this is only a backstop for a host whose driver stopped.
            var limit = TimeSpan.FromMilliseconds(Math.Max(Math.Min(request.WaitMs, options.MaxWaitMs), options.AskTimeoutMs) + 5000);
            try
            {
                await wait.WaitAsync(limit, ctx.Ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                await host.Ui.InvokeAsync(() => host.Lock.CancelWait(wait), CancellationToken.None).ConfigureAwait(false);
            }

            outcome = await wait.ConfigureAwait(false);
        }

        return outcome.Status switch
        {
            AcquireStatus.Granted => ctx.Ok(Grant(outcome.LockId, outcome.State), AgentJsonContext.Relaxed.LockGrantDto),
            AcquireStatus.Denied => throw new ApiException(
                ErrorCode.Forbidden,
                "The user did not allow you to take the project. Tell your user.",
                new Dictionary<string, string> { ["reason"] = "denied_by_user" }),
            _ => throw Busy(outcome, host.Lock.Options.RetryAfterMs),
        };
    }

    private static ApiException Busy(AcquireOutcome outcome, int retryAfterMs)
    {
        var reason = outcome.BusyReason ?? "held_by_agent";
        var message = reason switch
        {
            "gesture" => "The user is dragging on the timeline. Wait a moment (waitMs) and retry.",
            "modal" => "The user has a dialog open. Wait (waitMs) and retry, or tell your user.",
            "export" => "The user is exporting. Wait for the export to finish (waitMs) or tell your user.",
            _ => $"Another agent ({outcome.State.AgentName ?? outcome.State.PendingAgent}) holds or waits for the project. Wait (waitMs) or tell your user.",
        };
        return new ApiException(ErrorCode.Busy, message, new Dictionary<string, string> { ["reason"] = reason }, retryAfterMs);
    }

    private async Task<ApiResult> RenewAsync(ToolCallContext ctx)
    {
        var host = ctx.Host;
        var state = await d.Edits.WithLockAsync(ctx, waitWhilePaused: false, () =>
        {
            host.Lock.Renew(ctx.Call.LockId, d.NowMs);
            return host.Lock.State;
        }).ConfigureAwait(false);
        return ctx.Ok(Grant(null, state), AgentJsonContext.Relaxed.LockGrantDto);
    }

    private async Task<ApiResult> ReleaseAsync(ToolCallContext ctx)
    {
        var host = ctx.Host;
        await d.Edits.WithLockAsync(ctx, waitWhilePaused: false, () => host.Lock.Release(ctx.Call.LockId, host.UserState, d.NowMs)).ConfigureAwait(false);
        return ctx.Ok(EmptyDto.Instance, AgentJsonContext.Relaxed.EmptyDto);
    }

    private async Task<ApiResult> GetAsync(ToolCallContext ctx)
    {
        var snap = await ctx.SnapshotAsync().ConfigureAwait(false);
        return ApiResult.Ok(snap.Revision, ToDto(snap.Lock, snap.LockMine), AgentJsonContext.Relaxed.LockStateDto);
    }
}
