using System.Globalization;

namespace Cutlass.Core.Time;

/// <summary>
/// Non-drop <c>HH:MM:SS:FF</c> timecode (DESIGN 5.2; drop-frame is out of scope for v1). The frame field counts
/// <see cref="FrameRate.TimecodeBase"/> frames per second, so 29.97 counts 30 and 23.976 counts 24.
/// </summary>
public static class Timecode
{
    /// <summary>
    /// Formats a frame number as <c>HH:MM:SS:FF</c> with invariant digits. Hours grow beyond two digits when needed and
    /// negative frames get a leading <c>-</c>.
    /// </summary>
    /// <param name="frame">The frame number.</param>
    /// <param name="r">The rate whose <see cref="FrameRate.TimecodeBase"/> is used.</param>
    /// <returns>The timecode text, for example <c>"00:01:02:03"</c>.</returns>
    public static string Format(long frame, FrameRate r)
    {
        long fb = r.TimecodeBase;
        var negative = frame < 0;
        var abs = negative ? -(Int128)frame : frame;
        var ff = (long)(abs % fb);
        var totalSeconds = (long)(abs / fb);
        var ss = totalSeconds % 60;
        var mm = totalSeconds / 60 % 60;
        var hh = totalSeconds / 3600;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(negative ? "-" : string.Empty)}{hh:00}:{mm:00}:{ss:00}:{ff:00}");
    }

    /// <summary>
    /// Parses a timecode typed by the user. Accepts one to four fields separated by <c>:</c>, <c>;</c> or <c>.</c>,
    /// aligned from the right: <c>FF</c>, <c>SS:FF</c>, <c>MM:SS:FF</c> or <c>HH:MM:SS:FF</c>, with an optional
    /// leading <c>-</c>. Every field except the leftmost must be in range (frames below the timecode base, seconds and
    /// minutes below 60). The leftmost field may be any size, so <c>"150"</c> is 150 frames.
    /// </summary>
    /// <param name="s">The text.</param>
    /// <param name="r">The rate whose <see cref="FrameRate.TimecodeBase"/> is used.</param>
    /// <param name="frame">The parsed frame number, or 0 on failure.</param>
    /// <returns>True when the text is a valid timecode.</returns>
    public static bool TryParse(string s, FrameRate r, out long frame)
    {
        frame = 0;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        var text = s.Trim();
        var negative = false;
        if (text.StartsWith('-'))
        {
            negative = true;
            text = text[1..];
        }

        var parts = text.Split([':', ';', '.']);
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        // Fields from the right: frames, seconds, minutes, hours. Multipliers in frames.
        long fb = r.TimecodeBase;
        long[] limits = [fb, 60, 60, long.MaxValue];
        long[] multipliers = [1, fb, fb * 60, fb * 3600];
        Int128 total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var field = parts[parts.Length - 1 - i];
            if (field.Length is 0 or > 12
                || !long.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            var isLeftmost = i == parts.Length - 1;
            if (!isLeftmost && value >= limits[i])
            {
                return false;
            }

            total += (Int128)value * multipliers[i];
        }

        if (total > long.MaxValue)
        {
            return false;
        }

        frame = negative ? -(long)total : (long)total;
        return true;
    }
}
