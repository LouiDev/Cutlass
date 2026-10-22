using System.Globalization;
using System.Text;
using Cutlass.Agent.Lock;
using Cutlass.Agent.Tools;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Agent.Api;

/// <summary>
/// The compact describe text of AGENT_INTEGRATION 4.5: a header, a line per media and per track (top to bottom) and
/// the markers, at most 256 KB. Pure; any thread; invariant culture.
/// </summary>
public static class ProjectDescriber
{
    /// <summary>The longest describe text in characters.</summary>
    public const int MaxLength = 256 * 1024;

    /// <summary>Builds the text.</summary>
    /// <param name="project">The project.</param>
    /// <param name="path">The project file, or null.</param>
    /// <param name="dirty">Unsaved changes.</param>
    /// <param name="revision">The revision.</param>
    /// <param name="playhead">The user's playhead.</param>
    /// <param name="lockState">The lock state.</param>
    /// <param name="isOnline">Tells whether a media id's file is present.</param>
    /// <param name="from">Only clips ending after this frame, or null.</param>
    /// <param name="to">Only clips starting before this frame, or null.</param>
    /// <returns>The text, lines separated by <c>\n</c>.</returns>
    public static string Describe(
        Project project,
        string? path,
        bool dirty,
        long revision,
        long playhead,
        AgentLockState lockState,
        Func<string, bool> isOnline,
        long? from = null,
        long? to = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(lockState);
        ArgumentNullException.ThrowIfNull(isOnline);
        var c = CultureInfo.InvariantCulture;
        var s = project.Sequence.Settings;
        var rate = s.Rate;
        var duration = TimelineQuery.Duration(project);
        var sb = new StringBuilder();
        sb.Append(c, $"Project \"{ProjectSummaryMapper.NameOf(path)}\"{(dirty ? " (unsaved)" : string.Empty)} rev {revision} | ");
        sb.Append(c, $"{s.Width}x{s.Height} @ {rate.DisplayName} ({ProjectSummaryMapper.RateText(rate)}) | 48 kHz | {duration} frames ({Timecode.Format(duration, rate)})\n");
        var inText = project.Sequence.InFrame is { } i ? i.ToString(c) : "-";
        var outText = project.Sequence.OutFrame is { } o ? o.ToString(c) : "-";
        var lockText = lockState.IsHeld
            ? $"{lockState.AgentName} ({lockState.Reason}){(lockState.Phase == LockPhase.Paused ? " paused" : string.Empty)}"
            : lockState.Phase == LockPhase.Pending ? "pending " + lockState.PendingAgent : "free";
        sb.Append(c, $"In {inText} | Out {outText} | Playhead {playhead} | Lock: {lockText}\n");
        foreach (var asset in project.Media)
        {
            sb.Append("Media: ").Append(MediaLine(asset, isOnline(asset.Id))).Append('\n');
        }

        var lo = from ?? long.MinValue;
        var hi = to ?? long.MaxValue;
        foreach (var track in ProjectSummaryMapper.TopToBottom(project))
        {
            sb.Append(track.Name).Append(' ').Append(track.Id);
            if (track.Kind == TrackKind.Audio)
            {
                sb.Append(c, $" {Num(track.GainDb)} dB");
                if (track.Muted)
                {
                    sb.Append(" muted");
                }

                if (track.Solo)
                {
                    sb.Append(" solo");
                }
            }
            else if (track.Hidden)
            {
                sb.Append(" hidden");
            }

            if (track.Locked)
            {
                sb.Append(" locked");
            }

            sb.Append(':');
            var items = new List<string>();
            foreach (var clip in track.Clips)
            {
                if (clip.End <= lo || clip.Start >= hi)
                {
                    continue;
                }

                items.Add(ClipText(project, clip, isOnline));
                foreach (var t in track.Transitions.Where(t => t.LeftClipId == clip.Id))
                {
                    items.Add(string.Create(c, $"{t.Id} {EnumWire.Name(t.Kind)} {t.Duration} @{TimelineQuery.CutFrame(track, t)}"));
                }
            }

            sb.Append(items.Count == 0 ? " (empty)" : " " + string.Join(" | ", items)).Append('\n');
            if (sb.Length > MaxLength)
            {
                break;
            }
        }

        var markers = project.Sequence.Markers.Where(m => m.Frame >= lo && m.Frame < hi).ToList();
        if (markers.Count > 0)
        {
            sb.Append("Markers: ").Append(string.Join(" | ", markers.Select(m => string.Create(c, $"{m.Id} {m.Frame} \"{m.Name}\"")))).Append('\n');
        }

        if (sb.Length > MaxLength)
        {
            const string tail = "\n... (truncated; use get_project or describe with from and to)\n";
            sb.Length = MaxLength - tail.Length;
            sb.Append(tail);
        }

        return sb.ToString();
    }

    /// <summary>
    /// A short track overview for the agent briefing (MCP_HOSTING_CONCEPT 5.1): one line per track, top to bottom,
    /// with its clip and transition counts and its muted, hidden or locked state, for example
    /// <c>V1: 2 clips, 1 transition</c>. When the project has more tracks than <paramref name="maxLines"/>, the last
    /// line counts the rest (<c>... 3 more tracks</c>). Pure; any thread; invariant culture.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="maxLines">The most lines, at least 1.</param>
    /// <returns>The lines separated by <c>\n</c>, or "no tracks".</returns>
    public static string Summary(Project project, int maxLines)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);
        var c = CultureInfo.InvariantCulture;
        var tracks = ProjectSummaryMapper.TopToBottom(project).ToList();
        if (tracks.Count == 0)
        {
            return "no tracks";
        }

        var shown = tracks.Count <= maxLines ? tracks.Count : maxLines - 1;
        var lines = new List<string>(maxLines);
        foreach (var track in tracks.Take(shown))
        {
            var parts = new List<string>
            {
                track.Clips.Length switch
                {
                    0 => "empty",
                    1 => "1 clip",
                    var n => string.Create(c, $"{n} clips"),
                },
            };
            if (track.Transitions.Length > 0)
            {
                parts.Add(track.Transitions.Length == 1 ? "1 transition" : string.Create(c, $"{track.Transitions.Length} transitions"));
            }

            if (track.Kind == TrackKind.Audio && track.Muted)
            {
                parts.Add("muted");
            }

            if (track.Kind == TrackKind.Video && track.Hidden)
            {
                parts.Add("hidden");
            }

            if (track.Locked)
            {
                parts.Add("locked");
            }

            lines.Add(track.Name + ": " + string.Join(", ", parts));
        }

        if (shown < tracks.Count)
        {
            var rest = tracks.Count - shown;
            lines.Add(rest == 1 ? "... 1 more track" : string.Create(c, $"... {rest} more tracks"));
        }

        return string.Join('\n', lines);
    }

    private static string MediaLine(MediaAsset asset, bool online)
    {
        var c = CultureInfo.InvariantCulture;
        var dto = ProjectSummaryMapper.MapMedia(asset, online);
        var parts = new List<string> { string.Create(c, $"{asset.Id} {dto.FileName} {Num(dto.DurationSeconds)} s") };
        if (asset.Info.Video is { } v)
        {
            var fps = v.AvgFrameRate.IsPositive ? v.AvgFrameRate.ToDouble() : v.RFrameRate.ToDouble();
            parts.Add(asset.Info.IsStill
                ? string.Create(c, $"{v.Codec} {v.DisplayWidth}x{v.DisplayHeight} still")
                : string.Create(c, $"{v.Codec} {v.DisplayWidth}x{v.DisplayHeight} {Num(Math.Round(fps, 3))}"));
        }

        if (asset.Info.Audio is { } a)
        {
            parts.Add(string.Create(c, $"{a.Codec} {a.SampleRate / 1000}k {a.Channels}ch"));
        }

        return string.Join(", ", parts) + (online ? string.Empty : " OFFLINE");
    }

    private static string ClipText(Project project, Clip clip, Func<string, bool> isOnline)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        switch (clip)
        {
            case TitleClip t:
                var text = t.Title.Text.Replace("\n", "\\n", StringComparison.Ordinal);
                if (text.Length > 60)
                {
                    text = text[..57] + "...";
                }

                sb.Append(c, $"{clip.Id} title \"{text}\" {clip.Start}+{clip.Duration}");
                AppendFades(sb, t.FadeInFrames, t.FadeOutFrames);
                break;
            case MediaClip m:
                var rate = project.Sequence.Settings.Rate;
                var inS = (double)m.SourceInFlicks / TimeMath.FlicksPerSecond;
                var outS = (double)ProjectSummaryMapper.SourceOutFlicks(m, rate) / TimeMath.FlicksPerSecond;
                sb.Append(c, $"{clip.Id} {TimelineQuery.DisplayName(project, clip)} {clip.Start}+{clip.Duration} src {inS:0.000}-{outS:0.000} s");
                if (m.SpeedPercent != 100)
                {
                    sb.Append(c, $" speed {m.SpeedPercent}%");
                }

                if (m.LinkId is { } link)
                {
                    sb.Append(" link ").Append(link);
                }

                if (m.Stream == StreamKind.Audio)
                {
                    if (m.Audio.GainDb != 0)
                    {
                        sb.Append(c, $" gain {Num(m.Audio.GainDb)} dB");
                    }

                    AppendFades(sb, m.Audio.FadeInFrames, m.Audio.FadeOutFrames);
                    if (!m.Audio.Keys.IsDefaultOrEmpty)
                    {
                        sb.Append(c, $" keys {m.Audio.Keys.Length}");
                    }
                }
                else
                {
                    if (m.Video.Opacity < 1)
                    {
                        sb.Append(c, $" opacity {Num(m.Video.Opacity)}");
                    }

                    AppendFades(sb, m.Video.FadeInFrames, m.Video.FadeOutFrames);
                }

                if (!isOnline(m.MediaId))
                {
                    sb.Append(" OFFLINE");
                }

                break;
            default:
                sb.Append(c, $"{clip.Id} {clip.Start}+{clip.Duration}");
                break;
        }

        if (!clip.Enabled)
        {
            sb.Append(" disabled");
        }

        return sb.ToString();
    }

    private static void AppendFades(StringBuilder sb, long fadeIn, long fadeOut)
    {
        if (fadeIn != 0 || fadeOut != 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $" fade {fadeIn}/{fadeOut}");
        }
    }

    private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
