using System.Globalization;
using Cutlass.Agent.Api.Dto.Media;
using Cutlass.Agent.Api.Dto.Project;
using Cutlass.Agent.Api.Requests.Project;
using Cutlass.Agent.Tools.Dispatch;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Agent.Api;

/// <summary>Maps a project to the summary of AGENT_INTEGRATION 4.5. Pure; any thread.</summary>
public static class ProjectSummaryMapper
{
    /// <summary>Builds the summary.</summary>
    /// <param name="project">The project.</param>
    /// <param name="path">The project file, or null while untitled.</param>
    /// <param name="dirty">Unsaved changes.</param>
    /// <param name="revision">The revision.</param>
    /// <param name="playhead">The user's playhead.</param>
    /// <param name="isOnline">Tells whether a media id's file is present.</param>
    /// <param name="filter">Track and frame filters, or null.</param>
    /// <returns>The summary.</returns>
    public static ProjectSummaryDto Map(
        Project project,
        string? path,
        bool dirty,
        long revision,
        long playhead,
        Func<string, bool> isOnline,
        ProjectQueryRequest? filter)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(isOnline);
        var settings = project.Sequence.Settings;
        var dto = new ProjectSummaryDto
        {
            Revision = revision,
            Path = path?.Replace('\\', '/'),
            Name = NameOf(path),
            Dirty = dirty,
            Sequence = new SequenceSummaryDto
            {
                Width = settings.Width,
                Height = settings.Height,
                FrameRate = RateText(settings.Rate),
                Fps = Math.Round(settings.Rate.Value.ToDouble(), 3),
                SampleRate = TimeMath.SampleRate,
                DurationFrames = TimelineQuery.Duration(project),
                InFrame = project.Sequence.InFrame,
                OutFrame = project.Sequence.OutFrame,
                Playhead = playhead,
            },
        };

        var from = filter?.From ?? long.MinValue;
        var to = filter?.To ?? long.MaxValue;
        foreach (var track in TopToBottom(project))
        {
            if (filter?.TrackIds is { Count: > 0 } only && !only.Contains(track.Id, StringComparer.Ordinal))
            {
                continue;
            }

            var t = new TrackSummaryDto
            {
                Id = track.Id,
                Kind = EnumWire.Name(track.Kind),
                Name = track.Name,
                Index = TimelineQuery.KindIndex(project, track.Id) + 1,
                Locked = track.Locked,
                Targeted = track.Targeted,
            };
            if (track.Kind == TrackKind.Video)
            {
                t.Hidden = track.Hidden;
            }
            else
            {
                t.Muted = track.Muted;
                t.Solo = track.Solo;
                t.GainDb = track.GainDb;
            }

            var included = new HashSet<string>(StringComparer.Ordinal);
            foreach (var clip in track.Clips)
            {
                if (clip.End <= from || clip.Start >= to)
                {
                    continue;
                }

                included.Add(clip.Id);
                t.Clips.Add(MapClip(project, clip, isOnline));
            }

            foreach (var transition in track.Transitions)
            {
                if (included.Contains(transition.LeftClipId) || included.Contains(transition.RightClipId))
                {
                    t.Transitions.Add(new TransitionSummaryDto
                    {
                        Id = transition.Id,
                        Kind = EnumWire.Name(transition.Kind),
                        LeftClipId = transition.LeftClipId,
                        RightClipId = transition.RightClipId,
                        DurationFrames = transition.Duration,
                        CutFrame = TimelineQuery.CutFrame(track, transition),
                    });
                }
            }

            dto.Tracks.Add(t);
        }

        foreach (var marker in project.Sequence.Markers)
        {
            if (marker.Frame >= from && marker.Frame < to)
            {
                dto.Markers.Add(new MarkerSummaryDto { Id = marker.Id, Frame = marker.Frame, Name = marker.Name, Color = marker.Color });
            }
        }

        foreach (var asset in project.Media)
        {
            dto.Media.Add(MapMedia(asset, isOnline(asset.Id)));
        }

        return dto;
    }

    /// <summary>Maps one media asset.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="online">True when its file is present.</param>
    /// <returns>The DTO.</returns>
    public static MediaDto MapMedia(MediaAsset asset, bool online)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var info = asset.Info;
        var path = asset.Path.Replace('\\', '/');
        var slash = path.LastIndexOf('/');
        return new MediaDto
        {
            Id = asset.Id,
            Path = path,
            FileName = slash >= 0 ? path[(slash + 1)..] : path,
            Online = online,
            DurationSeconds = Math.Round(info.DurationMicros / 1_000_000.0, 3),
            Still = info.IsStill,
            FormatName = info.FormatName,
            Video = info.Video is { } v
                ? new MediaVideoDto
                {
                    Codec = v.Codec,
                    Width = v.Width,
                    Height = v.Height,
                    DisplayWidth = v.DisplayWidth,
                    DisplayHeight = v.DisplayHeight,
                    Rotation = v.Rotation,
                    FrameRate = v.AvgFrameRate.IsPositive ? RatioText(v.AvgFrameRate) : RatioText(v.RFrameRate),
                    Vfr = v.IsVfr,
                    Hdr = v.IsHdr,
                }
                : null,
            Audio = info.Audio is { } a ? new MediaAudioDto { Codec = a.Codec, SampleRate = a.SampleRate, Channels = a.Channels } : null,
        };
    }

    /// <summary>The tracks top to bottom: video tracks from the highest down to V1, then audio tracks from A1 down.</summary>
    /// <param name="project">The project.</param>
    /// <returns>The tracks.</returns>
    public static IEnumerable<Track> TopToBottom(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Sequence.Tracks.Where(t => t.Kind == TrackKind.Video).Reverse()
            .Concat(project.Sequence.Tracks.Where(t => t.Kind == TrackKind.Audio));
    }

    /// <summary>The project name: the file name without extension, or <c>Untitled</c>.</summary>
    /// <param name="path">The project file, or null.</param>
    /// <returns>The name.</returns>
    public static string NameOf(string? path) => string.IsNullOrEmpty(path) ? "Untitled" : Path.GetFileNameWithoutExtension(path.Replace('\\', '/').Split('/')[^1]);

    /// <summary>A frame rate as <c>num/den</c>, for example <c>30000/1001</c> or <c>25/1</c>.</summary>
    /// <param name="rate">The rate.</param>
    /// <returns>The text.</returns>
    public static string RateText(FrameRate rate) => RatioText(rate.Value);

    /// <summary>Flicks as seconds, rounded to the microsecond.</summary>
    /// <param name="flicks">The flicks.</param>
    /// <returns>The seconds.</returns>
    public static double Seconds(long flicks) => Math.Round((double)flicks / TimeMath.FlicksPerSecond, 6);

    /// <summary>The source position at the clip's end, in flicks (speed included).</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The flicks.</returns>
    public static long SourceOutFlicks(MediaClip clip, FrameRate rate)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return TimeMath.SourceFlicksAt(clip, clip.End, rate);
    }

    private static string RatioText(Rational r) => string.Create(CultureInfo.InvariantCulture, $"{r.Num}/{r.Den}");

    private static ClipSummaryDto MapClip(Project project, Clip clip, Func<string, bool> isOnline)
    {
        var dto = new ClipSummaryDto
        {
            Id = clip.Id,
            Start = clip.Start,
            Duration = clip.Duration,
            End = clip.End,
            Enabled = clip.Enabled,
            LinkId = clip.LinkId,
            DisplayName = TimelineQuery.DisplayName(project, clip),
        };
        switch (clip)
        {
            case MediaClip m:
                var rate = project.Sequence.Settings.Rate;
                var outFlicks = SourceOutFlicks(m, rate);
                dto.Type = "media";
                dto.MediaId = m.MediaId;
                dto.Stream = EnumWire.Name(m.Stream);
                dto.Offline = !isOnline(m.MediaId);
                dto.SourceInFlicks = m.SourceInFlicks;
                dto.SourceOutFlicks = outFlicks;
                dto.SourceInSeconds = Seconds(m.SourceInFlicks);
                dto.SourceOutSeconds = Seconds(outFlicks);
                dto.SpeedPercent = m.SpeedPercent;
                if (m.Stream == StreamKind.Video)
                {
                    var v = m.Video;
                    dto.Video = new ClipVideoDto
                    {
                        Transform = new TransformDto
                        {
                            X = v.Transform.X,
                            Y = v.Transform.Y,
                            Scale = v.Transform.Scale,
                            CropLeft = v.Transform.CropLeft,
                            CropTop = v.Transform.CropTop,
                            CropRight = v.Transform.CropRight,
                            CropBottom = v.Transform.CropBottom,
                        },
                        Opacity = v.Opacity,
                        Color = new ColorDto { Brightness = v.Color.Brightness, Contrast = v.Color.Contrast, Saturation = v.Color.Saturation, Gamma = v.Color.Gamma },
                        FadeInFrames = v.FadeInFrames,
                        FadeOutFrames = v.FadeOutFrames,
                    };
                }
                else
                {
                    var a = m.Audio;
                    dto.Audio = new ClipAudioDto
                    {
                        GainDb = a.GainDb,
                        FadeInFrames = a.FadeInFrames,
                        FadeOutFrames = a.FadeOutFrames,
                        Keys = [.. (a.Keys.IsDefault ? [] : a.Keys).Select(k => new VolumeKeyDto { Frame = k.Frame, Db = k.Db })],
                    };
                }

                break;
            case TitleClip t:
                dto.Type = "title";
                dto.Title = MapTitle(t.Title);
                dto.Opacity = t.Opacity;
                dto.FadeInFrames = t.FadeInFrames;
                dto.FadeOutFrames = t.FadeOutFrames;
                break;
        }

        return dto;
    }

    private static TitleDto MapTitle(TitleSpec s) => new()
    {
        Text = s.Text,
        Font = s.Font,
        Size = s.Size,
        Color = s.Color,
        OutlineWidth = s.OutlineWidth,
        OutlineColor = s.OutlineColor,
        Box = s.Box,
        BoxColor = s.BoxColor,
        BoxPadding = s.BoxPadding,
        Anchor = EnumWire.Name(s.Anchor),
        OffsetX = s.OffsetX,
        OffsetY = s.OffsetY,
    };
}
