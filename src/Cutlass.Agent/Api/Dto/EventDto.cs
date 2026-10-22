using System.Text.Json.Serialization;

namespace Cutlass.Agent.Api.Dto;

/// <summary>
/// One event of the long-poll queue (AGENT_INTEGRATION 4.7): <c>projectChanged</c>, <c>lockChanged</c>,
/// <c>exportProgress</c> or <c>playheadChanged</c>, with the fields of its kind.
/// </summary>
public sealed class EventDto
{
    /// <summary>The sequence number.</summary>
    public long Seq { get; set; }

    /// <summary>The kind.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>When it happened (UTC).</summary>
    public DateTimeOffset Time { get; set; }

    /// <summary>projectChanged: the revision.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Revision { get; set; }

    /// <summary>projectChanged: the change kind, for example <c>edit</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChangeKind { get; set; }

    /// <summary>projectChanged: the undo label, if any.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    /// <summary>projectChanged: <c>user</c> or <c>agent</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? By { get; set; }

    /// <summary>lockChanged: the phase.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Phase { get; set; }

    /// <summary>lockChanged: the agent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentName { get; set; }

    /// <summary>exportProgress: the export.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExportId { get; set; }

    /// <summary>exportProgress: the state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; set; }

    /// <summary>exportProgress: the progress.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Fraction { get; set; }

    /// <summary>playheadChanged: the frame.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Frame { get; set; }
}
