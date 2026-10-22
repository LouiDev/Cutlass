using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Serialization.Dto;
using Cutlass.Core.Time;

namespace Cutlass.Core.Serialization;

/// <summary>Mapping between the immutable model and the JSON DTOs.</summary>
public static partial class ProjectSerializer
{
    private const string MtimeFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    private static ProjectFileDto ToDto(Project project, string? projectFilePath)
    {
        var seq = project.Sequence;
        return new ProjectFileDto
        {
            Format = FormatName,
            SchemaVersion = Project.CurrentSchemaVersion,
            App = AppName,
            Media = [.. project.Media.Select(m => ToDto(m, projectFilePath))],
            Sequence = new SequenceDto
            {
                Width = seq.Settings.Width,
                Height = seq.Settings.Height,
                FrameRate = seq.Settings.Rate.Value.ToFfString(),
                SampleRate = TimeMath.SampleRate,
                InFrame = seq.InFrame,
                OutFrame = seq.OutFrame,
                HasHadClips = seq.HasHadClips,
                Markers = [.. seq.Markers.Select(m => new MarkerDto { Id = m.Id, Frame = m.Frame, Name = m.Name, Color = m.Color })],
                Tracks = [.. seq.Tracks.Select(ToDto)],
            },
        };
    }

    private static MediaAssetDto ToDto(MediaAsset asset, string? projectFilePath)
    {
        var info = asset.Info;
        return new MediaAssetDto
        {
            Id = asset.Id,
            Path = ToFilePath(asset.Path),
            RelativePath = projectFilePath is null ? asset.RelativePath : RelativeTo(asset.Path, projectFilePath),
            Fingerprint = asset.Fingerprint,
            SizeBytes = asset.SizeBytes,
            MtimeUtc = ToUtc(asset.MtimeUtc).ToString(MtimeFormat, CultureInfo.InvariantCulture),
            Info = new MediaInfoDto
            {
                DurationUs = info.DurationMicros,
                FormatStartUs = info.FormatStartMicros,
                FormatName = info.FormatName,
                Still = info.IsStill,
                Video = info.Video is { } v
                    ? new VideoStreamInfoDto
                    {
                        Index = v.StreamIndex,
                        Codec = v.Codec,
                        Width = v.Width,
                        Height = v.Height,
                        DisplayWidth = v.DisplayWidth,
                        DisplayHeight = v.DisplayHeight,
                        Rotation = v.Rotation,
                        Sar = RationalText(v.Sar),
                        RFrameRate = RationalText(v.RFrameRate),
                        AvgFrameRate = RationalText(v.AvgFrameRate),
                        TimeBase = RationalText(v.TimeBase),
                        StartPts = v.StartPts,
                        PixFmt = v.PixFmt,
                        Vfr = v.IsVfr,
                        Hdr = v.IsHdr,
                        BitRate = v.BitRate,
                    }
                    : null,
                Audio = info.Audio is { } a
                    ? new AudioStreamInfoDto { Index = a.StreamIndex, Codec = a.Codec, SampleRate = a.SampleRate, Channels = a.Channels }
                    : null,
            },
        };
    }

    private static TrackDto ToDto(Track track)
    {
        var video = track.Kind == TrackKind.Video;
        return new TrackDto
        {
            Id = track.Id,
            Kind = EnumText(track.Kind),
            Name = track.Name,
            Hidden = video ? track.Hidden : null,
            Muted = video ? null : track.Muted,
            Solo = video ? null : track.Solo,
            Locked = track.Locked,
            Targeted = track.Targeted,
            GainDb = video ? null : track.GainDb,
            Clips = [.. track.Clips.Select(ToDto)],
            Transitions =
            [
                .. track.Transitions.Select(t => new TransitionDto
                {
                    Id = t.Id,
                    Kind = EnumText(t.Kind),
                    Left = t.LeftClipId,
                    Right = t.RightClipId,
                    Duration = t.Duration,
                }),
            ],
        };
    }

    private static ClipDto ToDto(Clip clip)
    {
        switch (clip)
        {
            case MediaClip m:
                var isVideo = m.Stream == StreamKind.Video;
                return new ClipDto
                {
                    Type = "media",
                    Id = m.Id,
                    Stream = EnumText(m.Stream),
                    MediaId = m.MediaId,
                    Start = m.Start,
                    Duration = m.Duration,
                    Enabled = m.Enabled,
                    LinkId = m.LinkId,
                    SourceInFlicks = m.SourceInFlicks,
                    SpeedPercent = m.SpeedPercent,
                    Video = isVideo
                        ? new VideoPropsDto
                        {
                            Transform = new TransformDto
                            {
                                X = m.Video.Transform.X,
                                Y = m.Video.Transform.Y,
                                Scale = m.Video.Transform.Scale,
                                Crop = [m.Video.Transform.CropLeft, m.Video.Transform.CropTop, m.Video.Transform.CropRight, m.Video.Transform.CropBottom],
                            },
                            Opacity = m.Video.Opacity,
                            Color = new ColorAdjustDto
                            {
                                Brightness = m.Video.Color.Brightness,
                                Contrast = m.Video.Color.Contrast,
                                Saturation = m.Video.Color.Saturation,
                                Gamma = m.Video.Color.Gamma,
                            },
                            FadeIn = m.Video.FadeInFrames,
                            FadeOut = m.Video.FadeOutFrames,
                        }
                        : null,
                    Audio = isVideo
                        ? null
                        : new AudioPropsDto
                        {
                            GainDb = m.Audio.GainDb,
                            FadeIn = m.Audio.FadeInFrames,
                            FadeOut = m.Audio.FadeOutFrames,
                            Keys = [.. m.Audio.Keys.Select(k => new VolumeKeyDto { Frame = k.Frame, Db = k.Db })],
                        },
                };
            case TitleClip t:
                return new ClipDto
                {
                    Type = "title",
                    Id = t.Id,
                    Start = t.Start,
                    Duration = t.Duration,
                    Enabled = t.Enabled,
                    Title = new TitleSpecDto
                    {
                        Text = t.Title.Text,
                        Font = t.Title.Font,
                        Size = t.Title.Size,
                        Color = t.Title.Color,
                        OutlineWidth = t.Title.OutlineWidth,
                        OutlineColor = t.Title.OutlineColor,
                        Box = t.Title.Box,
                        BoxColor = t.Title.BoxColor,
                        BoxPadding = t.Title.BoxPadding,
                        Anchor = EnumText(t.Title.Anchor),
                        OffsetX = t.Title.OffsetX,
                        OffsetY = t.Title.OffsetY,
                    },
                    Opacity = t.Opacity,
                    FadeIn = t.FadeInFrames,
                    FadeOut = t.FadeOutFrames,
                };
            default:
                throw new InvalidOperationException($"Unknown clip type {clip.GetType().Name}.");
        }
    }

    private static Project FromDto(ProjectFileDto dto)
    {
        var seq = dto.Sequence ?? new SequenceDto();
        var settings = new SequenceSettings(seq.Width, seq.Height, ParseRate(seq.FrameRate));
        var tracks = Items(seq.Tracks, "track").Select(FromDto).ToImmutableArray();
        var markers = Items(seq.Markers, "marker")
            .Select(m => new Marker(m.Id ?? string.Empty, m.Frame, m.Name ?? string.Empty, m.Color ?? TimelineEdits.DefaultMarkerColor))
            .OrderBy(m => m.Frame)
            .ToImmutableArray();
        var sequence = new Sequence(settings, tracks, markers, seq.InFrame, seq.OutFrame, seq.HasHadClips);
        var media = Items(dto.Media, "media").Select(FromDto).ToImmutableArray();
        return new Project(Project.CurrentSchemaVersion, media, sequence);
    }

    private static MediaAsset FromDto(MediaAssetDto dto)
    {
        var info = dto.Info ?? new MediaInfoDto();
        var v = info.Video;
        var a = info.Audio;
        return new MediaAsset(
            dto.Id ?? string.Empty,
            dto.Path ?? string.Empty,
            dto.RelativePath,
            dto.Fingerprint ?? string.Empty,
            dto.SizeBytes,
            ParseMtime(dto.MtimeUtc),
            new MediaInfo(
                info.DurationUs,
                info.FormatStartUs,
                info.FormatName ?? string.Empty,
                info.Still,
                v is null
                    ? null
                    : new VideoStreamInfo(
                        v.Index,
                        v.Codec ?? string.Empty,
                        v.Width,
                        v.Height,
                        v.DisplayWidth,
                        v.DisplayHeight,
                        v.Rotation,
                        ParseRational(v.Sar, Rational.One),
                        ParseRational(v.RFrameRate, Rational.Zero),
                        ParseRational(v.AvgFrameRate, Rational.Zero),
                        ParseRational(v.TimeBase, Rational.One),
                        v.StartPts,
                        v.PixFmt ?? string.Empty,
                        v.Vfr,
                        v.Hdr,
                        v.BitRate),
                a is null ? null : new AudioStreamInfo(a.Index, a.Codec ?? string.Empty, a.SampleRate, a.Channels)));
    }

    private static Track FromDto(TrackDto dto)
    {
        var kind = ParseEnum<TrackKind>(dto.Kind, "track kind");
        var clips = Items(dto.Clips, "clip").Select(FromDto).OrderBy(c => c.Start).ToImmutableArray();
        var transitions = Items(dto.Transitions, "transition")
            .Select(t => new Transition(
                t.Id ?? string.Empty,
                TryParseEnum(t.Kind, out TransitionKind k) ? k : TransitionKind.Crossfade,
                t.Left ?? string.Empty,
                t.Right ?? string.Empty,
                t.Duration))
            .ToImmutableArray();
        return new Track(
            dto.Id ?? string.Empty,
            kind,
            dto.Name ?? string.Empty,
            dto.Hidden ?? false,
            dto.Locked,
            dto.Targeted,
            dto.Muted ?? false,
            dto.Solo ?? false,
            dto.GainDb ?? 0,
            clips,
            transitions);
    }

    private static Clip FromDto(ClipDto dto)
    {
        switch (dto.Type)
        {
            case "media":
                var stream = ParseEnum<StreamKind>(dto.Stream, "clip stream");
                var v = dto.Video ?? new VideoPropsDto();
                var a = dto.Audio ?? new AudioPropsDto();
                var t = v.Transform ?? new TransformDto();
                var crop = t.Crop ?? [];
                var c = v.Color ?? new ColorAdjustDto();
                return new MediaClip(
                    dto.Id ?? string.Empty,
                    dto.Start,
                    dto.Duration,
                    dto.Enabled,
                    dto.LinkId,
                    dto.MediaId ?? string.Empty,
                    stream,
                    dto.SourceInFlicks ?? 0,
                    dto.SpeedPercent ?? 100,
                    new VideoProps(
                        new Transform(t.X, t.Y, t.Scale, CropAt(crop, 0), CropAt(crop, 1), CropAt(crop, 2), CropAt(crop, 3)),
                        v.Opacity,
                        new ColorAdjust(c.Brightness, c.Contrast, c.Saturation, c.Gamma),
                        v.FadeIn,
                        v.FadeOut),
                    new AudioProps(
                        a.GainDb,
                        a.FadeIn,
                        a.FadeOut,
                        [.. Items(a.Keys, "volume key").Select(k => new VolumeKey(k.Frame, k.Db))]));
            case "title":
                var title = dto.Title ?? new TitleSpecDto();
                return new TitleClip(
                    dto.Id ?? string.Empty,
                    dto.Start,
                    dto.Duration,
                    dto.Enabled,
                    new TitleSpec(
                        title.Text ?? string.Empty,
                        title.Font ?? TitleSpec.Default.Font,
                        title.Size,
                        title.Color ?? TitleSpec.Default.Color,
                        title.OutlineWidth,
                        title.OutlineColor ?? TitleSpec.Default.OutlineColor,
                        title.Box,
                        title.BoxColor ?? TitleSpec.Default.BoxColor,
                        title.BoxPadding,
                        TryParseEnum(title.Anchor, out TitleAnchor anchor) ? anchor : TitleAnchor.Center,
                        title.OffsetX,
                        title.OffsetY),
                    dto.Opacity ?? 1,
                    dto.FadeIn ?? 0,
                    dto.FadeOut ?? 0);
            default:
                throw new InvalidDataException($"Unknown clip type '{dto.Type}'.");
        }
    }

    private static double CropAt(double[] crop, int index) => index < crop.Length ? crop[index] : 0;

    /// <summary>The entries of a list read from JSON; a <c>null</c> entry means a damaged file and is reported as such.</summary>
    private static IEnumerable<T> Items<T>(List<T>? list, string what)
        where T : class =>
        (list ?? []).Select(item => item ?? throw new InvalidDataException($"The project file has an empty {what} entry."));

    private static string EnumText<T>(T value)
        where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static bool TryParseEnum<T>(string? text, out T value)
        where T : struct, Enum
    {
        value = default;
        return !string.IsNullOrEmpty(text) && !char.IsAsciiDigit(text[0]) && !text.StartsWith('-')
            && Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    private static T ParseEnum<T>(string? text, string what)
        where T : struct, Enum =>
        TryParseEnum(text, out T value) ? value : throw new InvalidDataException($"Unknown {what} '{text}'.");

    private static string RationalText(Rational value) =>
        value.Den == 0 ? "0/1" : string.Create(CultureInfo.InvariantCulture, $"{value.Num}/{value.Den}");

    private static Rational ParseRational(string? text, Rational fallback) =>
        Rational.TryParse(text, out var value) ? value : fallback;

    private static FrameRate ParseRate(string? text)
    {
        if (Rational.TryParse(text, out var exact) && exact.IsPositive)
        {
            var rate = new FrameRate(exact);
            return rate.IsSupported ? rate : FrameRate.Nearest(exact);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var approx) && approx > 0 && approx < 1000)
        {
            return FrameRate.Nearest(Rational.Create((long)Math.Round(approx * 1000), 1000));
        }

        // Unreadable rate: fall back to the default sequence rate rather than refusing the file.
        return TimelineEdits.DefaultSettings.Rate;
    }

    private static DateTime ParseMtime(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value)
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Utc);

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value,
    };

    private static string? RelativeTo(string mediaPath, string projectFilePath)
    {
        try
        {
            var native = ToNativePath(mediaPath);
            if (!Path.IsPathFullyQualified(native) || Path.GetDirectoryName(Path.GetFullPath(projectFilePath)) is not { } directory)
            {
                return null;
            }

            var relative = Path.GetRelativePath(directory, native);
            return Path.IsPathFullyQualified(relative) ? null : ToFilePath(relative);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
