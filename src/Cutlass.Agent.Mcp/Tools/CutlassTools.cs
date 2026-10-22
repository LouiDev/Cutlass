using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cutlass.Agent.Api;
using Cutlass.Agent.Mcp.Backend;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Cutlass.Agent.Mcp.Tools;

/// <summary>
/// The MCP tools of Cutlass (docs/AGENT_INTEGRATION.md 5.2). One <see cref="McpServerTool"/> per
/// <see cref="ToolCatalog.McpTools"/> entry, so the MCP names, descriptions and argument schemas are exactly the
/// catalog's and every HTTP tool the catalog exposes to MCP is listed.
/// </summary>
/// <remarks>
/// Deviation from 5.2 ("each tool is a method with <c>[McpServerTool]</c>"), recorded in AGENT_INTEGRATION 5.2: the
/// catalog already holds the name, description and JSON schema of every tool for HTTP, so the MCP tools are built
/// from it instead of from about 60 attributed methods whose parameter lists would repeat the schemas and drift. The
/// few tools whose MCP shape differs from the HTTP route (seconds instead of milliseconds, save with an optional path,
/// images) are adapted in the group partials (<c>CutlassTools.Lock.cs</c>, <c>.Project.cs</c>, <c>.Preview.cs</c>,
/// <c>.Export.cs</c>).
/// </remarks>
public sealed partial class CutlassTools
{
    private readonly IAgentBackend _backend;
    private readonly FrozenDictionary<string, ToolAdapter> _adapters;

    /// <summary>
    /// Serialises tool dispatch (AGENT_INTEGRATION 5.2): the SDK may run concurrent requests out of order, so a client
    /// that pipelines calls (place, then split) would otherwise see them reach the backend in another order.
    /// </summary>
    private readonly SemaphoreSlim _dispatch = new(1, 1);

    /// <summary>Creates the tools over a backend.</summary>
    /// <param name="backend">Where calls go.</param>
    public CutlassTools(IAgentBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        var adapters = new Dictionary<string, ToolAdapter>(StringComparer.Ordinal);
        AddLockAdapters(adapters);
        AddProjectAdapters(adapters);
        AddPreviewAdapters(adapters);
        AddExportAdapters(adapters);
        _adapters = adapters.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Builds one MCP tool per catalog entry that MCP exposes.</summary>
    /// <returns>The tools, in catalog order.</returns>
    public IReadOnlyList<McpServerTool> CreateTools() =>
        [.. ToolCatalog.McpTools.Select(info => (McpServerTool)new CatalogMcpTool(this, info, BuildProtocolTool(info)))];

    /// <summary>Runs one MCP tool call: adapts the arguments, calls the backend and shapes the result.</summary>
    /// <param name="info">The catalog entry.</param>
    /// <param name="arguments">The MCP arguments, or null.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The MCP result; API errors become <c>isError</c> results reading <c>code: message</c>.</returns>
    public async Task<CallToolResult> InvokeAsync(ToolInfo info, IDictionary<string, JsonElement>? arguments, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(info);
        await _dispatch.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await InvokeSerialAsync(info, arguments, ct).ConfigureAwait(false);
        }
        finally
        {
            _dispatch.Release();
        }
    }

    private async Task<CallToolResult> InvokeSerialAsync(ToolInfo info, IDictionary<string, JsonElement>? arguments, CancellationToken ct)
    {
        var args = new JsonObject();
        if (arguments is not null)
        {
            foreach (var (key, value) in arguments)
            {
                // Clients send null for omitted optional fields; the API treats a missing field as its default.
                if (value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                {
                    args[key] = JsonNode.Parse(value.GetRawText());
                }
            }
        }

        var adapter = _adapters.GetValueOrDefault(info.McpName!);
        string tool;
        try
        {
            tool = adapter?.MapArguments?.Invoke(args) ?? info.Name;
        }
        catch (ApiException ex)
        {
            return ErrorResult(ex.Error);
        }

        ApiResult result;
        try
        {
            result = await _backend.CallAsync(tool, JsonSerializer.SerializeToElement(args), ct).ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            result = ApiResult.Fail(0, ex.Error);
        }

        if (result.Error is { } error)
        {
            return ErrorResult(error);
        }

        return adapter?.ShapeResult?.Invoke(result) ?? DefaultResult(result);
    }

    /// <summary>An <c>isError</c> result: <c>code: message</c>, then the details and the retry delay when present.</summary>
    /// <param name="error">The API error.</param>
    /// <returns>The result.</returns>
    public static CallToolResult ErrorResult(ApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = FormatError(error) }],
        };
    }

    /// <summary>Formats an API error for a model: <c>code: message</c>, then details and the retry delay.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The text.</returns>
    public static string FormatError(ApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var text = error.CodeName + ": " + error.Message;
        if (error.Details is { Count: > 0 } details)
        {
            text += "\ndetails: " + string.Join(", ", details.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => d.Key + "=" + d.Value));
        }

        if (error.RetryAfterMs is { } retry)
        {
            text += "\nretryAfterMs: " + retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return text;
    }

    /// <summary>The default success shape: the JSON envelope as one text block, or the plain text of a text tool.</summary>
    /// <param name="result">The API result.</param>
    /// <returns>The MCP result.</returns>
    private static CallToolResult DefaultResult(ApiResult result)
    {
        if (result.Png is not null)
        {
            return ImageResult(result, result.Png);
        }

        var text = result.Text ?? result.ToJson();
        return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
    }

    private Tool BuildProtocolTool(ToolInfo info)
    {
        var schema = JsonNode.Parse(info.Schema)!.AsObject();
        if (_adapters.GetValueOrDefault(info.McpName!)?.ExtendSchema is { } extend)
        {
            extend(schema);
        }

        return new Tool
        {
            Name = info.McpName!,
            Description = info.Description,
            InputSchema = JsonSerializer.SerializeToElement(schema),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = !info.NeedsLock,
                DestructiveHint = info.Mutating,
                IdempotentHint = !info.NeedsLock,
                OpenWorldHint = false,
            },
        };
    }

    /// <summary>How one MCP tool differs from its HTTP route.</summary>
    /// <param name="MapArguments">Rewrites the arguments in place and returns the catalog tool to call, or null.</param>
    /// <param name="ShapeResult">Builds the MCP result from a success, or null for the default.</param>
    /// <param name="ExtendSchema">Adds MCP-only properties to the schema, or null.</param>
    private sealed record ToolAdapter(
        Func<JsonObject, string>? MapArguments,
        Func<ApiResult, CallToolResult>? ShapeResult,
        Action<JsonObject>? ExtendSchema = null);
}
