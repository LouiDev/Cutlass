namespace Cutlass.App.ViewModels;

/// <summary>One entry of the welcome overlay's recent list (DESIGN 4.1). Missing files are greyed out.</summary>
/// <param name="Path">The project file.</param>
/// <param name="Exists">True when the file exists.</param>
public sealed record RecentProjectItem(string Path, bool Exists)
{
    /// <summary>The project name (file name without extension).</summary>
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>The folder holding the project.</summary>
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}
