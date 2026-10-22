using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cutlass.App.Services;

/// <summary>
/// The recent projects list (DESIGN 4.1), stored as a JSON array of paths in <c>&lt;data&gt;/recent.json</c>
/// (TECH_BRIEF 5.1). Newest first, at most <see cref="MaxCount"/> entries, written atomically through a unique temp
/// file (<c>&lt;path&gt;.&lt;pid&gt;.&lt;8 hex&gt;.tmp</c>, <see cref="Cutlass.Media.Projects.ProjectStore.TempPath"/>).
/// </summary>
public sealed class RecentProjectsStore
{
    /// <summary>How many projects the list keeps.</summary>
    public const int MaxCount = 10;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly StringComparer _comparer;

    /// <summary>Creates the store.</summary>
    /// <param name="file">The <c>recent.json</c> path.</param>
    /// <param name="pathComparer">Compares paths (case-insensitive on Windows only).</param>
    public RecentProjectsStore(string file, StringComparer pathComparer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(pathComparer);
        File = file;
        _comparer = pathComparer;
    }

    /// <summary>The <c>recent.json</c> path.</summary>
    public string File { get; }

    /// <summary>Puts a path first, removing an older entry of it, and trims to <see cref="MaxCount"/>.</summary>
    /// <param name="list">The current list.</param>
    /// <param name="path">The opened or saved project.</param>
    /// <param name="comparer">Compares paths.</param>
    /// <returns>The new list.</returns>
    public static IReadOnlyList<string> Touch(IReadOnlyList<string> list, string path, StringComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return [path, .. list.Where(p => !comparer.Equals(p, path)).Take(MaxCount - 1)];
    }

    /// <summary>Parses the file content; damaged content gives an empty list.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The paths.</returns>
    public static IReadOnlyList<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(json) is JsonArray array
                ? [.. array.Select(n => n is JsonValue v && v.TryGetValue(out string? s) ? s : null)
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).Take(MaxCount)]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Serializes the list.</summary>
    /// <param name="paths">The paths.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(IEnumerable<string> paths)
    {
        var array = new JsonArray([.. paths.Select(p => (JsonNode?)JsonValue.Create(p))]);
        return array.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>Loads the list (missing or damaged file: empty). Call off the UI thread.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The paths, newest first.</returns>
    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!System.IO.File.Exists(File))
            {
                return [];
            }

            return Parse(await System.IO.File.ReadAllTextAsync(File, Encoding.UTF8, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Saves the list atomically (a unique temp file, then rename).</summary>
    /// <param name="paths">The paths.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    public async Task SaveAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var unique = paths.Distinct(_comparer).Take(MaxCount);
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        // Unique per write, so two instances saving at once never write into the same temp file.
        var tmp = Cutlass.Media.Projects.ProjectStore.TempPath(File);
        try
        {
            await System.IO.File.WriteAllTextAsync(tmp, Serialize(unique), Utf8NoBom, cancellationToken).ConfigureAwait(false);
            System.IO.File.Move(tmp, File, overwrite: true);
        }
        catch
        {
            try
            {
                System.IO.File.Delete(tmp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort; a leftover temp is harmless.
            }

            throw;
        }
    }
}
