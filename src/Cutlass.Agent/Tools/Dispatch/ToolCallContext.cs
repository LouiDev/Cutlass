using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Cutlass.Agent.Api;
using Cutlass.Agent.Hosting;

namespace Cutlass.Agent.Tools.Dispatch;

/// <summary>The per-call context a tool handler gets from <see cref="ToolDispatcher"/>.</summary>
public sealed class ToolCallContext
{
    internal ToolCallContext(ToolDispatcher dispatcher, AgentCall call, ToolInfo info, CancellationToken ct)
    {
        Dispatcher = dispatcher;
        Call = call;
        Info = info;
        Ct = ct;
    }

    /// <summary>The dispatcher (host, revision, events, audit).</summary>
    public ToolDispatcher Dispatcher { get; }

    /// <summary>The call.</summary>
    public AgentCall Call { get; }

    /// <summary>The tool's catalog entry.</summary>
    public ToolInfo Info { get; }

    /// <summary>Cancels the call (the client went away).</summary>
    public CancellationToken Ct { get; }

    /// <summary>The host.</summary>
    public IAgentHost Host => Dispatcher.Host;

    /// <summary>The current time in Unix milliseconds.</summary>
    public long NowMs => Dispatcher.NowMs;

    /// <summary>The current revision.</summary>
    public long Revision => Dispatcher.Revision.Current;

    /// <summary>Deserialises the arguments.</summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <param name="info">Its type info from <see cref="AgentJsonContext"/>.</param>
    /// <returns>The request.</returns>
    /// <exception cref="ApiException"><c>invalid_argument</c> when the JSON does not fit.</exception>
    public T Read<T>(JsonTypeInfo<T> info)
        where T : new()
    {
        if (Call.Args.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new T();
        }

        if (Call.Args.ValueKind != JsonValueKind.Object)
        {
            throw ApiException.InvalidArgument("args", "The arguments must be a JSON object.");
        }

        try
        {
            return Call.Args.Deserialize(info) ?? new T();
        }
        catch (JsonException ex)
        {
            var field = (ex.Path ?? "$").TrimStart('$', '.');
            throw ApiException.InvalidArgument(
                field.Length == 0 ? "args" : field,
                $"The argument '{(field.Length == 0 ? "args" : field)}' has the wrong type or format. Check the tool's schema.");
        }
    }

    /// <summary>A JSON success with the current revision.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="value">The result.</param>
    /// <param name="info">Its type info.</param>
    /// <param name="warnings">Warnings, or null.</param>
    /// <returns>The result.</returns>
    public ApiResult Ok<T>(T value, JsonTypeInfo<T> info, IEnumerable<string>? warnings = null) =>
        ApiResult.Ok(Revision, value, info, warnings);

    /// <summary>Takes a snapshot of the host state on the UI thread.</summary>
    /// <returns>The snapshot.</returns>
    public Task<HostSnapshot> SnapshotAsync() => Dispatcher.SnapshotAsync(Call.LockId, Ct);
}
