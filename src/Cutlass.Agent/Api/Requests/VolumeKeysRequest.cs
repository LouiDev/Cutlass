using Cutlass.Agent.Api.Dto;

namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/edit/volume-keys</c>: replaces the envelope (empty clears).</summary>
public sealed class VolumeKeysRequest : MutationRequest
{
    /// <summary>The audio clip.</summary>
    public string? ClipId { get; set; }

    /// <summary>The keys relative to the clip start.</summary>
    public List<VolumeKeyDto>? Keys { get; set; }
}
