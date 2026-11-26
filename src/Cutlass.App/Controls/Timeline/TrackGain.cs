using System.Globalization;
using Cutlass.Core.Editing;

namespace Cutlass.App.Controls.Timeline;

/// <summary>
/// The audio track header gain field (DESIGN 4.5: a dB field on audio tracks at least 48 px tall). Dragging it
/// horizontally changes the gain like a ScrubbyNumberBox (DESIGN 4.4): 0.1 dB per pixel, Shift 10x, Ctrl 0.1x.
/// Pure, so it is unit tested.
/// </summary>
public static class TrackGain
{
    /// <summary>The lowest gain (the model's limit).</summary>
    public const double MinDb = TimelineEdits.MinGainDb;

    /// <summary>The highest gain (the model's limit).</summary>
    public const double MaxDb = TimelineEdits.MaxGainDb;

    /// <summary>dB per pixel of drag.</summary>
    public const double DbPerPixel = 0.1;

    /// <summary>The field is shown only on audio tracks at least this tall.</summary>
    public const double MinTrackHeight = 48;

    /// <summary>The field's left edge in the header.</summary>
    public const double FieldX = 40;

    /// <summary>The field's top edge in the row.</summary>
    public const double FieldY = 27;

    /// <summary>The field width.</summary>
    public const double FieldWidth = 56;

    /// <summary>The field height.</summary>
    public const double FieldHeight = 16;

    /// <summary>Clamps a gain to <see cref="MinDb"/>..<see cref="MaxDb"/>, rounded to 0.1 dB.</summary>
    /// <param name="db">The gain.</param>
    /// <returns>The clamped gain.</returns>
    public static double Clamp(double db) =>
        double.IsFinite(db) ? Math.Round(Math.Clamp(db, MinDb, MaxDb), 1, MidpointRounding.AwayFromZero) : 0;

    /// <summary>The gain after dragging the field by <paramref name="dx"/> pixels.</summary>
    /// <param name="startDb">The gain at the press.</param>
    /// <param name="dx">The horizontal drag distance in pixels.</param>
    /// <param name="shift">Shift held: 10x steps.</param>
    /// <param name="ctrl">Ctrl held: 0.1x steps.</param>
    /// <returns>The clamped gain.</returns>
    public static double FromDrag(double startDb, double dx, bool shift, bool ctrl)
    {
        var step = DbPerPixel * (shift ? 10 : 1) * (ctrl ? 0.1 : 1);
        return Clamp(startDb + (dx * step));
    }

    /// <summary>Formats a gain for the header, for example <c>-3.5 dB</c>, <c>+2.0 dB</c> or <c>0.0 dB</c>.</summary>
    /// <param name="db">The gain.</param>
    /// <returns>The text.</returns>
    public static string Format(double db)
    {
        var value = Clamp(db);
        var text = Math.Abs(value).ToString("0.0", CultureInfo.InvariantCulture) + " dB";
        return value > 0 ? "+" + text : value < 0 ? "-" + text : text;
    }

    /// <summary>True when the field is shown on a row.</summary>
    /// <param name="isAudio">True for an audio track.</param>
    /// <param name="rowHeight">The row height.</param>
    /// <returns>True when the field is shown.</returns>
    public static bool HasField(bool isAudio, double rowHeight) => isAudio && rowHeight >= MinTrackHeight;

    /// <summary>True when a header point (x, y within the row) is on the field.</summary>
    /// <param name="x">The x in the header.</param>
    /// <param name="yInRow">The y from the row top.</param>
    /// <returns>True when the point is on the field.</returns>
    public static bool Contains(double x, double yInRow) =>
        x >= FieldX && x < FieldX + FieldWidth && yInRow >= FieldY && yInRow < FieldY + FieldHeight;
}
