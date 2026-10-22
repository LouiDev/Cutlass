using Cutlass.Compiler;
using Cutlass.Core.Model;
using Cutlass.Media.Export;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;

namespace Cutlass.Media.Preview;

/// <summary>
/// Resolves the title fonts of a project the way <see cref="ExportService"/> does, memoised per font reference, and
/// wraps the catalog so the compiler names them by bare file name. The frame server and playback both use it, so they
/// compile the same graph text (and therefore the same <see cref="FrameCache"/> keys). Not thread-safe: each owner
/// calls it from one worker at a time.
/// </summary>
internal sealed class PreviewFontResolver
{
    private readonly IMediaCatalog _catalog;
    private readonly FontLocator _fonts;
    private readonly WorkDirectory _work;
    private readonly Dictionary<string, FontResolution?> _memo = new(StringComparer.Ordinal);

    /// <summary>Creates the resolver.</summary>
    /// <param name="catalog">The media catalog to wrap.</param>
    /// <param name="fonts">Places title fonts into the work directory.</param>
    /// <param name="work">The work directory.</param>
    public PreviewFontResolver(IMediaCatalog catalog, FontLocator fonts, WorkDirectory work)
    {
        _catalog = catalog;
        _fonts = fonts;
        _work = work;
    }

    /// <summary>
    /// Resolves every enabled title's font. Every title counts, not only those in a window, because a window can pull
    /// in neighbours of its run (TECH_BRIEF 7.5).
    /// </summary>
    /// <param name="project">The snapshot.</param>
    /// <param name="token">Cancels the resolution.</param>
    /// <returns>The catalog to compile with.</returns>
    public async Task<IMediaCatalog> ResolveAsync(Project project, CancellationToken token)
    {
        var references = project.Sequence.Tracks
            .SelectMany(t => t.Clips)
            .OfType<TitleClip>()
            .Where(c => c.Enabled)
            .Select(c => c.Title.Font)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (references.Count == 0)
        {
            return _catalog;
        }

        _fonts.EnsureBundledFonts();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            if (!_memo.TryGetValue(reference, out var resolved)
                || (resolved is not null && !File.Exists(_work.GetFullPath(resolved.BareName))))
            {
                try
                {
                    resolved = await _fonts.ResolveAsync(reference, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Remembered like any other failure, so a bad font path is not retried for every frame; the
                    // compiler then falls back to Inter-Regular with a warning.
                    resolved = null;
                }

                _memo[reference] = resolved;
            }

            if (resolved is not null)
            {
                map[reference] = resolved.BareName;
            }
        }

        return new FontResolvingCatalog(_catalog, map);
    }
}
