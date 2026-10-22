namespace Cutlass.Agent.Api.Requests;

/// <summary><c>GET /v1/exports/{id}</c> and <c>POST /v1/exports/{id}/cancel</c>.</summary>
public sealed class ExportIdRequest
{
    /// <summary>The export id.</summary>
    public string? ExportId { get; set; }

    /// <summary>Wait at most this long for a state change (25000).</summary>
    public int? WaitMs { get; set; }
}
