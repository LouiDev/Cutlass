namespace Cutlass.Agent.Imaging;

/// <summary>
/// Tiles equally sized BGRA frames into one RGB image (AGENT_INTEGRATION 4.4): cells in reading order, a 2 px dark
/// grey gutter between them. The layout names each cell's top-left corner. Pure.
/// </summary>
public sealed class ContactSheetComposer
{
    /// <summary>The gap between cells in pixels.</summary>
    public const int Gutter = 2;

    private const byte GutterGrey = 0x20;

    private readonly byte[] _rgb;

    /// <summary>Creates an empty sheet.</summary>
    /// <param name="cellWidth">The cell width.</param>
    /// <param name="cellHeight">The cell height.</param>
    /// <param name="columns">The columns.</param>
    /// <param name="cells">The cell count.</param>
    public ContactSheetComposer(int cellWidth, int cellHeight, int columns, int cells)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cells);
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Columns = Math.Min(columns, cells);
        Rows = (cells + Columns - 1) / Columns;
        Width = SheetWidth(cellWidth, Columns);
        Height = SheetWidth(cellHeight, Rows);
        _rgb = new byte[Width * 3 * Height];
        _rgb.AsSpan().Fill(GutterGrey);
    }

    /// <summary>The cell width.</summary>
    public int CellWidth { get; }

    /// <summary>The cell height.</summary>
    public int CellHeight { get; }

    /// <summary>The columns.</summary>
    public int Columns { get; }

    /// <summary>The rows.</summary>
    public int Rows { get; }

    /// <summary>The sheet width.</summary>
    public int Width { get; }

    /// <summary>The sheet height.</summary>
    public int Height { get; }

    /// <summary>The sheet size along one axis for a cell size and count.</summary>
    /// <param name="cell">The cell size.</param>
    /// <param name="count">The cells along the axis.</param>
    /// <returns>The size in pixels.</returns>
    public static int SheetWidth(int cell, int count) => (cell * count) + (Gutter * Math.Max(0, count - 1));

    /// <summary>The near square column count for a cell count.</summary>
    /// <param name="cells">The cells.</param>
    /// <returns>The columns.</returns>
    public static int DefaultColumns(int cells) => Math.Max(1, (int)Math.Ceiling(Math.Sqrt(cells)));

    /// <summary>The top-left corner of a cell.</summary>
    /// <param name="index">The cell index in reading order.</param>
    /// <returns>The corner.</returns>
    public (int X, int Y) CellOrigin(int index) =>
        ((index % Columns) * (CellWidth + Gutter), (index / Columns) * (CellHeight + Gutter));

    /// <summary>Copies a BGRA frame of the cell size into a cell.</summary>
    /// <param name="index">The cell index.</param>
    /// <param name="bgra">The pixels.</param>
    /// <param name="stride">Bytes per row.</param>
    public void Draw(int index, ReadOnlySpan<byte> bgra, int stride)
    {
        var (ox, oy) = CellOrigin(index);
        for (var y = 0; y < CellHeight; y++)
        {
            var src = bgra.Slice(y * stride, CellWidth * 4);
            var dst = _rgb.AsSpan(((oy + y) * Width * 3) + (ox * 3), CellWidth * 3);
            for (int x = 0, s = 0, t = 0; x < CellWidth; x++, s += 4, t += 3)
            {
                dst[t] = src[s + 2];
                dst[t + 1] = src[s + 1];
                dst[t + 2] = src[s];
            }
        }
    }

    /// <summary>Encodes the sheet as a PNG.</summary>
    /// <returns>The PNG bytes.</returns>
    public byte[] ToPng() => PngEncoder.EncodeRgb(_rgb, Width, Height, Width * 3);
}
