namespace Cutlass.IntegrationTests.Infrastructure;

/// <summary>
/// Reads the 16-bit value of a bit-block oracle frame (TECH_BRIEF 11.2): 8 columns of 40 px and 2 rows of 90 px,
/// bit <c>col + 8*row</c>, read by thresholding the centre pixel of each block at 128.
/// </summary>
public static class OracleReader
{
    private const int Columns = 8;
    private const int Rows = 2;
    private const int Threshold = 128;

    /// <summary>Decodes an 8-bit gray frame.</summary>
    /// <param name="gray">The pixels, <paramref name="stride"/> bytes per row.</param>
    /// <param name="width">Frame width (normally 320).</param>
    /// <param name="height">Frame height (normally 180).</param>
    /// <param name="stride">Bytes per row; 0 means <paramref name="width"/>.</param>
    /// <returns>The oracle value.</returns>
    public static int DecodeGray(ReadOnlySpan<byte> gray, int width, int height, int stride = 0) =>
        Decode(gray, width, height, stride == 0 ? width : stride, bytesPerPixel: 1, channel: 0);

    /// <summary>Decodes a BGRA frame using the green channel.</summary>
    /// <param name="bgra">The pixels, <paramref name="stride"/> bytes per row.</param>
    /// <param name="width">Frame width (normally 320).</param>
    /// <param name="height">Frame height (normally 180).</param>
    /// <param name="stride">Bytes per row; 0 means <c>width * 4</c>.</param>
    /// <returns>The oracle value.</returns>
    public static int DecodeBgra(ReadOnlySpan<byte> bgra, int width, int height, int stride = 0) =>
        Decode(bgra, width, height, stride == 0 ? width * 4 : stride, bytesPerPixel: 4, channel: 1);

    private static int Decode(ReadOnlySpan<byte> pixels, int width, int height, int stride, int bytesPerPixel, int channel)
    {
        var blockW = width / Columns;
        var blockH = height / Rows;
        var value = 0;
        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Columns; col++)
            {
                var x = (col * blockW) + (blockW / 2);
                var y = (row * blockH) + (blockH / 2);
                if (pixels[(y * stride) + (x * bytesPerPixel) + channel] >= Threshold)
                {
                    value |= 1 << (col + (Columns * row));
                }
            }
        }

        return value;
    }
}
