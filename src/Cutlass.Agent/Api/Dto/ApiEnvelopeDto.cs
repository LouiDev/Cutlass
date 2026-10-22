using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cutlass.Agent.Api.Dto;

/// <summary>The JSON envelope of every API response (AGENT_INTEGRATION 4.2 and 4.3).</summary>
public sealed class ApiEnvelopeDto
{
    /// <summary>The revision after the call.</summary>
    public long Revision { get; set; }

    /// <summary>Warnings (success only).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Warnings { get; set; }

    /// <summary>The result (success only).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; set; }

    /// <summary>Mutating calls: false when the edit clamped to nothing.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Changed { get; set; }

    /// <summary>Mutating calls: the ids to select.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Select { get; set; }

    /// <summary>Mutating calls: the label Undo would revert.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UndoLabel { get; set; }

    /// <summary>Mutating calls with <c>returnProject</c>: the summary.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Project { get; set; }

    /// <summary>The error (failure only).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiErrorDto? Error { get; set; }
}
