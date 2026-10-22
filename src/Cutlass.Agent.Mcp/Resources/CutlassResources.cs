using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cutlass.Agent.Api;
using Cutlass.Agent.Mcp.Backend;
using Cutlass.Agent.Mcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Cutlass.Agent.Mcp.Resources;

/// <summary>
/// The MCP resources (docs/AGENT_INTEGRATION.md 5.3): reads that need no lock. <c>cutlass://project</c> is the
/// summary JSON, <c>cutlass://project/describe</c> the compact text and the template <c>cutlass://frame/{n}</c> one
/// timeline frame as a 640 px wide PNG. Failures surface as MCP errors reading <c>code: message</c>.
/// </summary>
[McpServerResourceType]
public sealed class CutlassResources
{
    /// <summary>The width of frame resources in pixels (5.3).</summary>
    public const int FrameWidth = 640;

    private readonly IAgentBackend _backend;

    /// <summary>Creates the resources over the session's backend.</summary>
    /// <param name="backend">Where reads go.</param>
    public CutlassResources(IAgentBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>The project summary as JSON.</summary>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The JSON contents.</returns>
    [McpServerResource(UriTemplate = "cutlass://project", Name = "project", MimeType = "application/json")]
    [Description("The open project as JSON: sequence, tracks, clips, transitions, markers and media.")]
    public async Task<TextResourceContents> ProjectAsync(CancellationToken ct)
    {
        var result = await CallAsync("project.get", new JsonObject(), ct).ConfigureAwait(false);
        var json = result.Result is { } value ? value.GetRawText() : "null";
        return new TextResourceContents { Uri = "cutlass://project", MimeType = "application/json", Text = json };
    }

    /// <summary>The compact text overview of the project.</summary>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The text contents.</returns>
    [McpServerResource(UriTemplate = "cutlass://project/describe", Name = "project-describe", MimeType = "text/plain")]
    [Description("A short text overview of the timeline, one line per track; cheaper than the project JSON.")]
    public async Task<TextResourceContents> DescribeAsync(CancellationToken ct)
    {
        var result = await CallAsync("project.describe", new JsonObject(), ct).ConfigureAwait(false);
        return new TextResourceContents { Uri = "cutlass://project/describe", MimeType = "text/plain", Text = result.Text ?? string.Empty };
    }

    /// <summary>One timeline frame as a PNG, 640 pixels wide.</summary>
    /// <param name="n">The timeline frame.</param>
    /// <param name="ct">Cancels the render.</param>
    /// <returns>The PNG contents.</returns>
    [McpServerResource(UriTemplate = "cutlass://frame/{n}", Name = "frame", MimeType = "image/png")]
    [Description("One timeline frame as a PNG, 640 pixels wide. n is the timeline frame number.")]
    public async Task<BlobResourceContents> FrameAsync([Description("The timeline frame.")] long n, CancellationToken ct)
    {
        var args = new JsonObject { ["frame"] = n, ["width"] = FrameWidth, ["format"] = "base64" };
        var result = await CallAsync("frame.render", args, ct).ConfigureAwait(false);
        byte[]? png = result.Png;
        if (png is null && result.Result is { ValueKind: JsonValueKind.Object } json &&
            json.TryGetProperty("pngBase64", out var base64) && base64.ValueKind == JsonValueKind.String)
        {
            png = base64.GetBytesFromBase64();
        }

        if (png is null)
        {
            throw new McpException("internal: Cutlass returned no image for this frame.");
        }

        var uri = "cutlass://frame/" + n.ToString(CultureInfo.InvariantCulture);
        return BlobResourceContents.FromBytes(png, uri, CutlassTools.PngMimeType);
    }

    private async Task<ApiResult> CallAsync(string tool, JsonObject args, CancellationToken ct)
    {
        var result = await _backend.CallAsync(tool, JsonSerializer.SerializeToElement(args), ct).ConfigureAwait(false);
        if (result.Error is { } error)
        {
            throw new McpException(CutlassTools.FormatError(error));
        }

        return result;
    }
}
