namespace Cutlass.Agent.Api.Requests.Clips;

/// <summary><c>POST /v1/edit/freeze</c>: hold one frame of a video clip (effects batch 4).</summary>
public sealed class FreezeRequest : MutationRequest
{
    /// <summary>The video clip.</summary>
    public string? ClipId { get; set; }

    /// <summary>The timeline frame to hold; must lie inside the clip.</summary>
    public long? Frame { get; set; }

    /// <summary>The hold length in frames (at least 1).</summary>
    public long? DurationFrames { get; set; }

    /// <summary>Shift later clips (true) or overwrite what follows (false).</summary>
    public bool? RippleFollowing { get; set; }
}
