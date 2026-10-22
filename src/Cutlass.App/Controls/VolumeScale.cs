using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.Controls;

/// <summary>
/// The volume line of audio clips (DESIGN 5.5): a linear dB scale from -60 dB at the bottom of the clip to +12 dB at
/// the top, so 0 dB sits at 83% of the clip height. The line shows the clip gain plus the keyframe envelope. Keys are
/// drawn at the centre of their frame. Pure, unit tested.
/// </summary>
public static class VolumeScale
{
    /// <summary>The bottom of the scale in dB.</summary>
    public const double MinDb = TimelineEdits.MinGainDb;

    /// <summary>The top of the scale in dB.</summary>
    public const double MaxDb = TimelineEdits.MaxGainDb;

    /// <summary>The keyframe dot radius in pixels.</summary>
    public const double DotRadius = 4;

    /// <summary>The grab distance of the volume line in pixels.</summary>
    public const double LineGrab = 4;

    /// <summary>The y of a level inside a clip rectangle.</summary>
    /// <param name="db">The level in dB (clamped to the scale).</param>
    /// <param name="top">The clip rectangle's top.</param>
    /// <param name="height">The clip rectangle's height.</param>
    /// <returns>The y.</returns>
    public static double DbToY(double db, double top, double height)
    {
        var fraction = (Math.Clamp(db, MinDb, MaxDb) - MinDb) / (MaxDb - MinDb);
        return top + (height * (1 - fraction));
    }

    /// <summary>The dB change of a vertical pointer movement (up is louder).</summary>
    /// <param name="dy">The movement in pixels (positive is down).</param>
    /// <param name="height">The clip rectangle's height.</param>
    /// <returns>The change in dB.</returns>
    public static double DbPerPixels(double dy, double height) => height <= 0 ? 0 : -dy * (MaxDb - MinDb) / height;

    /// <summary>
    /// The level of the volume line at a fractional clip-relative frame: the clip gain plus the keyframe envelope,
    /// interpolated linearly between key centres and constant outside them.
    /// </summary>
    /// <param name="clip">The audio clip.</param>
    /// <param name="frame">The frame relative to the clip start (fractional).</param>
    /// <returns>The level in dB.</returns>
    public static double LevelAt(MediaClip clip, double frame)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var keys = clip.Audio.Keys;
        if (keys.IsDefaultOrEmpty)
        {
            return clip.Audio.GainDb;
        }

        double env;
        if (frame <= keys[0].Frame + 0.5)
        {
            env = keys[0].Db;
        }
        else if (frame >= keys[^1].Frame + 0.5)
        {
            env = keys[^1].Db;
        }
        else
        {
            env = keys[^1].Db;
            for (var i = 1; i < keys.Length; i++)
            {
                var x1 = keys[i].Frame + 0.5;
                if (frame <= x1)
                {
                    var x0 = keys[i - 1].Frame + 0.5;
                    var t = (frame - x0) / (x1 - x0);
                    env = keys[i - 1].Db + ((keys[i].Db - keys[i - 1].Db) * t);
                    break;
                }
            }
        }

        return clip.Audio.GainDb + env;
    }

    /// <summary>
    /// The keys a volume line drag at a clip-relative frame moves (DESIGN 5.5): the two keys around the segment, the
    /// single key before the first or after the last, or none when the clip has no keys (the clip gain changes).
    /// </summary>
    /// <param name="clip">The audio clip.</param>
    /// <param name="frame">The pressed frame relative to the clip start.</param>
    /// <returns>The key indices.</returns>
    public static IReadOnlyList<int> SegmentKeys(MediaClip clip, double frame)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var keys = clip.Audio.Keys;
        if (keys.IsDefaultOrEmpty)
        {
            return [];
        }

        if (frame < keys[0].Frame + 0.5)
        {
            return [0];
        }

        for (var i = 1; i < keys.Length; i++)
        {
            if (frame < keys[i].Frame + 0.5)
            {
                return [i - 1, i];
            }
        }

        return [keys.Length - 1];
    }

    /// <summary>A dB value for tooltips, for example <c>-6.0 dB</c> or <c>+3.5 dB</c>.</summary>
    /// <param name="db">The level.</param>
    /// <returns>The text.</returns>
    public static string Format(double db) =>
        (db > 0.05 ? "+" : string.Empty) + db.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " dB";
}
