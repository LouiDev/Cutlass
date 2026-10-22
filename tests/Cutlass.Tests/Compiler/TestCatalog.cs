using Cutlass.Compiler;
using Cutlass.Core.Model;

namespace Cutlass.Tests.Compiler;

/// <summary>An in-memory <see cref="IMediaCatalog"/> for compiler tests.</summary>
internal sealed class TestCatalog : IMediaCatalog
{
    private readonly Dictionary<string, MediaRenderInfo> _items = new(StringComparer.Ordinal);

    /// <summary>Fonts the catalog resolves (reference to bare name).</summary>
    public Dictionary<string, string> Fonts { get; } = new(StringComparer.Ordinal);

    /// <summary>Creates a catalog with every asset of the project as an original without index or proxy.</summary>
    public static TestCatalog For(Project project, Func<MediaAsset, FrameIndex?>? index = null)
    {
        var catalog = new TestCatalog();
        foreach (var asset in project.Media)
        {
            catalog.Add(new MediaRenderInfo(asset, new RenderSource(asset.Path, asset.Info.FormatStartMicros, index?.Invoke(asset)), null));
        }

        return catalog;
    }

    /// <summary>Adds or replaces an entry.</summary>
    public TestCatalog Add(MediaRenderInfo info)
    {
        _items[info.Asset.Id] = info;
        return this;
    }

    /// <summary>Removes an entry (the media goes offline).</summary>
    public TestCatalog Remove(string mediaId)
    {
        _items.Remove(mediaId);
        return this;
    }

    /// <inheritdoc />
    public MediaRenderInfo? Get(string mediaId) => _items.GetValueOrDefault(mediaId);

    /// <inheritdoc />
    public string? ResolveFont(string fontReference) => Fonts.GetValueOrDefault(fontReference);
}
