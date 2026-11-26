namespace Cutlass.Agent.Api.Dto.Status;

/// <summary>The <c>project</c> part of the status.</summary>
public sealed class ProjectStatusDto
{
    /// <summary>The project file, or null while untitled.</summary>
    public string? Path { get; set; }

    /// <summary>The project name (file name without extension, or <c>Untitled</c>).</summary>
    public string Name { get; set; } = "Untitled";

    /// <summary>True with unsaved changes.</summary>
    public bool Dirty { get; set; }

    /// <summary>True when a project is open (always true in v1: there is always a project).</summary>
    public bool HasProject { get; set; } = true;

    /// <summary>Media whose files are missing.</summary>
    public List<string> OfflineMediaIds { get; set; } = [];
}
