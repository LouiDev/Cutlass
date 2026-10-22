namespace Cutlass.Agent.Api.Requests;

/// <summary>A call on one media asset (<c>GET</c> and <c>DELETE /v1/media/{id}</c>).</summary>
public sealed class MediaIdRequest : MutationRequest
{
    /// <summary>The media id.</summary>
    public string? MediaId { get; set; }
}
