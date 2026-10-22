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
}
