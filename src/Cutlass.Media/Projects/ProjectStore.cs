using System.Globalization;
using System.Text;
using Cutlass.Core.Model;
using Cutlass.Core.Serialization;

namespace Cutlass.Media.Projects;

/// <summary>
/// Loads and saves <c>.cutlass</c> projects, their <c>.cutlass.user</c> sidecars and <c>.cutlass.autosave</c> files
/// through <see cref="ProjectSerializer"/> (TECH_BRIEF 3.5, DESIGN 11). Every save is atomic: write a temp file,
/// flush to disk, then <c>File.Move(tmp, path, overwrite: true)</c>, so a crash never leaves a half-written project.
/// The temp name is unique per write (<c>&lt;path&gt;.&lt;pid&gt;.&lt;8 hex&gt;.tmp</c>), so two writers (two instances, or a
/// crash autosave racing a timed one) never share a temp file.
/// </summary>
public sealed class ProjectStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The sidecar path of a project: <c>&lt;name&gt;.cutlass.user</c>.</summary>
    /// <param name="projectPath">The project file.</param>
    /// <returns>The sidecar path.</returns>
    public static string SidecarPath(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        return projectPath + ProjectSerializer.SidecarSuffix;
    }

    /// <summary>The autosave path of a project: <c>&lt;name&gt;.cutlass.autosave</c>.</summary>
    /// <param name="projectPath">The project file.</param>
    /// <returns>The autosave path.</returns>
    public static string AutosavePath(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        return projectPath + ProjectSerializer.AutosaveSuffix;
    }

    /// <summary>Reads a project file.</summary>
    /// <param name="path">The <c>.cutlass</c> (or autosave) file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The project, migrated to the current schema.</returns>
    /// <exception cref="InvalidDataException">When the file is not a readable Cutlass project.</exception>
    /// <exception cref="IOException">When the file cannot be read.</exception>
    public async Task<Project> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return WithNativePaths(ProjectSerializer.Deserialize(bytes));
    }

    /// <summary>
    /// Converts the media paths of a loaded project from the file's forward slashes (TECH_BRIEF 3.5) to this OS's
    /// separator, so they compare equal to paths from pickers and drops. Relative paths stay in file form; the
    /// serializer converts them when it resolves them. On Linux nothing changes. Returns the same reference when no
    /// path changed.
    /// </summary>
    /// <param name="project">The loaded project.</param>
    /// <returns>The project with native media paths.</returns>
    public static Project WithNativePaths(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (Path.DirectorySeparatorChar == '/' || !project.Media.Any(m => m.Path.Contains('/', StringComparison.Ordinal)))
        {
            return project;
        }

        return project with
        {
            Media = [.. project.Media.Select(m => m with { Path = m.Path.Replace('/', Path.DirectorySeparatorChar) })],
        };
    }

    /// <summary>Saves a project atomically. Media relative paths are computed from <paramref name="path"/>.</summary>
    /// <param name="project">The project.</param>
    /// <param name="path">The <c>.cutlass</c> file.</param>
    /// <param name="cancellationToken">Cancels the write (the old file stays intact).</param>
    /// <returns>A task that completes when the file is in place.</returns>
    public Task SaveAsync(Project project, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        return WriteAtomicAsync(full, ProjectSerializer.SerializeToUtf8Bytes(project, full), cancellationToken);
    }

    /// <summary>Writes the autosave next to the project (or to the given untitled autosave path).</summary>
    /// <param name="project">The project.</param>
    /// <param name="autosavePath">The autosave file, normally <see cref="AutosavePath"/>.</param>
    /// <param name="relativeTo">The project file whose folder relative media paths refer to, or null for an untitled
    /// project (stored relative paths are kept).</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    public Task SaveAutosaveAsync(Project project, string autosavePath, string? relativeTo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(autosavePath);
        var bytes = ProjectSerializer.SerializeToUtf8Bytes(project, relativeTo is null ? null : Path.GetFullPath(relativeTo));
        return WriteAtomicAsync(Path.GetFullPath(autosavePath), bytes, cancellationToken);
    }

    /// <summary>Reads a project's view state. A missing or damaged sidecar gives the defaults.</summary>
    /// <param name="projectPath">The project file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The view state.</returns>
    public async Task<ViewState> LoadViewStateAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var sidecar = SidecarPath(projectPath);
        string? json = null;
        try
        {
            if (File.Exists(sidecar))
            {
                json = await File.ReadAllTextAsync(sidecar, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable sidecar never blocks opening the project.
        }

        return ProjectSerializer.DeserializeViewState(json);
    }

    /// <summary>Saves a project's view state atomically. Writing it never dirties the project.</summary>
    /// <param name="state">The view state.</param>
    /// <param name="projectPath">The project file.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the sidecar is in place.</returns>
    public Task SaveViewStateAsync(ViewState state, string projectPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var sidecar = Path.GetFullPath(SidecarPath(projectPath));
        return WriteAtomicAsync(sidecar, Utf8NoBom.GetBytes(ProjectSerializer.SerializeViewState(state)), cancellationToken);
    }

    /// <summary>Deletes the autosave of a project if it exists (after a clean save or close).</summary>
    /// <param name="autosavePath">The autosave file.</param>
    public void DeleteAutosave(string autosavePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(autosavePath);
        if (File.Exists(autosavePath))
        {
            File.Delete(autosavePath);
        }
    }

    /// <summary>A unique temp file next to <paramref name="path"/>: <c>&lt;path&gt;.&lt;pid&gt;.&lt;8 hex&gt;.tmp</c>.</summary>
    /// <param name="path">The file about to be written.</param>
    /// <returns>The temp path.</returns>
    public static string TempPath(string path) =>
        string.Create(CultureInfo.InvariantCulture, $"{path}.{Environment.ProcessId}.{Random.Shared.Next():x8}.tmp");

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var temp = TempPath(path);
        try
        {
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Overwritten by the next save.
            }

            throw;
        }
    }
}
