using Cutlass.Compiler;
using Cutlass.Core.Model;
using Cutlass.Media.Platform;

namespace Cutlass.Media.Export;

/// <summary>
/// Step 1 of the export flow (TECH_BRIEF 9): copies the bundled fonts and resolves every enabled title's font with the
/// <see cref="FontLocator"/>, so the compiler names them by their bare file names in the work directory. Every title
/// counts, not only those inside the range: widening a window across a transition (TECH_BRIEF 7.5) pulls in a
/// neighbouring title, as in <see cref="Preview.PreviewFontResolver"/>. Warnings name only the fonts of titles inside
/// the range. The export itself and the dialog's "Show ffmpeg command" share it, so the preview shows the fonts the
/// export will use.
/// </summary>
internal static class ExportFontResolver
{
    /// <summary>Resolves the title fonts of a request.</summary>
    /// <param name="request">The export request.</param>
    /// <param name="fonts">The font locator.</param>
    /// <param name="token">Cancels the resolution.</param>
    /// <returns>The catalog to compile against and the font warnings.</returns>
    public static async Task<(IMediaCatalog Catalog, IReadOnlyList<string> Warnings)> ResolveAsync(
        ExportRequest request,
        FontLocator fonts,
        CancellationToken token)
    {
        var titles = request.Project.Sequence.Tracks
            .SelectMany(t => t.Clips)
            .OfType<TitleClip>()
            .Where(c => c.Enabled)
            .ToList();
        var references = titles.Select(c => c.Title.Font).Distinct(StringComparer.Ordinal).ToList();
        var inRange = titles
            .Where(c => c.End > request.StartFrame && c.Start < request.EndFrame)
            .Select(c => c.Title.Font)
            .ToHashSet(StringComparer.Ordinal);
        if (references.Count == 0)
        {
            return (request.Catalog, []);
        }

        fonts.EnsureBundledFonts();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        foreach (var reference in references)
        {
            var resolved = await fonts.ResolveAsync(reference, token).ConfigureAwait(false);
            if (resolved is null)
            {
                if (inRange.Contains(reference))
                {
                    warnings.Add($"No font was found for '{reference}', not even a system fallback.");
                }

                continue;
            }

            map[reference] = resolved.BareName;
            if (resolved.IsFallback && inRange.Contains(reference))
            {
                warnings.Add($"The title font '{reference}' is not available; a system font is used.");
            }
        }

        return (new FontResolvingCatalog(request.Catalog, map), warnings);
    }
}
