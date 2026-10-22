using Cutlass.Agent.Api;
using Cutlass.Agent.Hosting;
using Cutlass.Agent.Lock;
using Cutlass.Agent.Tools;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Media.Export;

namespace Cutlass.Tests.Agent.Kit;

/// <summary>
/// A host for unit tests (AGENT_INTEGRATION 7.1): a synchronous invoker, a session over a test project, the agent
/// lock, a manual clock and a settable user state. Host operations work in memory; import and export are unavailable.
/// </summary>
internal sealed class FakeAgentHost : IAgentHost, IDisposable
{
    private readonly SyncUiInvoker _ui = new();
    private AgentUserState _user = AgentUserState.Idle;

    public FakeAgentHost(Project project, LeaseOptions? lease = null, ManualTimeProvider? time = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "cutlass-agent-unit-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Root);
        Time = time ?? new ManualTimeProvider();
        FakeServices = new FakeAgentServices(Root);
        Session = new ProjectSession(project, () => Time.GetTimestamp() / TimeSpan.TicksPerMillisecond);
        Lock = new AgentLock(Session, lease);
        Policy = AgentPolicy.FromSettings(FakeServices.Settings, [Root], [Root]);
        _ui.InvokeAsync(() => { Driver = new AgentLockDriver(this, Time); }, CancellationToken.None).GetAwaiter().GetResult();
        Audit = new AuditLog(null);
        Dispatcher = new ToolDispatcher(this, Audit, Time, "9.9.9");
    }

    public event EventHandler? UserStateChanged;

    public string Root { get; }

    public ManualTimeProvider Time { get; }

    public FakeAgentServices FakeServices { get; }

    public AuditLog Audit { get; }

    public ToolDispatcher Dispatcher { get; }

    public AgentLockDriver Driver { get; private set; } = null!;

    public string Kind => "headless";

    public IAgentUiInvoker Ui => _ui;

    public IAgentServices Services => FakeServices;

    public AgentPolicy Policy { get; set; }

    public ProjectSession Session { get; }

    public AgentLock Lock { get; }

    public string? ProjectPath { get; set; }

    public long Playhead { get; set; }

    public AgentUserState UserState => _user;

    public List<EditResult> Edits { get; } = [];

    public List<long> Focus { get; } = [];

    public List<AgentLockState> LockChanges { get; } = [];

    public int Prepared { get; private set; }

    public void SetUser(AgentUserState state)
    {
        _user = state;
        _ui.Post(() => UserStateChanged?.Invoke(this, EventArgs.Empty));
    }

    public void Tick() => _ui.Post(Driver.Tick);

    public void PrepareForAgent() => Prepared++;

    public void OnAgentEdit(EditResult result) => Edits.Add(result);

    public void OnAgentFocus(long frame) => Focus.Add(frame);

    public void OnLockChanged(AgentLockState state) => LockChanges.Add(state);

    public Task<HostImport> ImportAsync(IReadOnlyList<string> paths, string lockId, CancellationToken ct) =>
        throw new ApiException(ErrorCode.Unavailable, "No import in unit tests.");

    public async Task<HostOutcome> NewProjectAsync(SequenceSettings? settings, string lockId, CancellationToken ct)
    {
        await _ui.InvokeAsync(
            () =>
            {
                using (Session.WriteAs(SessionWriter.Agent))
                {
                    Session.Replace(TimelineEdits.NewProject(Services.Ids, settings));
                }

                ProjectPath = null;
            },
            ct);
        return HostOutcome.Ok(null);
    }

    public Task<HostOutcome> OpenProjectAsync(string path, string lockId, CancellationToken ct) =>
        Task.FromResult(HostOutcome.Fail(new ApiError(ErrorCode.Unavailable, "No open in unit tests.")));

    public async Task<HostOutcome> SaveProjectAsync(string? path, string lockId, CancellationToken ct)
    {
        var target = path ?? ProjectPath!;
        var project = await _ui.InvokeAsync(() => Session.Current, ct);
        await Services.Projects.SaveAsync(project, target, ct);
        await _ui.InvokeAsync(
            () =>
            {
                Session.MarkSaved();
                ProjectPath = target;
            },
            ct);
        return HostOutcome.Ok(target);
    }

    public Task<ExportJob> StartExportAsync(ExportRequest request, string lockId, CancellationToken ct) =>
        throw new ApiException(ErrorCode.Unavailable, "No export in unit tests.");

    public Task<ApiResult> CallAsync(string tool, string json = "{}", string? lockId = null) =>
        Dispatcher.DispatchAsync(AgentCall.Of(tool, json, lockId), CancellationToken.None);

    public async Task<string> AcquireAsync(string name = "Claude Code", string reason = "Testing")
    {
        var result = await CallAsync("lock.acquire", $$"""{"agentName":"{{name}}","reason":"{{reason}}"}""");
        Assert.Null(result.Error);
        return result.Result!.Value.GetProperty("lockId").GetString()!;
    }

    public void Dispose()
    {
        Dispatcher.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}
