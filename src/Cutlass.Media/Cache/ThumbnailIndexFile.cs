namespace Cutlass.Media.Cache;

/// <summary>
/// The <c>thumbs/thumbs.json</c> file (TECH_BRIEF 6.1): <c>{intervalSec, tileW:160, tileH:90, cols:10, rows:10, count}</c>.
/// </summary>
internal sealed record ThumbnailIndexFile
{
    /// <summary>Seconds between two thumbnails.</summary>
    public double IntervalSec { get; set; }

    /// <summary>Tile width in pixels.</summary>
    public int TileW { get; set; }

    /// <summary>Tile height in pixels.</summary>
    public int TileH { get; set; }

    /// <summary>Tiles per sheet row.</summary>
    public int Cols { get; set; }

    /// <summary>Tile rows per sheet.</summary>
    public int Rows { get; set; }

    /// <summary>The number of thumbnails.</summary>
    public int Count { get; set; }
}
