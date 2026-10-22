namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/playhead</c>.</summary>
public sealed class PlayheadRequest
{
    /// <summary>The frame.</summary>
    public long? Frame { get; set; }
}
