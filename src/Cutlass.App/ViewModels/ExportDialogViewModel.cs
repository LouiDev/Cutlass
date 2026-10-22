using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services;
using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Emit;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Media.Export;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The export dialog (DESIGN 9): preset (greyed out by capability), quality, resolution (Sequence, fixed heights or a
/// custom even height with the aspect locked; widths for GIF), range, output path with Browse and validation, Limit
/// peaks, an estimated size, and "Show ffmpeg command" built like the export itself (title fonts resolved). Export
/// asks before replacing a file, queues the job through the start callback and closes the dialog.
/// </summary>
public sealed partial class ExportDialogViewModel : ObservableObject
{
    /// <summary>The smallest custom output height (the sequence settings minimum).</summary>
    public const int MinCustomHeight = 16;

    /// <summary>The largest custom output height (8K).</summary>
    public const int MaxCustomHeight = 4320;

    /// <summary>The largest output width a custom height may give (8K).</summary>
    public const int MaxCustomWidth = 8192;

    private readonly Project _project;
    private readonly FfmpegCapabilities _capabilities;
    private readonly IDialogService? _dialogs;
    private readonly IMediaCatalog? _catalog;
    private readonly Func<ExportRequest, CancellationToken, Task<string>>? _buildCommand;
    private readonly Func<ExportRequest, bool>? _start;
    private readonly string _baseName;

    /// <summary>Creates the dialog state for a project snapshot.</summary>
    /// <param name="project">The project to export (a snapshot).</param>
    /// <param name="capabilities">What the ffmpeg build can do.</param>
    /// <param name="baseName">The output file name without extension, normally the project name.</param>
    /// <param name="outputFolder">The folder of the suggested output path.</param>
    /// <param name="dialogs">The dialog service for Browse and overwrite confirmation, or null (tests).</param>
    /// <param name="catalog">The media catalog the export and the command preview compile against, or null.</param>
    /// <param name="buildCommand">Builds the ffmpeg command of a request the way the export runs it (fonts resolved),
    /// or null before ffmpeg was found. It is called off the UI thread.</param>
    /// <param name="start">Queues the export; returns false when it could not be queued. Null in tests.</param>
    /// <param name="initialPresetId">The preset to select first (Settings, the last export), or null for H.264.</param>
    public ExportDialogViewModel(
        Project project,
        FfmpegCapabilities capabilities,
        string baseName,
        string outputFolder,
        IDialogService? dialogs = null,
        IMediaCatalog? catalog = null,
        Func<ExportRequest, CancellationToken, Task<string>>? buildCommand = null,
        Func<ExportRequest, bool>? start = null,
        string? initialPresetId = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(capabilities);
        _project = project;
        _capabilities = capabilities;
        _dialogs = dialogs;
        _catalog = catalog;
        _buildCommand = buildCommand;
        _start = start;
        _baseName = string.IsNullOrWhiteSpace(baseName) ? "Untitled" : baseName;

        Presets = [.. ExportPresets.All.Select(p => new ExportPresetOption(p, p.UnavailableReason(capabilities)))];
        Qualities = Enum.GetValues<ExportQuality>();
        SelectedQuality = ExportPresets.DefaultQuality;
        LimitPeaks = true;
        var sequence = project.Sequence;
        HasInOut = sequence.InFrame is not null || sequence.OutFrame is not null;
        IsInOutRange = HasInOut;
        IsWholeRange = !HasInOut;

        var remembered = initialPresetId is null ? null : ExportPresets.Find(initialPresetId);
        var initial = Presets.FirstOrDefault(o => o.Preset == remembered && o.IsAvailable)
            ?? Presets.FirstOrDefault(o => o.Preset == ExportPresets.Default && o.IsAvailable)
            ?? Presets.FirstOrDefault(o => o.IsAvailable)
            ?? Presets[0];
        OutputPath = SuggestOutputPath(outputFolder, _baseName, initial.Preset.Extension);
        SelectedPreset = initial;
    }

    /// <summary>Raised when the dialog should close; the argument is true when an export was queued.</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>Every preset with its availability.</summary>
    public IReadOnlyList<ExportPresetOption> Presets { get; }

    /// <summary>The quality choices.</summary>
    public IReadOnlyList<ExportQuality> Qualities { get; }

    /// <summary>The size choices for the selected preset (heights for video, widths for GIF, none for audio).</summary>
    public ObservableCollection<ExportResolutionOption> Resolutions { get; } = [];

    /// <summary>The selected preset.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQualityEnabled))]
    [NotifyPropertyChangedFor(nameof(HasResolution))]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial ExportPresetOption SelectedPreset { get; set; }

    /// <summary>The quality.</summary>
    [ObservableProperty]
    public partial ExportQuality SelectedQuality { get; set; }

    /// <summary>The output size.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomResolution))]
    [NotifyPropertyChangedFor(nameof(CustomSizeText))]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial ExportResolutionOption? SelectedResolution { get; set; }

    /// <summary>The custom output height, as typed (used when <see cref="IsCustomResolution"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomSizeText))]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial string CustomHeightText { get; set; } = string.Empty;

    /// <summary>True when the custom resolution entry is selected.</summary>
    public bool IsCustomResolution => SelectedResolution?.IsCustom == true && SelectedPreset.Preset.HasVideo;

    /// <summary>The size the custom height gives, for example "= 1280x720", or empty when it is invalid.</summary>
    public string CustomSizeText =>
        IsCustomResolution && TryCustomSize(_project.Sequence.Settings, CustomHeightText, out var w, out var h)
            ? string.Create(CultureInfo.InvariantCulture, $"= {w}x{h}")
            : string.Empty;

    /// <summary>Range: the whole sequence.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial bool IsWholeRange { get; set; }

    /// <summary>Range: In to Out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial bool IsInOutRange { get; set; }

    /// <summary>The output file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProblemText))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial string OutputPath { get; set; }

    /// <summary>Run the mix through the peak limiter (on by default).</summary>
    [ObservableProperty]
    public partial bool LimitPeaks { get; set; }

    /// <summary>The estimated file size, or a note that it depends on the content.</summary>
    [ObservableProperty]
    public partial string EstimatedSizeText { get; set; } = string.Empty;

    /// <summary>The ffmpeg command, once "Show ffmpeg command" was pressed.</summary>
    [ObservableProperty]
    public partial string? CommandText { get; set; }

    /// <summary>Why the last Export press did not queue (an existing folder, a file that was not replaced), or null.</summary>
    [ObservableProperty]
    public partial string? ExportErrorText { get; set; }

    /// <summary>True when the sequence has an In or Out mark.</summary>
    public bool HasInOut { get; }

    /// <summary>True when the preset has a quality choice.</summary>
    public bool IsQualityEnabled => SelectedPreset.Preset.HasQuality;

    /// <summary>True when the preset writes video, so a size can be chosen.</summary>
    public bool HasResolution => SelectedPreset.Preset.HasVideo;

    /// <summary>The selected range as frames and duration.</summary>
    public string RangeText
    {
        get
        {
            var (start, end) = SelectedRange();
            var rate = _project.Sequence.Settings.Rate;
            var frames = Math.Max(0, end - start);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Timecode.Format(start, rate)} to {Timecode.Format(end, rate)} ({frames} frames)");
        }
    }

    /// <summary>Why Export is disabled, or null when it is enabled.</summary>
    public string? ProblemText
    {
        get
        {
            if (SelectedPreset.UnavailableReason is { } reason)
            {
                return char.ToUpperInvariant(reason[0]) + reason[1..] + ".";
            }

            var (start, end) = SelectedRange();
            if (end <= start)
            {
                return "Nothing to export: the range is empty.";
            }

            if (IsCustomResolution && !TryCustomSize(_project.Sequence.Settings, CustomHeightText, out _, out _))
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"Custom height: enter whole pixels from {MinCustomHeight} to {MaxCustomHeight} (the width follows the sequence aspect, at most {MaxCustomWidth}).");
            }

            return OutputPathProblem(OutputPath, SelectedPreset.Preset.Extension, _project);
        }
    }

    /// <summary>
    /// Replaces a known preset extension at the end of a path with another one, or appends the extension when the
    /// path has none or an unrelated one (so "trip.v2" becomes "trip.v2.mp4").
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="extension">The new extension including the dot.</param>
    /// <returns>The path with the extension.</returns>
    public static string WithExtension(string path, string extension)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        if (path.Length == 0)
        {
            return path;
        }

        var current = Path.GetExtension(path);
        if (current.Equals(extension, StringComparison.OrdinalIgnoreCase))
        {
            return path[..^current.Length] + extension;
        }

        var known = ExportPresets.All.Any(p => p.Extension.Equals(current, StringComparison.OrdinalIgnoreCase));
        return known ? path[..^current.Length] + extension : path + extension;
    }

    /// <summary>The suggested output path: the folder, the base name made file-name safe, and the extension.</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="baseName">The base name, for example the project name.</param>
    /// <param name="extension">The extension including the dot.</param>
    /// <returns>The path.</returns>
    public static string SuggestOutputPath(string folder, string baseName, string extension)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new StringBuilder();
        foreach (var c in baseName ?? string.Empty)
        {
            // Both separators are replaced on every OS so a name never turns into a sub-folder.
            safe.Append(invalid.Contains(c) || c is '/' or '\\' or ':' ? '_' : c);
        }

        var name = safe.ToString().Trim().TrimEnd('.');
        if (name.Length == 0)
        {
            name = "Untitled";
        }

        return Path.Combine(folder, name + extension);
    }

    /// <summary>
    /// The custom size for a typed height (DESIGN 9: even, aspect locked): a whole number from
    /// <see cref="MinCustomHeight"/> to <see cref="MaxCustomHeight"/>, rounded down to even, with the width from the
    /// sequence aspect rounded to even and at most <see cref="MaxCustomWidth"/>.
    /// </summary>
    /// <param name="settings">The sequence settings.</param>
    /// <param name="heightText">The typed height (invariant culture).</param>
    /// <param name="width">The output width.</param>
    /// <param name="height">The output height.</param>
    /// <returns>True when the height is valid.</returns>
    public static bool TryCustomSize(SequenceSettings settings, string? heightText, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(settings);
        width = height = 0;
        if (!int.TryParse((heightText ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var typed) ||
            typed < MinCustomHeight || typed > MaxCustomHeight)
        {
            return false;
        }

        (width, height) = ExportPresets.CanvasForHeight(settings, typed);
        return width <= MaxCustomWidth;
    }

    /// <summary>
    /// Why an output path cannot be used, or null: it must be a full path with a valid file name and must not be one
    /// of the project's media files (by spelling, and by <see cref="FileIdentity.SameFile"/> when the output exists).
    /// The extension follows the preset, so only the name before it is checked.
    /// </summary>
    /// <param name="path">The output path as typed.</param>
    /// <param name="extension">The preset extension including the dot.</param>
    /// <param name="project">The project (its media must not be overwritten), or null.</param>
    /// <returns>The problem, or null.</returns>
    public static string? OutputPathProblem(string? path, string extension, Project? project)
    {
        var text = (path ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return "Choose an output file.";
        }

        string full;
        try
        {
            if (!Path.IsPathFullyQualified(text))
            {
                return "Enter a full path for the output file.";
            }

            full = Path.GetFullPath(WithExtension(text, extension));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "The output path is not valid.";
        }

        var fileName = Path.GetFileName(full);
        if (Path.GetFileNameWithoutExtension(full).Trim().Length == 0 || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "Enter a valid file name for the output.";
        }

        // The spelling first; then, only when the output exists (only then can it replace anything), the file identity,
        // which also catches \\?\ prefixes, short names, symbolic and hard links (TECH_BRIEF 9, as ExportService does).
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (project is not null &&
            (project.Media.Any(m => string.Equals(SafeFullPath(m.Path), full, comparison)) ||
             (File.Exists(full) && project.Media.Any(m => FileIdentity.SameFile(m.Path, full)))))
        {
            return "The output would replace a media file of this project.";
        }

        return null;
    }

    /// <summary>
    /// A rough size for presets whose size depends on the content (CRF video): typical bits per pixel at each quality,
    /// plus the audio bitrate. Null where even that is meaningless (GIF, FLAC, unknown encoders).
    /// </summary>
    /// <param name="preset">The preset.</param>
    /// <param name="capabilities">The build.</param>
    /// <param name="quality">The quality.</param>
    /// <param name="width">The output width.</param>
    /// <param name="height">The output height.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <param name="frames">The number of frames.</param>
    /// <returns>The rough bytes, or null.</returns>
    public static long? EstimateRoughBytes(
        ExportPreset preset,
        FfmpegCapabilities capabilities,
        ExportQuality quality,
        int width,
        int height,
        FrameRate rate,
        long frames)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (frames <= 0 || preset.Resolve(capabilities) is not { } encoding || encoding.Video is not { } video)
        {
            return null;
        }

        // Typical H.264 bits per pixel for Draft, Good, High and Best; the newer codecs need about half to two thirds.
        double[] h264 = [0.04, 0.08, 0.12, 0.2];
        var factor = video.Encoder switch
        {
            "libx264" => 1.0,
            "libx265" => 0.55,
            "libvpx-vp9" => 0.65,
            "libsvtav1" or "libaom-av1" => 0.5,
            _ => 0.0,
        };
        if (factor == 0)
        {
            return null;
        }

        var seconds = TimeMath.SecondsAt(frames, rate).ToDouble();
        var videoBps = h264[Math.Clamp((int)quality, 0, h264.Length - 1)] * factor * width * height * rate.Value.ToDouble();
        var audioBps = encoding.Audio?.Encoder switch
        {
            null => 0,
            "libopus" or "libvorbis" => 160_000,
            _ => 192_000,
        };
        return (long)Math.Round((videoBps + audioBps) / 8 * seconds);
    }

    /// <summary>
    /// Estimates the output size where the bitrate is fixed (PCM, AAC, MP3 VBR averages, ProRes 422 HQ, OpenH264).
    /// CRF encoders depend on the content, so they give null.
    /// </summary>
    /// <param name="preset">The preset.</param>
    /// <param name="capabilities">The build (decides between an encoder and its fallback).</param>
    /// <param name="quality">The quality.</param>
    /// <param name="width">The output width.</param>
    /// <param name="height">The output height.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <param name="frames">The number of frames.</param>
    /// <returns>The estimated bytes, or null when unknown.</returns>
    public static long? EstimateBytes(
        ExportPreset preset,
        FfmpegCapabilities capabilities,
        ExportQuality quality,
        int width,
        int height,
        FrameRate rate,
        long frames)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (frames <= 0 || preset.Resolve(capabilities) is not { } encoding)
        {
            return null;
        }

        var seconds = TimeMath.SecondsAt(frames, rate).ToDouble();
        double? audioBps = encoding.Audio?.Encoder switch
        {
            null => 0,
            "pcm_s16le" => TimeMath.SampleRate * 2 * 16,
            "aac" => preset.Kind == ExportPresetKind.Audio ? 256_000 : 192_000,
            // Average LAME VBR bitrates for -q:a 6, 4, 2 and 0.
            "libmp3lame" => preset.QualityValue(quality) switch { 6 => 115_000, 4 => 165_000, 2 => 190_000, _ => 245_000 },
            _ => null,
        };
        double? videoBps = encoding.Video switch
        {
            null => 0,
            { UsesBitrate: true } => FfmpegArgsBuilder.OpenH264Bitrate(quality, width, height),
            // Apple's ProRes 422 HQ target: 220 Mb/s at 1920x1080 29.97, scaled by pixels and rate.
            { Encoder: "prores_ks" } => 220e6 * ((double)width * height / (1920.0 * 1080.0)) * (rate.Value.ToDouble() / 29.97),
            _ => null,
        };
        if (audioBps is not { } a || videoBps is not { } v)
        {
            return null;
        }

        return (long)Math.Round((a + v) / 8 * seconds);
    }

    /// <summary>Formats a byte count for the dialog, for example "About 12.3 MB".</summary>
    /// <param name="bytes">The bytes, or null.</param>
    /// <returns>The text.</returns>
    public static string FormatEstimate(long? bytes)
    {
        if (bytes is not { } b)
        {
            return "Size depends on the content";
        }

        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = b;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return "About " + value.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>Formats a rough estimate, for example "Roughly 12.3 MB (depends on the content)".</summary>
    /// <param name="bytes">The rough bytes, or null.</param>
    /// <returns>The text.</returns>
    public static string FormatRoughEstimate(long? bytes) =>
        bytes is { } b ? "Roughly" + FormatEstimate(b)["About".Length..] + " (depends on the content)" : FormatEstimate(null);

    /// <summary>The frame range the dialog would export.</summary>
    /// <returns>Start (inclusive) and end (exclusive).</returns>
    public (long Start, long End) SelectedRange() =>
        IsInOutRange && HasInOut ? ExportRequest.InOutRange(_project) : ExportRequest.WholeRange(_project);

    /// <summary>Builds the export request from the current choices.</summary>
    /// <param name="catalog">The media catalog.</param>
    /// <param name="progress">Receives progress, or null.</param>
    /// <returns>The request.</returns>
    public ExportRequest BuildRequest(IMediaCatalog catalog, IProgress<ExportProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var preset = SelectedPreset.Preset;
        var (width, height) = OutputSize();
        var (start, end) = SelectedRange();
        return new ExportRequest(
            _project,
            catalog,
            preset,
            SelectedQuality,
            width,
            height,
            start,
            end,
            LimitPeaks,
            WithExtension(OutputPath.Trim(), preset.Extension),
            progress);
    }

    partial void OnSelectedPresetChanged(ExportPresetOption value)
    {
        OutputPath = WithExtension(OutputPath ?? string.Empty, value.Preset.Extension);
        RebuildResolutions();
        CommandText = null;
        UpdateEstimate();
    }

    partial void OnSelectedQualityChanged(ExportQuality value) => UpdateEstimate();

    partial void OnSelectedResolutionChanged(ExportResolutionOption? value) => UpdateEstimate();

    partial void OnCustomHeightTextChanged(string value) => UpdateEstimate();

    partial void OnIsWholeRangeChanged(bool value)
    {
        if (value)
        {
            IsInOutRange = false;
        }

        UpdateEstimate();
    }

    partial void OnIsInOutRangeChanged(bool value)
    {
        if (value)
        {
            IsWholeRange = false;
        }

        UpdateEstimate();
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private (int Width, int Height) OutputSize()
    {
        var settings = _project.Sequence.Settings;
        if (IsCustomResolution)
        {
            return TryCustomSize(settings, CustomHeightText, out var w, out var h) ? (w, h) : (settings.Width, settings.Height);
        }

        return SelectedResolution is { } r && SelectedPreset.Preset.HasVideo
            ? (r.Width, r.Height)
            : (settings.Width, settings.Height);
    }

    private void RebuildResolutions()
    {
        var settings = _project.Sequence.Settings;
        var preset = SelectedPreset.Preset;
        var previous = SelectedResolution;
        Resolutions.Clear();
        if (preset.Kind == ExportPresetKind.Gif)
        {
            foreach (var w in ExportPresets.GifWidths)
            {
                var (cw, ch) = ExportPresets.CanvasForWidth(settings, w);
                Resolutions.Add(new ExportResolutionOption(string.Create(CultureInfo.InvariantCulture, $"{cw} px wide ({cw}x{ch})"), cw, ch));
            }

            // Unspecified default GIF width: 480, the middle of the DESIGN 9 choices.
            SelectedResolution = Resolutions.FirstOrDefault(r => r.Width == 480) ?? Resolutions[0];
            return;
        }

        if (!preset.HasVideo)
        {
            SelectedResolution = null;
            return;
        }

        Resolutions.Add(new ExportResolutionOption(
            string.Create(CultureInfo.InvariantCulture, $"Sequence ({settings.Width}x{settings.Height})"), settings.Width, settings.Height));
        foreach (var h in ExportPresets.Heights)
        {
            var (cw, ch) = ExportPresets.CanvasForHeight(settings, h);
            Resolutions.Add(new ExportResolutionOption(string.Create(CultureInfo.InvariantCulture, $"{h}p ({cw}x{ch})"), cw, ch));
        }

        // DESIGN 9: custom (even, aspect locked). The height field starts at the sequence height.
        Resolutions.Add(ExportResolutionOption.Custom);
        if (string.IsNullOrWhiteSpace(CustomHeightText))
        {
            CustomHeightText = settings.Height.ToString(CultureInfo.InvariantCulture);
        }

        SelectedResolution = Resolutions.FirstOrDefault(r => previous is not null && r.Label == previous.Label) ?? Resolutions[0];
    }

    private void UpdateEstimate()
    {
        if (SelectedPreset is null)
        {
            return;
        }

        var (start, end) = SelectedRange();
        var (w, h) = OutputSize();
        var rate = _project.Sequence.Settings.Rate;
        var exact = EstimateBytes(SelectedPreset.Preset, _capabilities, SelectedQuality, w, h, rate, end - start);
        EstimatedSizeText = exact is not null
            ? FormatEstimate(exact)
            : FormatRoughEstimate(EstimateRoughBytes(SelectedPreset.Preset, _capabilities, SelectedQuality, w, h, rate, end - start));
    }

    private bool CanExport() => ProblemText is null && _start is not null && _catalog is not null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_catalog is null || _start is null)
        {
            return;
        }

        var request = BuildRequest(_catalog);
        var final = request.FinalPath;
        var (fileExists, isFolder) = await Task.Run(() => (File.Exists(final), Directory.Exists(final)));
        if (isFolder)
        {
            ExportErrorText = final + " is a folder. Enter a file name.";
            return;
        }

        // Never overwrite silently: without a dialog service (tests) an existing file is refused.
        if (fileExists && (_dialogs is null ||
            !await _dialogs.ConfirmAsync("Replace file", $"{Path.GetFileName(final)} already exists. Replace it?", "Replace")))
        {
            ExportErrorText = _dialogs is null ? Path.GetFileName(final) + " already exists." : null;
            return;
        }

        ExportErrorText = null;

        if (_start(request))
        {
            CloseRequested?.Invoke(this, true);
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (_dialogs is null)
        {
            return;
        }

        var preset = SelectedPreset.Preset;
        if (await _dialogs.PickExportPathAsync(OutputPath, preset.Extension, preset.DisplayName) is { } path)
        {
            OutputPath = WithExtension(path, preset.Extension);
        }
    }

    [RelayCommand]
    private async Task ShowCommandAsync()
    {
        if (_catalog is null || _buildCommand is null)
        {
            CommandText = "The command is available once ffmpeg was found.";
            return;
        }

        var request = BuildRequest(_catalog);
        var build = _buildCommand;
        try
        {
            // Font resolution and compiling can take a moment on long timelines, so they run off the UI thread.
            CommandText = await Task.Run(() => build(request, CancellationToken.None));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            CommandText = "The command could not be built: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task CopyCommandAsync()
    {
        if (_dialogs is not null && !string.IsNullOrEmpty(CommandText))
        {
            await _dialogs.CopyToClipboardAsync(CommandText);
        }
    }
}
