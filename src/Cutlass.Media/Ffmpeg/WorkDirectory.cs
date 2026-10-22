using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cutlass.Media.Platform;

namespace Cutlass.Media.Ffmpeg;

/// <summary>
/// The directory every ffmpeg process runs in, <c>&lt;cache&gt;/work/</c> (TECH_BRIEF 5.5). Graphs refer to the files
/// in it by bare names only (<c>^[A-Za-z0-9_.-]+$</c>), so no drive colon, apostrophe or Unicode character ever
/// needs escaping inside a filtergraph. Files are content-addressed and written temp-then-rename, so several app
/// instances can share the directory.
/// </summary>
public sealed partial class WorkDirectory
{
    /// <summary>How old <c>.txt</c> and <c>.ffg</c> files get before the startup cleanup deletes them.</summary>
    public static readonly TimeSpan StaleAge = TimeSpan.FromDays(2);

    /// <summary>How old an abandoned <c>*.tmp-*</c> file gets before the startup cleanup deletes it.</summary>
    public static readonly TimeSpan StaleTempAge = TimeSpan.FromHours(1);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Creates the work directory object. Call <see cref="EnsureCreated"/> before use.</summary>
    /// <param name="path">The directory.</param>
    public WorkDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = AppPaths.Normalize(path);
    }

    /// <summary>The full path of the directory.</summary>
    public string Path { get; }

    /// <summary>Creates the work directory for the given app paths (<see cref="AppPaths.WorkDirectory"/>).</summary>
    /// <param name="paths">The app paths.</param>
    /// <returns>The work directory.</returns>
    public static WorkDirectory For(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new WorkDirectory(paths.WorkDirectory);
    }

    /// <summary>True when <paramref name="name"/> is a bare file name usable inside a graph.</summary>
    /// <param name="name">The candidate name.</param>
    /// <returns>True for names matching <c>^[A-Za-z0-9_.-]+$</c> other than <c>.</c> and <c>..</c>.</returns>
    public static bool IsBareName(string? name) =>
        !string.IsNullOrEmpty(name) && name is not "." and not ".." && BareNameRegex().IsMatch(name);

    /// <summary>The first <paramref name="hexChars"/> lowercase hex digits of the SHA-256 of <paramref name="bytes"/>.</summary>
    /// <param name="bytes">The content.</param>
    /// <param name="hexChars">How many hex digits (at most 64).</param>
    /// <returns>The short hash.</returns>
    public static string ShortHash(ReadOnlySpan<byte> bytes, int hexChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hexChars);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hexChars, 64);
        return Convert.ToHexStringLower(SHA256.HashData(bytes))[..hexChars];
    }

    /// <summary>Creates the directory if it does not exist.</summary>
    public void EnsureCreated() => Directory.CreateDirectory(Path);

    /// <summary>The full path of a bare name inside the directory.</summary>
    /// <param name="bareName">The bare file name.</param>
    /// <returns>The full path.</returns>
    /// <exception cref="ArgumentException">When <paramref name="bareName"/> is not a bare name.</exception>
    public string GetFullPath(string bareName)
    {
        if (!IsBareName(bareName))
        {
            throw new ArgumentException($"'{bareName}' is not a bare file name ([A-Za-z0-9_.-]+).", nameof(bareName));
        }

        return System.IO.Path.Combine(Path, bareName);
    }

    /// <summary>
    /// Writes title text as <c>t_&lt;sha8&gt;.txt</c>: UTF-8 without BOM, line endings normalised to <c>\n</c> and no
    /// trailing newline (drawtext would draw an empty last line).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The bare file name.</returns>
    public string WriteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
        var bytes = Utf8NoBom.GetBytes(normalized);
        var name = "t_" + ShortHash(bytes, 8) + ".txt";
        WriteIfMissing(name, bytes);
        return name;
    }

    /// <summary>Writes a filtergraph script as <c>g_&lt;sha12&gt;.ffg</c> (UTF-8 without BOM, content as given).</summary>
    /// <param name="graph">The graph text.</param>
    /// <returns>The bare file name.</returns>
    public string WriteScript(string graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var bytes = Utf8NoBom.GetBytes(graph);
        var name = "g_" + ShortHash(bytes, 12) + ".ffg";
        WriteIfMissing(name, bytes);
        return name;
    }

    /// <summary>
    /// Writes a file whose name the caller chose (for example a compiler <c>AuxFile</c>): UTF-8 without BOM. An
    /// existing file with the same bytes is kept; a different one is replaced atomically.
    /// </summary>
    /// <param name="bareName">The bare file name.</param>
    /// <param name="utf8Content">The content.</param>
    /// <returns>The full path.</returns>
    /// <exception cref="ArgumentException">When <paramref name="bareName"/> is not a bare name.</exception>
    public string WriteFile(string bareName, string utf8Content)
    {
        ArgumentNullException.ThrowIfNull(utf8Content);
        var full = GetFullPath(bareName);
        var bytes = Utf8NoBom.GetBytes(utf8Content);
        if (File.Exists(full) && new FileInfo(full).Length == bytes.Length && File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes))
        {
            return full;
        }

        WriteAtomic(full, bytes);
        return full;
    }

    /// <summary>
    /// Copies a user font into the directory once, as <c>font_&lt;sha8&gt;.&lt;ext&gt;</c> keyed by its content.
    /// </summary>
    /// <param name="sourcePath">The font file anywhere on disk (any characters).</param>
    /// <returns>The bare file name.</returns>
    public string ImportFont(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var bytes = File.ReadAllBytes(sourcePath);
        var ext = System.IO.Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
        if (!IsBareName(ext) || ext.Length == 0)
        {
            ext = "ttf";
        }

        var name = "font_" + ShortHash(bytes, 8) + "." + ext;
        WriteIfMissing(name, bytes);
        return name;
    }

    /// <summary>
    /// Copies a file under a fixed bare name when it is missing or has a different size (the bundled Inter fonts).
    /// </summary>
    /// <param name="sourcePath">The source file.</param>
    /// <param name="bareName">The bare target name.</param>
    /// <returns>The full target path.</returns>
    public string CopyIfChanged(string sourcePath, string bareName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var full = GetFullPath(bareName);
        var source = new FileInfo(sourcePath);
        if (File.Exists(full) && new FileInfo(full).Length == source.Length)
        {
            return full;
        }

        WriteAtomic(full, File.ReadAllBytes(sourcePath));
        return full;
    }

    /// <summary>
    /// The startup cleanup: deletes <c>.txt</c> and <c>.ffg</c> files older than <see cref="StaleAge"/> and abandoned
    /// <c>*.tmp-*</c> files older than <see cref="StaleTempAge"/>. Files in use by another instance are skipped.
    /// </summary>
    /// <param name="nowUtc">The current time (UTC).</param>
    /// <returns>The number of files deleted.</returns>
    public int DeleteStale(DateTime nowUtc)
    {
        if (!Directory.Exists(Path))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(Path))
        {
            var name = System.IO.Path.GetFileName(file);
            var isTemp = name.Contains(".tmp-", StringComparison.Ordinal);
            var ext = System.IO.Path.GetExtension(name);
            var isScratch = ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ffg", StringComparison.OrdinalIgnoreCase);
            if (!isTemp && !isScratch)
            {
                continue;
            }

            try
            {
                var age = nowUtc - File.GetLastWriteTimeUtc(file);
                if (age > (isTemp ? StaleTempAge : StaleAge))
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use by another instance; try again next start.
            }
        }

        return deleted;
    }

    private void WriteIfMissing(string bareName, byte[] bytes)
    {
        var full = GetFullPath(bareName);
        if (File.Exists(full) && new FileInfo(full).Length == bytes.Length)
        {
            // Content-addressed: the same name with the same size is the same content.
            return;
        }

        WriteAtomic(full, bytes);
    }

    private void WriteAtomic(string fullPath, byte[] bytes)
    {
        EnsureCreated();
        var temp = fullPath + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, fullPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);

            // Another instance may have produced the same content-addressed file at the same moment.
            if (!File.Exists(fullPath) || new FileInfo(fullPath).Length != bytes.Length)
            {
                throw;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Removed by the startup cleanup later.
        }
    }

    // \z instead of $: in .NET "$" also matches before a final newline.
    [GeneratedRegex(@"^[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex BareNameRegex();
}
