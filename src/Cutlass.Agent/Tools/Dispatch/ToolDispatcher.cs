using System.Diagnostics;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto;
using Cutlass.Agent.Hosting;
using Cutlass.Agent.Lock;
using Cutlass.Agent.Tools.Audit;
using Cutlass.Agent.Tools.Edit;
using Cutlass.Core.Session;

namespace Cutlass.Agent.Tools.Dispatch;

/// <summary>
/// Runs tool calls (AGENT_INTEGRATION 2.1 and 2.3): finds the tool in <see cref="ToolCatalog"/>, renews the caller's
/// lease, limits heavy calls (2 at once, 1 while the user plays, a third waits 10 s then gets <c>busy</c>), runs the
/// handler, maps exceptions to API errors and records the audit entry. The HTTP host and the MCP in-process backend
/// both call <see cref="DispatchAsync"/>, so every tool is written once. Construct it on any thread; it attaches to the
/// session and the lock on the UI thread.
/// </summary>
public sealed class ToolDispatcher : IDisposable
{
    /// <summary>How long a heavy call waits for a slot before it gets <c>busy</c>.</summary>
    public static readonly TimeSpan HeavyWait = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, Func<ToolCallContext, Task<ApiResult>>> _handlers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _heavy = new(2, 2);
    private readonly SemaphoreSlim _heavyWhilePlaying = new(1, 1);
    private AgentRevision? _revision;
    private LockPhase _lastPhase = LockPhase.Free;
    private bool _disposed;

    /// <summary>Creates the dispatcher and registers every tool.</summary>
    /// <param name="host">The host.</param>
    /// <param name="audit">The audit log.</param>
    /// <param name="time">The clock, or null for the system clock.</param>
    /// <param name="appVersion">The version reported by status, or null for the assembly's.</param>
    public ToolDispatcher(IAgentHost host, AuditLog audit, TimeProvider? time = null, string? appVersion = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(audit);
        Host = host;
        Audit = audit;
        Time = time ?? TimeProvider.System;
        AppVersion = appVersion ?? typeof(ToolDispatcher).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        Events = new AgentEventQueue(Time);
        Edits = new EditRunner(this);
        Paths = new PathPolicy(host);
        if (host.Ui.CheckAccess())
        {
            Attach();
        }
        else
        {
            host.Ui.InvokeAsync(Attach, CancellationToken.None).GetAwaiter().GetResult();
        }

        new StatusTools(this).Register();
        new LockTools(this).Register();
        new ProjectTools(this).Register();
        new MediaTools(this).Register();
        new PreviewTools(this).Register();
        new AudioTools(this).Register();
        new ExportTools(this).Register();
        new EventTools(this).Register();
        new BatchTools(this).Register();
        new EditTools(this).Register();
        new DiscoveryTools(this).Register();
    }

    /// <summary>The host.</summary>
    public IAgentHost Host { get; }

    /// <summary>The audit log.</summary>
    public AuditLog Audit { get; }

    /// <summary>The clock.</summary>
    public TimeProvider Time { get; }

    /// <summary>The version reported by status.</summary>
    public string AppVersion { get; }

    /// <summary>The revision counter.</summary>
    public AgentRevision Revision => _revision ?? throw new InvalidOperationException("The dispatcher is not attached.");

    /// <summary>The event queue.</summary>
    public AgentEventQueue Events { get; }

    /// <summary>The file access rules.</summary>
    public PathPolicy Paths { get; }

    /// <summary>
    /// Stops a <c>serve</c> process (<c>/v1/shutdown</c>); the argument says whether to save first. Null (the App)
    /// makes shutdown <c>not_found</c>.
    /// </summary>
    public Func<bool, Task>? ShutdownHandler { get; set; }

    /// <summary>The current time in Unix milliseconds.</summary>
    public long NowMs => Time.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>The catalog names with a handler.</summary>
    public IReadOnlyCollection<string> ToolNames => _handlers.Keys;

    internal EditRunner Edits { get; }

    /// <summary>True when the media file is present (always true while no catalog exists).</summary>
    /// <param name="mediaId">The media id.</param>
    /// <returns>True when online.</returns>
    public bool IsOnline(string mediaId) => Host.Services.Catalog is not { } catalog || catalog.IsOnline(mediaId);

    /// <summary>Runs one call. Never throws for tool failures; they become error results.</summary>
    /// <param name="call">The call.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The result.</returns>
    public async Task<ApiResult> DispatchAsync(AgentCall call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);
        var watch = Stopwatch.StartNew();
        ApiResult result;
        string agent = call.AgentName ?? "?";
        try
        {
            if (ToolCatalog.Find(call.Tool) is not { } info || !_handlers.TryGetValue(call.Tool, out var handler))
            {
                throw new ApiException(ErrorCode.NotFound, $"There is no tool '{call.Tool}'. See the tool catalog for the names.");
            }

            var holder = await Host.Ui.InvokeAsync(() => TouchLock(call), ct).ConfigureAwait(false);
            if (holder is not null)
            {
                agent = holder;
            }

            if (holder is null && call.AgentName is null && call.Args.ValueKind == System.Text.Json.JsonValueKind.Object
                && call.Args.TryGetProperty("agentName", out var named) && named.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                agent = named.GetString() ?? agent;
            }

            var context = new ToolCallContext(this, call, info, ct);
            result = info.Heavy ? await RunHeavyAsync(context, handler).ConfigureAwait(false) : await handler(context).ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            result = ApiResult.Fail(_revision?.Current ?? 0, ex.Error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            result = ApiResult.Fail(_revision?.Current ?? 0, new ApiError(ErrorCode.Busy, "The call was cancelled.", null, 1000));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Host.Services.Log?.WriteException("agent: " + call.Tool, ex);
            result = ApiResult.Fail(_revision?.Current ?? 0, new ApiError(ErrorCode.Internal, "Cutlass hit an internal error running this tool: " + ex.Message));
        }

        Audit.Record(new AuditEntry(
            DateTime.UtcNow,
            agent,
            call.Tool,
            watch.ElapsedMilliseconds,
            result.Error?.CodeName ?? "ok",
            result.Revision,
            AuditLog.Summarize(call.Args.ValueKind == System.Text.Json.JsonValueKind.Undefined ? null : call.Args.GetRawText())));
        return result;
    }

    /// <summary>Stops listening to the session and the lock.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        void Detach()
        {
            Host.Lock.Changed -= OnLockChanged;
            if (_revision is { } revision)
            {
                revision.Raised -= OnRevision;
                revision.Dispose();
            }
        }

        if (Host.Ui.CheckAccess())
        {
            Detach();
        }
        else
        {
            Host.Ui.Post(Detach);
        }
    }

    /// <summary>Takes a snapshot of the host state on the UI thread.</summary>
    /// <param name="lockId">The caller's lock id, or null.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The snapshot.</returns>
    public Task<HostSnapshot> SnapshotAsync(string? lockId, CancellationToken ct) =>
        Host.Ui.InvokeAsync(
            () => new HostSnapshot(
                Host.Session.Current,
                Host.ProjectPath,
                Host.Session.IsDirty,
                Host.Playhead,
                Host.Lock.State,
                Host.Lock.IsHolder(lockId),
                Host.Session.InGroup ? Host.Session.GroupLabel : null,
                Host.UserState,
                Revision.Current),
            ct);

    internal void Register(string name, Func<ToolCallContext, Task<ApiResult>> handler)
    {
        if (ToolCatalog.Find(name) is null)
        {
            throw new InvalidOperationException($"The tool '{name}' is not in the catalog.");
        }

        _handlers[name] = handler;
    }

    private string? TouchLock(AgentCall call)
    {
        var lck = Host.Lock;
        if (call.LockId is { } id && lck.Touch(id, call.Tool, NowMs))
        {
            return lck.State.AgentName;
        }

        return null;
    }

    private async Task<ApiResult> RunHeavyAsync(ToolCallContext context, Func<ToolCallContext, Task<ApiResult>> handler)
    {
        var playing = await Host.Ui.InvokeAsync(() => Host.UserState.IsPlaying, context.Ct).ConfigureAwait(false);
        var busy = new ApiException(
            ErrorCode.Busy,
            "Too many renders or analyses are running. Wait a moment and retry.",
            new Dictionary<string, string> { ["reason"] = "heavy_calls" },
            2000);
        if (!await _heavy.WaitAsync(HeavyWait, context.Ct).ConfigureAwait(false))
        {
            throw busy;
        }

        var second = false;
        try
        {
            if (playing)
            {
                // While the user plays back only one heavy call runs, so preview keeps priority (3.8).
                if (!await _heavyWhilePlaying.WaitAsync(HeavyWait, context.Ct).ConfigureAwait(false))
                {
                    throw busy;
                }

                second = true;
            }

            return await handler(context).ConfigureAwait(false);
        }
        finally
        {
            if (second)
            {
                _heavyWhilePlaying.Release();
            }

            _heavy.Release();
        }
    }

    private void Attach()
    {
        _revision = new AgentRevision(Host.Session);
        _revision.Raised += OnRevision;
        Host.Lock.Changed += OnLockChanged;
        _lastPhase = Host.Lock.State.Phase;
    }

    private void OnRevision(object? sender, ProjectChangedEventArgs e) =>
        Events.Add(new EventDto
        {
            Kind = "projectChanged",
            Revision = Revision.Current,
            ChangeKind = EnumWire.Name(e.Kind),
            Label = e.Kind is ChangeKind.Undo ? Host.Session.RedoLabel : Host.Session.UndoLabel,
            By = Host.Session.Writer == SessionWriter.Agent ? "agent" : "user",
        });

    private void OnLockChanged(object? sender, AgentLockState state)
    {
        if (state.Phase == _lastPhase)
        {
            return;
        }

        var previous = _lastPhase;
        _lastPhase = state.Phase;
        Events.Add(new EventDto { Kind = "lockChanged", Phase = EnumWire.Name(state.Phase), AgentName = state.AgentName ?? state.EndedAgent });
        var what = state.Phase switch
        {
            LockPhase.Held when previous == LockPhase.Paused => "resumed",
            LockPhase.Held => "granted: " + state.Reason,
            LockPhase.Paused => "paused",
            LockPhase.Pending => "pending: " + state.PendingAgent,
            _ => state.EndedReason is { } r ? "ended: " + LockCheck.WireReason(r) : "free",
        };
        Audit.RecordLock(state.AgentName ?? state.EndedAgent ?? state.PendingAgent ?? "?", what, _revision?.Current ?? 0);
    }
}
