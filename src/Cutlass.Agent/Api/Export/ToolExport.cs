namespace Cutlass.Agent.Api.Export;

/// <summary>
/// Picks the exporter for a <c>format</c> of <c>GET /v1/tools</c> (AGENT_INTEGRATION 9.2). The app, the MCP exe and
/// the tests can render the same documents without a server.
/// </summary>
public static class ToolExport
{
    /// <summary>The formats, the default first.</summary>
    public static IReadOnlyList<string> Formats { get; } = ["json", "openai", "anthropic", "gemini", "markdown"];

    /// <summary>Renders the catalog in a format.</summary>
    /// <param name="format">One of <see cref="Formats"/> (case-insensitive), or null for <c>json</c>.</param>
    /// <param name="text">The document.</param>
    /// <param name="mediaType"><c>application/json</c>, or <c>text/markdown</c> for markdown.</param>
    /// <returns>False for an unknown format.</returns>
    public static bool TryRender(string? format, out string text, out string mediaType)
    {
        mediaType = "application/json";
        switch (format?.Trim().ToLowerInvariant())
        {
            case null or "" or "json":
                text = ToolsDocument.Build();
                return true;
            case "openai":
                text = OpenAiToolExporter.Export();
                return true;
            case "anthropic":
                text = AnthropicToolExporter.Export();
                return true;
            case "gemini":
                text = GeminiToolExporter.Export();
                return true;
            case "markdown":
                text = MarkdownToolExporter.Export();
                mediaType = "text/markdown";
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }
}
