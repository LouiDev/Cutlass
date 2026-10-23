namespace Cutlass.Agent.Api.Requests.Project;

/// <summary><c>POST /v1/shutdown</c>.</summary>
public sealed class ShutdownRequest
{
    /// <summary>Save the project first.</summary>
    public bool? Save { get; set; }
}
