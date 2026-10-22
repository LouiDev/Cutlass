using System.Text.Json;

namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/call</c>: any tool by catalog name.</summary>
public sealed class CallRequest
{
    /// <summary>The catalog name, for example <c>edit.split</c>.</summary>
    public string? Tool { get; set; }

    /// <summary>The tool's arguments (route and query arguments included).</summary>
    public JsonElement? Args { get; set; }
}
