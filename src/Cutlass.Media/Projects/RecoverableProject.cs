using System.Globalization;

namespace Cutlass.Media.Projects;

/// <summary>An autosave that can be recovered (DESIGN 11): newer than its project, or an untitled project's autosave.</summary>
/// <param name="AutosavePath">The autosave file.</param>
/// <param name="ProjectPath">The project the autosave belongs to, or null for an untitled project.</param>
/// <param name="SavedUtc">When the autosave was written (its last write time, UTC).</param>
public sealed record RecoverableProject(string AutosavePath, string? ProjectPath, DateTime SavedUtc)
{
    /// <summary>True for an untitled project's autosave (it lives in the data folder).</summary>
    public bool IsUntitled => ProjectPath is null;

    /// <summary>The project name, or "Untitled" for an untitled project.</summary>
    public string Name => ProjectPath is null ? "Untitled" : Path.GetFileNameWithoutExtension(ProjectPath);

    /// <summary>When the autosave was written, in local time, for example <c>2026-10-22 14:05</c>.</summary>
    public string SavedText => SavedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
