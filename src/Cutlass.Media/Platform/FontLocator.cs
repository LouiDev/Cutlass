using System.Collections.Immutable;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.Media.Platform;

/// <summary>
/// Makes title fonts available to drawtext (TECH_BRIEF 5.5). The bundled static Inter TTFs ship in
/// <c>&lt;appdir&gt;/fonts/</c> and are copied into the work directory when missing or of a different size; user fonts
/// are copied once as <c>font_&lt;sha8&gt;.&lt;ext&gt;</c>. drawtext always gets <c>fontfile=</c> with a bare name,
/// never <c>font=</c> (fontconfig is slow and noisy on Windows).
/// </summary>
public sealed class FontLocator
{
    /// <summary>The prefix of a bundled font reference, for example <c>builtin:Inter-Bold</c>.</summary>
    public const string BuiltinPrefix = "builtin:";

    /// <summary>The largest user font file that is read (64 MiB); a font path from a project could name anything.</summary>
    public const long MaxFontFileBytes = 64L * 1024 * 1024;

    private readonly string _appDirectory;
    private readonly WorkDirectory _work;
    private readonly ProcessRunner? _runner;

    /// <summary>Creates a locator.</summary>
    /// <param name="appDirectory">The folder the app runs from (its <c>fonts/</c> subfolder holds Inter).</param>
    /// <param name="work">The work directory fonts are copied into.</param>
    /// <param name="runner">Runs <c>fc-match</c> and <c>fc-list</c> on Linux; may be null on Windows.</param>
    public FontLocator(string appDirectory, WorkDirectory work, ProcessRunner? runner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        ArgumentNullException.ThrowIfNull(work);
        _appDirectory = appDirectory;
        _work = work;
        _runner = runner;
    }

    /// <summary>The bundled font files, copied into the work directory under these exact names.</summary>
    public static ImmutableArray<string> BundledFontFiles { get; } = ["Inter-Regular.ttf", "Inter-SemiBold.ttf", "Inter-Bold.ttf"];

    /// <summary>Replaces the Windows font folder (tests: a machine without its system fonts).</summary>
    internal string? WindowsFontsOverride { get; init; }

    /// <summary>The folder holding the bundled fonts, <c>&lt;appdir&gt;/fonts</c>.</summary>
    public string BundledFontsDirectory => Path.Combine(_appDirectory, "fonts");

    /// <summary>Creates a locator for this process's app folder.</summary>
    /// <param name="work">The work directory.</param>
    /// <param name="runner">The process runner.</param>
    /// <returns>The locator.</returns>
    public static FontLocator ForCurrentApp(WorkDirectory work, ProcessRunner runner) =>
        new(AppContext.BaseDirectory, work, runner);

    /// <summary>The full path of a bundled font next to the executable.</summary>
    /// <param name="fileName">One of <see cref="BundledFontFiles"/>.</param>
    /// <returns>The path, or null when the file is missing.</returns>
    public string? FindBundledFont(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var path = Path.Combine(BundledFontsDirectory, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Copies every bundled font into the work directory when missing or of a different size.</summary>
    /// <returns>The bare names now present in the work directory (all three when the install is complete).</returns>
    public IReadOnlyList<string> EnsureBundledFonts()
    {
        var present = new List<string>();
        foreach (var file in BundledFontFiles)
        {
            if (FindBundledFont(file) is { } source)
            {
                _work.CopyIfChanged(source, file);
                present.Add(file);
            }
        }

        return present;
    }

    /// <summary>
    /// Resolves a title font reference to a bare name in the work directory: <c>builtin:Inter-Bold</c> for a bundled
    /// font, or the absolute path of a user font. Falls back to a system font when it is unavailable, and to the
    /// bundled Inter when even that cannot be read. A user font path comes from a project file, so it is read only
    /// when it names a regular <c>.ttf</c>, <c>.otf</c> or <c>.ttc</c> file of at most <see cref="MaxFontFileBytes"/>
    /// (<see cref="IsAcceptableFontFile"/>); devices, FIFOs, huge and unreadable files take the fallback (the title
    /// shows the fallback warning). Never throws for file system errors.
    /// </summary>
    /// <param name="fontReference">The reference from the title spec.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The resolution, or null when not even a fallback font exists.</returns>
    public async Task<FontResolution?> ResolveAsync(string fontReference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fontReference);
        if (fontReference.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
        {
            var file = fontReference[BuiltinPrefix.Length..] + ".ttf";
            if (BundledFontFiles.Contains(file) && FindBundledFont(file) is { } source)
            {
                _work.CopyIfChanged(source, file);
                return new FontResolution(file, IsFallback: false);
            }
        }
        else if (fontReference.Length > 0 && IsAcceptableFontFile(fontReference) && TryImport(fontReference) is { } imported)
        {
            return new FontResolution(imported, IsFallback: false);
        }

        return await ResolveFallbackAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// True when a user font path names a regular file with a <c>.ttf</c>, <c>.otf</c> or <c>.ttc</c> extension of at
    /// most <see cref="MaxFontFileBytes"/>. Never opens the file.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>True when the file may be read.</returns>
    public static bool IsAcceptableFontFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!IsFontFile(path) || FileIdentity.KindOf(path) != FileKind.Regular)
        {
            return false;
        }

        try
        {
            var length = new FileInfo(path).Length;
            return length > 0 && length <= MaxFontFileBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Finds a system fallback font: <c>segoeui.ttf</c> then <c>arial.ttf</c> on Windows;
    /// <c>fc-match -f %{file} sans-serif</c> then DejaVu Sans on Linux.
    /// </summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The fallback copied into the work directory, or null when none exists.</returns>
    public async Task<FontResolution?> ResolveFallbackAsync(CancellationToken cancellationToken)
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            var fonts = WindowsFontsDirectory();
            candidates.Add(Path.Combine(fonts, "segoeui.ttf"));
            candidates.Add(Path.Combine(fonts, "arial.ttf"));
        }
        else
        {
            if (await RunToolAsync("fc-match", ["-f", "%{file}", "sans-serif"], cancellationToken).ConfigureAwait(false) is { } matched)
            {
                candidates.Add(matched.Trim());
            }

            candidates.Add("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");
        }

        foreach (var candidate in candidates)
        {
            if (candidate.Length > 0 && IsAcceptableFontFile(candidate) && TryImport(candidate) is { } imported)
            {
                return new FontResolution(imported, IsFallback: true);
            }
        }

        // The last resort is the bundled Inter, which drawtext can always read from the work directory.
        if (FindBundledFont(BundledFontFiles[0]) is { } inter)
        {
            try
            {
                _work.CopyIfChanged(inter, BundledFontFiles[0]);
                return new FontResolution(BundledFontFiles[0], IsFallback: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing usable at all.
            }
        }

        return null;
    }

    /// <summary>
    /// Lists installed fonts. Windows: <c>*.ttf</c> and <c>*.otf</c> in <c>%WINDIR%\Fonts</c> and the per-user font
    /// folder, named by file. Linux: <c>fc-list</c>, else a scan of the usual font folders.
    /// </summary>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The fonts sorted by family, then path.</returns>
    public async Task<IReadOnlyList<SystemFont>> ListSystemFontsAsync(CancellationToken cancellationToken)
    {
        var fonts = new List<SystemFont>();
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            fonts.AddRange(ScanFolders([WindowsFontsDirectory(), Path.Combine(local, "Microsoft", "Windows", "Fonts")], recursive: false));
        }
        else
        {
            var listing = await RunToolAsync("fc-list", ["--format", "%{family[0]}|%{file}\n"], cancellationToken).ConfigureAwait(false);
            if (listing is not null)
            {
                fonts.AddRange(ParseFcList(listing));
            }

            if (fonts.Count == 0)
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                fonts.AddRange(ScanFolders(
                    ["/usr/share/fonts", "/usr/local/share/fonts", Path.Combine(home, ".local", "share", "fonts")],
                    recursive: true));
            }
        }

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return [.. fonts
            .DistinctBy(f => f.Path, comparer)
            .OrderBy(f => f.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Path, StringComparer.Ordinal)];
    }

    /// <summary>Parses <c>fc-list --format "%{family[0]}|%{file}\n"</c> output, keeping TrueType and OpenType files.</summary>
    /// <param name="listing">The fc-list stdout.</param>
    /// <returns>The fonts, in listing order.</returns>
    public static IReadOnlyList<SystemFont> ParseFcList(string listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        var fonts = new List<SystemFont>();
        foreach (var raw in listing.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var bar = line.LastIndexOf('|');
            if (bar <= 0 || bar == line.Length - 1)
            {
                continue;
            }

            var path = line[(bar + 1)..].Trim();
            if (IsFontFile(path))
            {
                fonts.Add(new SystemFont(UnescapeFcValue(line[..bar].Trim()), path));
            }
        }

        return fonts;
    }

    /// <summary>
    /// Removes fontconfig's backslash escapes from a pattern value (<c>%{family}</c> prints <c>\-</c>, <c>\:</c>,
    /// <c>\,</c> and <c>\\</c> escaped on some fontconfig versions), so a family reads as the user sees it.
    /// </summary>
    /// <param name="value">The printed value.</param>
    /// <returns>The plain value.</returns>
    public static string UnescapeFcValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    private string? TryImport(string path)
    {
        try
        {
            return _work.ImportFont(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsFontFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase);
    }

    private string WindowsFontsDirectory()
    {
        if (WindowsFontsOverride is not null)
        {
            return WindowsFontsOverride;
        }

        var windir = Environment.GetEnvironmentVariable("WINDIR");
        return string.IsNullOrEmpty(windir)
            ? Environment.GetFolderPath(Environment.SpecialFolder.Fonts)
            : Path.Combine(windir, "Fonts");
    }

    private static IEnumerable<SystemFont> ScanFolders(IEnumerable<string> folders, bool recursive)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
        };
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                var ext = Path.GetExtension(file);
                if (ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) || ext.Equals(".otf", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new SystemFont(Path.GetFileNameWithoutExtension(file), file);
                }
            }
        }
    }

    private async Task<string?> RunToolAsync(string tool, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (_runner is null)
        {
            return null;
        }

        try
        {
            _work.EnsureCreated();
            var spec = new ProcessSpec(tool, args, _work.Path, false, ProcessPurpose.Probe, false);
            var result = await _runner.RunAsync(spec, TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? result.StdoutText : null;
        }
        catch (FfmpegException)
        {
            // fontconfig tools are optional; the fixed fallbacks still apply.
            return null;
        }
    }
}
