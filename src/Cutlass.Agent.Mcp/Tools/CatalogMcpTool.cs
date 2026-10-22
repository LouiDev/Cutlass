using Cutlass.Agent.Api;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Cutlass.Agent.Mcp.Tools;

/// <summary>
/// One MCP tool backed by a <see cref="ToolCatalog"/> entry: the protocol description comes from the catalog and a
/// call runs through <see cref="CutlassTools.InvokeAsync"/>.
/// </summary>
internal sealed class CatalogMcpTool : McpServerTool
{
    private readonly CutlassTools _tools;
    private readonly ToolInfo _info;

    /// <summary>Creates the tool.</summary>
    /// <param name="tools">The tool runner.</param>
    /// <param name="info">The catalog entry.</param>
    /// <param name="protocolTool">The protocol description (name, description, schema, annotations).</param>
    public CatalogMcpTool(CutlassTools tools, ToolInfo info, Tool protocolTool)
    {
        _tools = tools;
        _info = info;
        ProtocolTool = protocolTool;
    }

    /// <inheritdoc />
    public override Tool ProtocolTool { get; }

    /// <inheritdoc />
    public override IReadOnlyList<object> Metadata { get; } = [];

    /// <inheritdoc />
    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ValueTask<CallToolResult>(_tools.InvokeAsync(_info, request.Params?.Arguments, cancellationToken));
    }

    /// <inheritdoc />
    public override string ToString() => _info.McpName ?? _info.Name;
}
