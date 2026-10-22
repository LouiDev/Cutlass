namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/project/new</c>.</summary>
public sealed class NewProjectRequest
{
    /// <summary>Canvas width.</summary>
    public int? Width { get; set; }

    /// <summary>Canvas height.</summary>
    public int? Height { get; set; }

    /// <summary>Frame rate such as <c>30000/1001</c>.</summary>
    public string? FrameRate { get; set; }

    /// <summary>Headless only: drop unsaved changes.</summary>
    public bool? DiscardChanges { get; set; }
}
