using System.Collections;

namespace Cutlass.Media.Settings;

/// <summary>
/// An immutable list of folder paths with value equality, so <see cref="AppSettings"/> (a record) stays comparable by
/// value when it holds folder lists (the settings round trip and the Settings dialog compare copies).
/// </summary>
public sealed class FolderList : IReadOnlyList<string>, IEquatable<FolderList>
{
    private readonly string[] _items;

    private FolderList(string[] items)
    {
        _items = items;
    }

    /// <summary>The empty list.</summary>
    public static FolderList Empty { get; } = new([]);

    /// <inheritdoc />
    public int Count => _items.Length;

    /// <inheritdoc />
    public string this[int index] => _items[index];

    /// <summary>Copies a sequence (null gives the empty list); an existing <see cref="FolderList"/> is reused.</summary>
    /// <param name="items">The folders.</param>
    /// <returns>The list.</returns>
    public static FolderList From(IEnumerable<string>? items) => items switch
    {
        null => Empty,
        FolderList list => list,
        _ => new FolderList([.. items]),
    };

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_items).GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    public bool Equals(FolderList? other) => other is not null && _items.AsSpan().SequenceEqual(other._items);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FolderList other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var item in _items)
        {
            hash.Add(item, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() => "[" + string.Join(", ", _items) + "]";
}
