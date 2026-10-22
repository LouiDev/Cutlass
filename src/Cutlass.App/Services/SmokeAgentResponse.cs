using System.Text.Json;

namespace Cutlass.App.Services;

/// <summary>One answer of the agent API to the smoke runner.</summary>
/// <param name="Code"><c>ok</c> or the error code, for example <c>lock_required</c>.</param>
/// <param name="Message">The error message, or null.</param>
/// <param name="Json">The response envelope (<c>revision</c>, <c>result</c> or <c>error</c>), or null for an image.</param>
/// <param name="Png">The image of an image answer, or null.</param>
/// <param name="Revision">The revision the server reported.</param>
public sealed record SmokeAgentResponse(string Code, string? Message, JsonDocument? Json, byte[]? Png, long Revision);
