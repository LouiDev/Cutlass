namespace Cutlass.Agent.Api.Requests;

/// <summary><c>GET /v1/events</c>.</summary>
public sealed class EventsRequest
{
    /// <summary>Return events after this sequence number.</summary>
    public long? After { get; set; }

    /// <summary>Wait at most this long, 25000 ms.</summary>
    public int? TimeoutMs { get; set; }
}
