using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cutlass.Agent.Api.Requests.Clips;

/// <summary>
/// <c>POST /v1/edit/in-out</c>: a number sets, <c>null</c> clears, an absent field keeps the point (unspecified in
/// the spec; keeping is the least surprising).
/// </summary>
public sealed class InOutRequest : MutationRequest
{
    /// <summary>The In frame, JSON null, or absent (<see cref="JsonValueKind.Undefined"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement InFrame { get; set; }

    /// <summary>The Out frame (exclusive), JSON null, or absent (<see cref="JsonValueKind.Undefined"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement OutFrame { get; set; }
}
