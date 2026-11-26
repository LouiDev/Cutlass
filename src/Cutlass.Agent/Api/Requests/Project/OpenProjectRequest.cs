namespace Cutlass.Agent.Api.Requests.Project;

/// <summary><c>POST /v1/project/open</c>.</summary>
public sealed class OpenProjectRequest
{
    /// <summary>The project file.</summary>
    public string? Path { get; set; }

    /// <summary>Headless only: drop unsaved changes.</summary>
    public bool? DiscardChanges { get; set; }
}
