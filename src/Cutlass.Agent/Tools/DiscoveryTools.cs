using System.Collections.Concurrent;
using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Export;
using Cutlass.Agent.Tools.Dispatch;

namespace Cutlass.Agent.Tools;

/// <summary>
/// The discovery routes (AGENT_INTEGRATION 9.2): <c>/v1/tools</c> in every format, <c>/v1/openapi.json</c> and
/// <c>/v1/instructions</c>. They are reads without a lock and go through the dispatcher like every tool, so the auth
/// guard, the rate limit and the audit log apply. The answers are documents (<see cref="ToolResultKind.Document"/>),
/// served as is; the catalog never changes at run time, so each one is rendered once.
/// </summary>
internal sealed class DiscoveryTools(ToolDispatcher d)
{
    private readonly ConcurrentDictionary<string, (string Text, string MediaType)> _cache = new(StringComparer.Ordinal);

    public void Register()
    {
        d.Register("discovery.tools", ToolsAsync);
        d.Register("discovery.openapi", OpenApiAsync);
        d.Register("discovery.instructions", InstructionsAsync);
    }

    private static Task<ApiResult> Document(ToolCallContext ctx, string text, string mediaType) =>
        Task.FromResult(new ApiResult { Revision = ctx.Revision, Text = text, TextMediaType = mediaType });

    private Task<ApiResult> ToolsAsync(ToolCallContext ctx)
    {
        var format = ctx.Call.Args.ValueKind == JsonValueKind.Object && ctx.Call.Args.TryGetProperty("format", out var f)
            ? f.ValueKind == JsonValueKind.String ? f.GetString() : throw ApiException.InvalidArgument("format", "'format' must be a string.")
            : null;
        var key = "tools:" + (format?.Trim().ToLowerInvariant() ?? "json");
        if (!_cache.TryGetValue(key, out var doc))
        {
            if (!ToolExport.TryRender(format, out var text, out var mediaType))
            {
                throw ApiException.InvalidArgument("format", "'format' is one of " + string.Join(", ", ToolExport.Formats) + ".");
            }

            doc = _cache.GetOrAdd(key, (text, mediaType));
        }

        return Document(ctx, doc.Text, doc.MediaType);
    }

    private Task<ApiResult> OpenApiAsync(ToolCallContext ctx)
    {
        var doc = _cache.GetOrAdd("openapi", _ => (OpenApiDocument.Build(d.AppVersion), "application/json"));
        return Document(ctx, doc.Text, doc.MediaType);
    }

    private Task<ApiResult> InstructionsAsync(ToolCallContext ctx)
    {
        var doc = _cache.GetOrAdd("instructions", _ => (InstructionsJson(), "application/json"));
        return Document(ctx, doc.Text, doc.MediaType);
    }

    private static string InstructionsJson() =>
        JsonSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["text"] = AgentInstructions.Http() },
            AgentJsonContext.Relaxed.DictionaryStringString);
}
