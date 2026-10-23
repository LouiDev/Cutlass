using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto;
using Cutlass.Agent.Api.Requests;
using Cutlass.Agent.Lock;
using Cutlass.Agent.Tools.Dispatch;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;

namespace Cutlass.Agent.Tools.Edit;

/// <summary>
/// Runs every mutating call (AGENT_INTEGRATION 2.3, 3.4 and 3.6): one at a time in arrival order; on the UI thread
/// in one synchronous delegate it checks the lock, checks <c>ifRevision</c>, runs the edit as
/// <see cref="SessionWriter.Agent"/> with an "Agent: " label, resets the agent redo counter, follows the edit and
/// builds the mutation envelope. While the lock is paused, the call waits (without blocking a thread) for up to 25 s.
/// </summary>
public sealed class EditRunner
{
    /// <summary>The undo label prefix of agent steps.</summary>
    public const string LabelPrefix = "Agent: ";

    private readonly ToolDispatcher _dispatcher;
    private readonly SemaphoreSlim _mutations = new(1, 1);

    internal EditRunner(ToolDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>Runs a pure timeline edit through <see cref="ProjectSession.Execute"/>.</summary>
    /// <param name="ctx">The call.</param>
    /// <param name="request">The request (for <c>ifRevision</c> and <c>returnProject</c>).</param>
    /// <param name="edit">The edit; it may throw <see cref="ApiException"/> for unknown ids.</param>
    /// <returns>The mutation envelope.</returns>
    public Task<ApiResult> EditAsync(ToolCallContext ctx, MutationRequest? request, Func<Project, EditResult> edit) =>
        MutateAsync(ctx, request, session =>
        {
            var result = session.Execute(p =>
            {
                var r = edit(p);
                return r with { Label = LabelPrefix + r.Label };
            });
            return new MutationOutcome(result with { Label = Strip(result.Label) }, ResetsRedo: true);
        });

    /// <summary>
    /// Runs any session mutation with the lock and revision checks. The action runs on the UI thread inside
    /// <see cref="ProjectSession.WriteAs"/>(Agent) and may throw <see cref="ApiException"/>.
    /// </summary>
    /// <param name="ctx">The call.</param>
    /// <param name="request">The request, or null.</param>
    /// <param name="action">The action.</param>
    /// <returns>The mutation envelope.</returns>
    public async Task<ApiResult> MutateAsync(ToolCallContext ctx, MutationRequest? request, Func<ProjectSession, MutationOutcome> action)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(action);
        return await WithLockAsync(ctx, waitWhilePaused: true, () =>
        {
            var host = ctx.Host;
            var session = host.Session;
            if (request?.IfRevision is { } expected && expected != _dispatcher.Revision.Current)
            {
                throw new ApiException(
                    ErrorCode.Conflict,
                    $"The project changed (revision {_dispatcher.Revision.Current}, you expected {expected}). Read the project again before editing.",
                    new Dictionary<string, string> { ["reason"] = "revision_mismatch", ["revision"] = _dispatcher.Revision.Current.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            }

            var before = session.Current;
            MutationOutcome outcome;
            using (session.WriteAs(SessionWriter.Agent))
            {
                outcome = action(session);
            }

            var changed = !ReferenceEquals(before, session.Current);
            if (changed && outcome.ResetsRedo)
            {
                host.Lock.AgentRedoCount = 0;
            }

            if (changed)
            {
                host.OnAgentEdit(outcome.Result);
            }

            host.Lock.NoteBatch(session.InGroup ? Strip(session.GroupLabel) : null);
            JsonElement? project = null;
            if (request?.ReturnProject == true)
            {
                var summary = ProjectSummaryMapper.Map(
                    session.Current,
                    host.ProjectPath,
                    session.IsDirty,
                    _dispatcher.Revision.Current,
                    host.Playhead,
                    _dispatcher.IsOnline,
                    null);
                project = JsonSerializer.SerializeToElement(summary, AgentJsonContext.Relaxed.ProjectSummaryDto);
            }

            var dto = new EditOutcomeDto { Label = outcome.Result.Label, Batch = session.InGroup ? Strip(session.GroupLabel) : null };
            return new ApiResult
            {
                Revision = _dispatcher.Revision.Current,
                Warnings = [.. outcome.Result.Warnings],
                Result = JsonSerializer.SerializeToElement(dto, AgentJsonContext.Relaxed.EditOutcomeDto),
                Changed = changed,
                Select = outcome.Result.Select.IsDefault ? [] : [.. outcome.Result.Select],
                UndoLabel = session.UndoLabel,
                Project = project,
            };
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a delegate on the UI thread after a successful lock check, serialised with the other mutating calls. With
    /// <paramref name="waitWhilePaused"/>, a paused lock makes the call wait up to 25 s for Resume.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="ctx">The call.</param>
    /// <param name="waitWhilePaused">True for mutating calls; false for renew, release and the playhead.</param>
    /// <param name="onUi">The delegate.</param>
    /// <returns>The delegate's result.</returns>
    public async Task<T> WithLockAsync<T>(ToolCallContext ctx, bool waitWhilePaused, Func<T> onUi)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(onUi);
        var host = ctx.Host;
        // Renew, release and the playhead run beside a paused mutation, so only waiting calls queue.
        if (waitWhilePaused)
        {
            await _mutations.WaitAsync(ctx.Ct).ConfigureAwait(false);
        }

        try
        {
            var options = await host.Ui.InvokeAsync(() => host.Lock.Options, ctx.Ct).ConfigureAwait(false);
            var deadline = DateTime.UtcNow.AddMilliseconds(options.PausedWaitMs);
            while (true)
            {
                var step = await host.Ui.InvokeAsync(
                    () =>
                    {
                        var check = host.Lock.Check(ctx.Call.LockId, _dispatcher.NowMs);
                        if (check.Status == LockCheckStatus.Paused && waitWhilePaused)
                        {
                            return (Done: false, Value: default(T), Resume: host.Lock.WhenResumed);
                        }

                        if (check.Status is LockCheckStatus.Required or LockCheckStatus.Lost)
                        {
                            throw LockErrors.From(check, host.Lock.State);
                        }

                        return (Done: true, Value: onUi(), Resume: (Task<bool>?)null);
                    },
                    ctx.Ct).ConfigureAwait(false);
                if (step.Done)
                {
                    return step.Value!;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero
                    || await Task.WhenAny(step.Resume!, Task.Delay(remaining, ctx.Ct)).ConfigureAwait(false) != step.Resume)
                {
                    ctx.Ct.ThrowIfCancellationRequested();
                    throw LockErrors.Paused(options.RetryAfterMs);
                }
            }
        }
        finally
        {
            if (waitWhilePaused)
            {
                _mutations.Release();
            }
        }
    }

    /// <summary>
    /// Checks the lock (waiting while paused) and then runs asynchronous work off the UI thread, serialised with the
    /// other mutating calls. Host operations use it; they recheck the lock at their own commit point.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="ctx">The call.</param>
    /// <param name="work">The work.</param>
    /// <returns>The work's result.</returns>
    public async Task<T> HostOperationAsync<T>(ToolCallContext ctx, Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        await WithLockAsync(ctx, waitWhilePaused: true, () => true).ConfigureAwait(false);
        await _mutations.WaitAsync(ctx.Ct).ConfigureAwait(false);
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            _mutations.Release();
        }
    }

    /// <summary>Removes the "Agent: " prefix.</summary>
    /// <param name="label">The label, or null.</param>
    /// <returns>The label without the prefix.</returns>
    public static string Strip(string? label) =>
        label is null ? string.Empty : label.StartsWith(LabelPrefix, StringComparison.Ordinal) ? label[LabelPrefix.Length..] : label;
}
