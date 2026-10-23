namespace Cutlass.Agent.Api.Requests.Project;

/// <summary><c>POST /v1/project/save-as</c>.</summary>
public sealed class SaveAsRequest
{
    /// <summary>The new path.</summary>
    public string? Path { get; set; }

    /// <summary>Replace an existing file.</summary>
    public bool? Overwrite { get; set; }
}
