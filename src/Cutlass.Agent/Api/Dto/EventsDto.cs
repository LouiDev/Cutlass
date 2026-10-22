namespace Cutlass.Agent.Api.Dto;

/// <summary>The result of <c>GET /v1/events</c>.</summary>
public sealed class EventsDto
{
    /// <summary>The events after the requested sequence number.</summary>
    public List<EventDto> Events { get; set; } = [];

    /// <summary>The <c>after</c> value of the next poll.</summary>
    public long Next { get; set; }

    /// <summary>True when events were dropped: re-read the project and the lock.</summary>
    public bool Gap { get; set; }
}
