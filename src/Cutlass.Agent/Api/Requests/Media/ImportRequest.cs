namespace Cutlass.Agent.Api.Requests.Media;

/// <summary><c>POST /v1/media/import</c>.</summary>
public sealed class ImportRequest
{
    /// <summary>The files, at most 32.</summary>
    public List<string>? Paths { get; set; }
}
