using Cutlass.Agent.Api;
using Cutlass.Core.Editing;

namespace Cutlass.Agent.Tools;

/// <summary>Batches and agent undo and redo (AGENT_INTEGRATION 3.6).</summary>
internal sealed class BatchTools(ToolDispatcher d)
{
    /// <summary>The longest batch label.</summary>
    public const int MaxLabelLength = 80;

    public void Register()
    {
        d.Register("batch.begin", BeginAsync);
        d.Register("batch.end", EndAsync);
        d.Register("batch.cancel", CancelAsync);
        d.Register("undo", UndoAsync);
        d.Register("redo", RedoAsync);
    }

    private static ApiException BatchOpen() =>
        ApiException.Conflict("batch_open", "A batch is open. End or cancel it first.");

    private Task<ApiResult> BeginAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.BatchRequest);
        var label = Args.Required(req.Label, "label").Trim();
        if (label.Length > MaxLabelLength)
        {
            label = label[..MaxLabelLength];
        }

        return d.Edits.MutateAsync(ctx, req, session =>
        {
            if (session.InGroup)
            {
                throw ApiException.Conflict("nested_batch", $"The batch \"{EditRunner.Strip(session.GroupLabel)}\" is still open; batches do not nest. End it first.");
            }

            session.BeginGroup(EditRunner.LabelPrefix + label);
            return new MutationOutcome(EditResult.Unchanged(session.Current, "Begin Batch"), ResetsRedo: false);
        });
    }

    private Task<ApiResult> EndAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.BatchRequest);
        return d.Edits.MutateAsync(ctx, req, session =>
        {
            if (!session.InGroup)
            {
                throw ApiException.Conflict("no_batch", "No batch is open.");
            }

            var label = EditRunner.Strip(session.GroupLabel);
            var recorded = session.EndGroup();
            return new MutationOutcome(
                recorded ? EditResult.Unchanged(session.Current, "End Batch") : EditResult.Unchanged(session.Current, "End Batch", $"The batch \"{label}\" changed nothing, so no undo step was recorded."),
                ResetsRedo: false);
        });
    }

    private Task<ApiResult> CancelAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.BatchRequest);
        return d.Edits.MutateAsync(ctx, req, session =>
        {
            if (!session.InGroup)
            {
                throw ApiException.Conflict("no_batch", "No batch is open.");
            }

            session.CancelGroup();
            return new MutationOutcome(EditResult.Unchanged(session.Current, "Cancel Batch"), ResetsRedo: false);
        });
    }

    private Task<ApiResult> UndoAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.BatchRequest);
        var host = ctx.Host;
        return d.Edits.MutateAsync(ctx, req, session =>
        {
            if (session.InGroup)
            {
                throw BatchOpen();
            }

            if (session.UndoCount <= host.Lock.UndoBaseline)
            {
                throw ApiException.Conflict("not_agent_step", "There is no step of yours to undo; the user's steps are never undone by agents.");
            }

            var label = session.UndoLabel;
            session.Undo();
            host.Lock.AgentRedoCount++;
            return new MutationOutcome(EditResult.Unchanged(session.Current, "Undo " + EditRunner.Strip(label)), ResetsRedo: false);
        });
    }

    private Task<ApiResult> RedoAsync(ToolCallContext ctx)
    {
        var req = ctx.Read(AgentJsonContext.Relaxed.BatchRequest);
        var host = ctx.Host;
        return d.Edits.MutateAsync(ctx, req, session =>
        {
            if (session.InGroup)
            {
                throw BatchOpen();
            }

            if (host.Lock.AgentRedoCount <= 0 || !session.CanRedo)
            {
                throw ApiException.Conflict("not_agent_step", "There is no step of yours to redo.");
            }

            var label = session.RedoLabel;
            session.Redo();
            host.Lock.AgentRedoCount--;
            return new MutationOutcome(EditResult.Unchanged(session.Current, "Redo " + EditRunner.Strip(label)), ResetsRedo: false);
        });
    }
}
