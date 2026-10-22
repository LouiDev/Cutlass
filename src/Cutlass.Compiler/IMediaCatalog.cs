namespace Cutlass.Compiler;

/// <summary>
/// Read-only access to the media the compiler renders from (TECH_BRIEF 7.1). Implementations must return the same
/// answers for the duration of one compile.
/// </summary>
public interface IMediaCatalog
{
    /// <summary>Looks up a media asset.</summary>
    /// <param name="mediaId">The media id.</param>
    /// <returns>The render info, or null when the media is unknown or offline.</returns>
    MediaRenderInfo? Get(string mediaId);

    /// <summary>
    /// Resolves a title font reference (<c>builtin:Inter-Bold</c> or a font file path) to the bare name of a font file
    /// in the work directory. The compiler has no I/O, so user fonts must be imported by the caller first.
    /// </summary>
    /// <remarks>
    /// User fonts are named <c>font_&lt;sha8&gt;.&lt;ext&gt;</c> by content, which only the Media layer can compute, so
    /// it answers here (TECH_BRIEF 7.1 and 9). The default answers nothing, and the compiler then maps
    /// <c>builtin:</c> references to the bundled Inter files itself and anything else to <c>Inter-Regular.ttf</c>
    /// with a warning.
    /// </remarks>
    /// <param name="fontReference">The reference from the title spec.</param>
    /// <returns>The bare file name, or null when this catalog does not know the font.</returns>
    string? ResolveFont(string fontReference) => null;
}
