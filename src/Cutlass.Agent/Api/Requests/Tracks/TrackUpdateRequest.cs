namespace Cutlass.Agent.Api.Requests.Tracks;

/// <summary><c>POST /v1/edit/track/update</c>: null fields keep their values.</summary>
public sealed class TrackUpdateRequest : MutationRequest
{
    /// <summary>The track.</summary>
    public string? TrackId { get; set; }

    /// <summary>The name.</summary>
    public string? Name { get; set; }

    /// <summary>Video: hidden.</summary>
    public bool? Hidden { get; set; }

    /// <summary>Locked.</summary>
    public bool? Locked { get; set; }

    /// <summary>Targeted.</summary>
    public bool? Targeted { get; set; }

    /// <summary>Audio: muted.</summary>
    public bool? Muted { get; set; }

    /// <summary>Audio: solo.</summary>
    public bool? Solo { get; set; }

    /// <summary>Audio: gain in dB.</summary>
    public double? GainDb { get; set; }
}
