using System.Globalization;
using Cutlass.Core.Time;

namespace Cutlass.App.Services;

/// <summary>
/// What the user may type into a click-to-type timecode field (DESIGN 4.3): an absolute timecode such as
/// <c>00:00:12:04</c> (shorter forms like <c>12:04</c> or a bare frame count also work, see
/// <see cref="Timecode.TryParse"/>), or a relative entry that starts with <c>+</c> or <c>-</c>: a frame count
/// (<c>+15</c>) or a timecode (<c>-00:00:01:00</c>). Digits are always invariant, whatever the UI culture.
/// </summary>
public static class TimecodeEntry
{
    /// <summary>Resolves an entry against the current frame.</summary>
    /// <param name="text">The typed text.</param>
    /// <param name="current">The current frame (the base of a relative entry).</param>
    /// <param name="rate">The rate of the timecode.</param>
    /// <param name="frame">The resolved frame, never negative; the current frame on failure.</param>
    /// <returns>True when the text is a valid entry.</returns>
    public static bool TryResolve(string? text, long current, FrameRate rate, out long frame)
    {
        frame = current;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim();
        if (t[0] is '+' or '-')
        {
            var body = t[1..].Trim();
            if (body.Length == 0 || body[0] is '+' or '-')
            {
                return false;
            }

            long offset;
            if (long.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out var frames))
            {
                offset = frames;
            }
            else if (!Timecode.TryParse(body, rate, out offset))
            {
                return false;
            }

            var target = t[0] == '+' ? (Int128)current + offset : (Int128)current - offset;
            frame = (long)Int128.Clamp(target, 0, long.MaxValue);
            return true;
        }

        if (!Timecode.TryParse(t, rate, out var absolute))
        {
            return false;
        }

        frame = Math.Max(0, absolute);
        return true;
    }
}
