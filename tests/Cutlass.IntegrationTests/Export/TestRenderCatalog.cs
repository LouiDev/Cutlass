using Cutlass.Compiler;

namespace Cutlass.IntegrationTests.Export;

/// <summary>An in-memory media catalog built from probed test media.</summary>
internal sealed class TestRenderCatalog : IMediaCatalog
{
    private readonly Dictionary<string, MediaRenderInfo> _items = new(StringComparer.Ordinal);

    /// <summary>Adds an entry.</summary>
    public TestRenderCatalog Add(MediaRenderInfo info)
    {
        _items[info.Asset.Id] = info;
        return this;
    }

    /// <inheritdoc />
    public MediaRenderInfo? Get(string mediaId) => _items.GetValueOrDefault(mediaId);
}
