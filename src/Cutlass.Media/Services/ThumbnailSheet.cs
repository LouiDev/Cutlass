using System.Globalization;

namespace Cutlass.Media.Services;

/// <summary>
/// The thumbnail sprites of one media item (TECH_BRIEF 6.5): <see cref="Count"/> tiles of 160x90, thumbnail
/// <c>i</c> showing source time <c>i * interval</c>, laid out row by row on 10x10 JPEG sheets
/// <c>sheet_001.jpg</c>, <c>sheet_002.jpg</c> and so on. One sprite set serves the timeline filmstrip, the media bin
/// hover skim and the viewer placeholder; lookups clamp to the first and last thumbnail. Immutable.
/// </summary>
/// <param name="Directory">The folder holding the sheets (the cache's <c>thumbs/</c>).</param>
/// <param name="IntervalMicros">Source time between two thumbnails.</param>
/// <param name="Count">The number of thumbnails (at least 1).</param>
public sealed record ThumbnailSheet(string Directory, long IntervalMicros, int Count)
{
    /// <summary>Tile width in pixels.</summary>
    public const int TileWidth = 160;

    /// <summary>Tile height in pixels.</summary>
    public const int TileHeight = 90;

    /// <summary>Tiles per sheet row.</summary>
    public const int Columns = 10;

    /// <summary>Tile rows per sheet.</summary>
    public const int Rows = 10;

    /// <summary>Tiles per sheet.</summary>
    public const int TilesPerSheet = Columns * Rows;

    /// <summary>Sheet width in pixels (1600).</summary>
    public const int SheetWidth = TileWidth * Columns;

    /// <summary>Sheet height in pixels (900).</summary>
    public const int SheetHeight = TileHeight * Rows;

    /// <summary>The number of sheets.</summary>
    public int SheetCount => (Count + TilesPerSheet - 1) / TilesPerSheet;

    /// <summary>Every sheet path, in order.</summary>
    public IReadOnlyList<string> SheetPaths => [.. Enumerable.Range(0, SheetCount).Select(SheetPath)];

    /// <summary>The file name of a sheet: <c>sheet_001.jpg</c> for sheet 0 (ffmpeg's <c>sheet_%03d.jpg</c> counts from 1).</summary>
    /// <param name="sheet">The zero-based sheet number.</param>
    /// <returns>The file name.</returns>
    public static string SheetFileName(int sheet)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sheet);
        return string.Create(CultureInfo.InvariantCulture, $"sheet_{sheet + 1:D3}.jpg");
    }

    /// <summary>The full path of a sheet.</summary>
    /// <param name="sheet">The zero-based sheet number.</param>
    /// <returns>The path.</returns>
    public string SheetPath(int sheet) => Path.Combine(Directory, SheetFileName(sheet));

    /// <summary>The thumbnail index for a source time: <c>floor(t / interval)</c>, clamped.</summary>
    /// <param name="sourceMicros">The source time from the file's start, in microseconds.</param>
    /// <returns>The index from 0 to <c>Count - 1</c>.</returns>
    public int IndexAt(long sourceMicros)
    {
        if (sourceMicros <= 0 || IntervalMicros <= 0)
        {
            return 0;
        }

        return (int)Math.Min(Count - 1, sourceMicros / IntervalMicros);
    }

    /// <summary>The source time a thumbnail shows.</summary>
    /// <param name="index">The thumbnail index.</param>
    /// <returns>The source time in microseconds.</returns>
    public long SourceMicrosOf(int index) => (long)Math.Clamp(index, 0, Count - 1) * IntervalMicros;

    /// <summary>Where a thumbnail sits: its sheet file and the tile's pixel rectangle on it.</summary>
    /// <param name="index">The thumbnail index (clamped).</param>
    /// <returns>The sheet path and the tile rectangle.</returns>
    public (string SheetPath, int X, int Y, int Width, int Height) Tile(int index)
    {
        var i = Math.Clamp(index, 0, Count - 1);
        var sheet = i / TilesPerSheet;
        var cell = i % TilesPerSheet;
        return (SheetPath(sheet), (cell % Columns) * TileWidth, (cell / Columns) * TileHeight, TileWidth, TileHeight);
    }

    /// <summary>The tile for a source time (filmstrip tiles and the viewer placeholder).</summary>
    /// <param name="sourceMicros">The source time from the file's start, in microseconds.</param>
    /// <returns>The sheet path and the tile rectangle.</returns>
    public (string SheetPath, int X, int Y, int Width, int Height) TileAt(long sourceMicros) => Tile(IndexAt(sourceMicros));

    /// <summary>The tile for a horizontal position across a media bin card (hover skim, DESIGN 4.2).</summary>
    /// <param name="fraction">0 at the card's left edge, 1 at its right edge (clamped).</param>
    /// <returns>The sheet path and the tile rectangle.</returns>
    public (string SheetPath, int X, int Y, int Width, int Height) SkimTile(double fraction)
    {
        var f = double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1);
        return Tile((int)Math.Min(Count - 1, Math.Floor(f * Count)));
    }
}
