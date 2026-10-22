namespace Cutlass.Agent.Api;

/// <summary>What a tool returns.</summary>
public enum ToolResultKind
{
    /// <summary>The JSON envelope.</summary>
    Json,

    /// <summary>A PNG (or, with <c>format=base64</c>, JSON with <c>pngBase64</c>).</summary>
    Png,

    /// <summary>Plain text (the describe text).</summary>
    Text,

    /// <summary>
    /// A document served as is, not wrapped in the envelope: the discovery routes of AGENT_INTEGRATION 9.2 (tool
    /// definitions, OpenAPI, instructions) as JSON or Markdown. Through <c>/v1/call</c> it becomes the envelope's result.
    /// </summary>
    Document,
}
