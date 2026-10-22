using System.Text.Json;
using System.Text.Json.Nodes;
using Cutlass.Agent.Api;
using ModelContextProtocol.Protocol;

namespace Cutlass.Agent.Mcp.Tools;

/// <summary>
/// The preview group: <c>cutlass_render_frame</c> and <c>cutlass_contact_sheet</c> return an
/// <see cref="ImageContentBlock"/> (<c>image/png</c>) plus a text block with the frame, timecode and size (and the
/// contact sheet's cell layout), so the model sees the picture and knows what it shows (5.2).
/// </summary>
public sealed partial class CutlassTools
{
    /// <summary>The MIME type of every image the tools return.</summary>
    public const string PngMimeType = "image/png";

    private static void AddPreviewAdapters(Dictionary<string, ToolAdapter> adapters)
    {
        // Both backends are asked for base64 JSON: it carries the metadata and the image in one body, over HTTP as
        // well as in process, and the image block is built from it.
        static Func<JsonObject, string> Base64(string tool) => args =>
        {
            args["format"] = "base64";
            return tool;
        };

        static void HideFormat(JsonObject schema) => RemoveProperty(schema, "format");

        adapters["cutlass_render_frame"] = new ToolAdapter(Base64("frame.render"), ShapeImage, HideFormat);
        adapters["cutlass_contact_sheet"] = new ToolAdapter(Base64("contact-sheet"), ShapeImage, HideFormat);
    }

    /// <summary>An image result: the PNG as an image block and the envelope (without the base64 text) as a text block.</summary>
    /// <param name="result">The API result.</param>
    /// <param name="png">The PNG bytes.</param>
    /// <returns>The MCP result.</returns>
    private static CallToolResult ImageResult(ApiResult result, byte[] png)
    {
        var envelope = JsonNode.Parse(result.ToJson())!.AsObject();
        (envelope["result"] as JsonObject)?.Remove("pngBase64");
        return new CallToolResult
        {
            Content =
            [
                ImageContentBlock.FromBytes(png, PngMimeType),
                new TextContentBlock { Text = ToRelaxedJson(envelope) },
            ],
        };
    }

    private static CallToolResult ShapeImage(ApiResult result)
    {
        if (result.Png is { } direct)
        {
            return ImageResult(result, direct);
        }

        if (result.Result is { ValueKind: JsonValueKind.Object } json &&
            json.TryGetProperty("pngBase64", out var base64) && base64.ValueKind == JsonValueKind.String &&
            base64.TryGetBytesFromBase64(out var png))
        {
            return ImageResult(result, png);
        }

        return ErrorResult(new ApiError(ErrorCode.Internal, "Cutlass returned no image for this call. Try again, or render a smaller width."));
    }

    /// <summary>Writes a node with the relaxed encoder of the API (AgentJsonContext.Encoder).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The JSON text.</returns>
    private static string ToRelaxedJson(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, AgentJsonContext.WriterOptions))
        {
            node.WriteTo(w);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
