namespace Cutlass.Agent.Api.Requests.Clips;

/// <summary><c>POST /v1/edit/speed</c>.</summary>
public sealed class SpeedRequest : MutationRequest
{
    /// <summary>The clips.</summary>
    public List<string>? ClipIds { get; set; }

    /// <summary>25 to 400.</summary>
    public int? Percent { get; set; }

    /// <summary>Shift later clips (true).</summary>
    public bool? RippleFollowing { get; set; }
}
