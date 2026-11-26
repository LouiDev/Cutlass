using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels.MediaBin;

/// <summary>
/// One media bin card or row (DESIGN 4.2): poster, name, duration and the badges (busy spinner, VFR, HDR, offline,
/// unsupported). A placeholder item exists while a file is being imported.
/// </summary>
public sealed partial class MediaItemViewModel : ObservableObject
{
    /// <summary>Creates a placeholder for a file being imported.</summary>
    /// <param name="path">The file.</param>
    public MediaItemViewModel(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        IsBusy = true;
    }

    /// <summary>Creates the item of a project asset.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="order">The position in the project's media list (sort by date added).</param>
    public MediaItemViewModel(MediaAsset asset, int order)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Path = asset.Path;
        Name = NameOf(asset.Path);
        Order = order;
        Asset = asset;
    }

    /// <summary>The media file (follows the asset after a relink).</summary>
    public string Path { get; private set; }

    /// <summary>The file name.</summary>
    public string Name { get; private set; }

    /// <summary>The project asset, null while importing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Id))]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    [NotifyPropertyChangedFor(nameof(ResolutionText))]
    [NotifyPropertyChangedFor(nameof(FpsText))]
    [NotifyPropertyChangedFor(nameof(CodecText))]
    [NotifyPropertyChangedFor(nameof(IsVfr))]
    [NotifyPropertyChangedFor(nameof(IsHdr))]
    [NotifyPropertyChangedFor(nameof(IsAudioOnly))]
    [NotifyPropertyChangedFor(nameof(IsStill))]
    [NotifyPropertyChangedFor(nameof(KindGlyph))]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial MediaAsset? Asset { get; set; }

    /// <summary>The poster (160x90 card image), once rendered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPoster))]
    public partial Bitmap? Poster { get; set; }

    /// <summary>True while probing, indexing or rendering the poster (spinner badge).</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>True when the file is missing (red stripes).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial bool IsOffline { get; set; }

    /// <summary>True when the file changed since import and was re-probed (DESIGN 11, "changed" badge).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial bool IsChanged { get; set; }

    /// <summary>The position in the project's media list.</summary>
    [ObservableProperty]
    public partial int Order { get; set; }

    /// <summary>The media id, or empty while importing.</summary>
    public string Id => Asset?.Id ?? string.Empty;

    /// <summary>True when a poster is shown.</summary>
    public bool HasPoster => Poster is not null;

    /// <summary>The duration, for example <c>01:04</c>, or "Still".</summary>
    public string DurationText => Asset is null ? string.Empty : Asset.Info.IsStill ? "Still" : FormatDuration(Asset.Info.DurationMicros);

    /// <summary>The display size, for example <c>1920x1080</c>, or empty for audio.</summary>
    public string ResolutionText => Asset?.Info.Video is { } v
        ? string.Create(CultureInfo.InvariantCulture, $"{v.DisplayWidth}x{v.DisplayHeight}")
        : string.Empty;

    /// <summary>The frame rate, for example <c>29.97</c>, or empty.</summary>
    public string FpsText => Asset?.Info is { Video: { } v, IsStill: false } && v.AvgFrameRate.Num > 0
        ? v.AvgFrameRate.ToDouble().ToString("0.##", CultureInfo.InvariantCulture)
        : string.Empty;

    /// <summary>The codecs, for example <c>h264 / aac</c>.</summary>
    public string CodecText => Asset is null
        ? string.Empty
        : string.Join(" / ", new[] { Asset.Info.Video?.Codec, Asset.Info.Audio?.Codec }.Where(c => !string.IsNullOrEmpty(c)));

    /// <summary>The proxy column: <c>P</c> when a proxy is in use, the progress while one is built, else <c>-</c>.</summary>
    public string ProxyText => HasProxy
        ? "P"
        : IsProxyBuilding ? (ProxyFraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "-";

    /// <summary>True when a proxy is attached (the <c>P</c> chip, DESIGN 4.2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProxyText))]
    public partial bool HasProxy { get; set; }

    /// <summary>True while a proxy job is queued or running (the proxy progress badge).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProxyText))]
    public partial bool IsProxyBuilding { get; set; }

    /// <summary>The proxy job's progress, 0 to 1.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProxyText))]
    [NotifyPropertyChangedFor(nameof(ProxyPercent))]
    public partial double ProxyFraction { get; set; }

    /// <summary>The proxy progress as a percentage for progress bars.</summary>
    public double ProxyPercent => Math.Round(ProxyFraction * 100, 1);

    /// <summary>The thumbnail under the mouse while hover-skimming the card (DESIGN 4.2), or null to show the poster.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSkimming))]
    public partial IImage? SkimImage { get; set; }

    /// <summary>True while a skim thumbnail replaces the poster.</summary>
    public bool IsSkimming => SkimImage is not null;

    /// <summary>True for variable frame rate media (grey VFR badge).</summary>
    public bool IsVfr => Asset?.Info.Video?.IsVfr == true;

    /// <summary>True for HDR media (amber badge).</summary>
    public bool IsHdr => Asset?.Info.Video?.IsHdr == true;

    /// <summary>True when the media has no video.</summary>
    public bool IsAudioOnly => Asset is { Info.Video: null };

    /// <summary>True for still images.</summary>
    public bool IsStill => Asset?.Info.IsStill == true;

    /// <summary>The kind label shown on cards without a poster: AUDIO, IMAGE or VIDEO.</summary>
    public string KindGlyph => IsAudioOnly ? "AUDIO" : IsStill ? "IMAGE" : "VIDEO";

    /// <summary>The card tooltip: path, format and badges.</summary>
    public string ToolTip
    {
        get
        {
            var parts = new List<string> { Path };
            if (IsOffline)
            {
                parts.Add("Offline: the file is missing. Use Relink... to find it.");
            }

            if (IsChanged)
            {
                parts.Add("Changed: the file was modified since import and has been read again.");
            }

            if (Asset is not null)
            {
                parts.Add(string.Join("  ", new[] { DurationText, ResolutionText, FpsText, CodecText }.Where(s => s.Length > 0)));
            }

            if (IsHdr)
            {
                parts.Add("Shown and exported without tone mapping");
            }

            return string.Join('\n', parts);
        }
    }

    private static string NameOf(string path) => System.IO.Path.GetFileName(path.Replace('\\', '/').Split('/')[^1]);

    // A relink keeps the item (same media id) but points it at another file.
    partial void OnAssetChanged(MediaAsset? value)
    {
        if (value is null || string.Equals(value.Path, Path, StringComparison.Ordinal))
        {
            return;
        }

        Path = value.Path;
        Name = NameOf(value.Path);
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ToolTip));
    }

    /// <summary>Formats a duration as <c>mm:ss</c>, or <c>h:mm:ss</c> from one hour.</summary>
    /// <param name="micros">The duration in microseconds.</param>
    /// <returns>The text.</returns>
    public static string FormatDuration(long micros)
    {
        var seconds = Math.Max(0, micros) / 1_000_000;
        var h = seconds / 3600;
        var m = seconds / 60 % 60;
        var s = seconds % 60;
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{m:00}:{s:00}");
    }
}
