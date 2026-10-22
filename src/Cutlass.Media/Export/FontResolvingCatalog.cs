using Cutlass.Compiler;

namespace Cutlass.Media.Export;

/// <summary>
/// Wraps a media catalog with the title fonts the <c>FontLocator</c> already placed in the work directory, so the
/// pure compiler can name them by their bare file names.
/// </summary>
internal sealed class FontResolvingCatalog : IMediaCatalog
{
    private readonly IMediaCatalog _inner;
    private readonly IReadOnlyDictionary<string, string> _fonts;

    /// <summary>Creates the wrapper.</summary>
    /// <param name="inner">The media catalog.</param>
    /// <param name="fonts">Font reference to bare name in the work directory.</param>
    public FontResolvingCatalog(IMediaCatalog inner, IReadOnlyDictionary<string, string> fonts)
    {
        _inner = inner;
        _fonts = fonts;
    }

    /// <inheritdoc />
    public MediaRenderInfo? Get(string mediaId) => _inner.Get(mediaId);

    /// <inheritdoc />
    public string? ResolveFont(string fontReference) =>
        _fonts.TryGetValue(fontReference, out var bare) ? bare : _inner.ResolveFont(fontReference);
}
