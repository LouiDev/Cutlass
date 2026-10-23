using System.Collections.Immutable;
using Cutlass.App.ViewModels.Dialogs;
using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Media.Export;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App.Shell;

/// <summary>
/// M7 export dialog hardening (DESIGN 9): the custom resolution (even, aspect locked, limits), path validation, the
/// remembered preset, rough estimates, overwrite protection and the font-resolving command preview hook.
/// </summary>
public sealed class ExportDialogHardeningTests : IDisposable
{
    private static readonly string[] AllEncoders =
        ["libx264", "libopenh264", "libx265", "libvpx-vp9", "libopus", "libvorbis", "libsvtav1", "libaom-av1", "aac", "prores_ks", "libmp3lame", "pcm_s16le", "flac", "gif"];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cutlass-export-hardening-" + Guid.NewGuid().ToString("N"));

    /// <summary>Removes the temporary folder.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written.
        }
    }

    private static FfmpegCapabilities Caps(params string[] without) => new(
        "N-126308-test",
        12,
        0,
        FilterScriptSyntax.Slash,
        ImmutableSortedSet.CreateRange(StringComparer.Ordinal, FfmpegCapabilities.RequiredFilters),
        ImmutableSortedSet.CreateRange(StringComparer.Ordinal, AllEncoders.Except(without)));

    private static Project Project() => new ProjectBuilder().With(V1, V("v1", 0, 50)).Build();

    private static SequenceSettings Hd => new(1920, 1080, FrameRate.Fps25);

    private ExportDialogViewModel Dialog(
        Project? project = null,
        FfmpegCapabilities? caps = null,
        string? preset = null,
        Func<ExportRequest, bool>? start = null,
        Func<ExportRequest, CancellationToken, Task<string>>? command = null,
        Cutlass.App.Services.Dialogs.IDialogService? dialogs = null) =>
        new(project ?? Project(), caps ?? Caps(), "trip", _folder, dialogs, new EmptyCatalog(), command, start, preset);

    [Theory]
    [InlineData("720", 1280, 720)]
    [InlineData("721", 1280, 720)]
    [InlineData(" 360 ", 640, 360)]
    [InlineData("16", 28, 16)]
    [InlineData("4320", 7680, 4320)]
    [InlineData("1081", 1920, 1080)]
    public void CustomHeightsAreEvenWithTheAspectLocked(string text, int width, int height)
    {
        Assert.True(ExportDialogViewModel.TryCustomSize(Hd, text, out var w, out var h));
        Assert.Equal((width, height), (w, h));
        Assert.Equal(0, w % 2);
        Assert.Equal(0, h % 2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("15")]
    [InlineData("4321")]
    [InlineData("-720")]
    [InlineData("720.5")]
    [InlineData("abc")]
    public void CustomHeightsOutsideTheLimitsAreRefused(string text) =>
        Assert.False(ExportDialogViewModel.TryCustomSize(Hd, text, out _, out _));

    [Fact]
    public void ACustomHeightThatGivesAnOverWideFrameIsRefused()
    {
        // A 4:1 sequence at 4320 lines would be 17280 wide.
        var wide = new SequenceSettings(4000, 1000, FrameRate.Fps25);
        Assert.False(ExportDialogViewModel.TryCustomSize(wide, "4320", out _, out _));
        Assert.True(ExportDialogViewModel.TryCustomSize(wide, "2048", out var w, out _));
        Assert.Equal(8192, w);
    }

    [Fact]
    public void TheCustomEntryDrivesTheRequestAndExplainsBadInput()
    {
        var vm = Dialog();
        Assert.False(vm.IsCustomResolution);
        vm.SelectedResolution = vm.Resolutions.Single(r => r.IsCustom);
        Assert.True(vm.IsCustomResolution);
        Assert.Equal("1080", vm.CustomHeightText);
        vm.CustomHeightText = "541";
        Assert.Equal("= 960x540", vm.CustomSizeText);
        var request = vm.BuildRequest(new EmptyCatalog());
        Assert.Equal((960, 540), (request.Width, request.Height));

        vm.CustomHeightText = "8";
        Assert.StartsWith("Custom height: enter whole pixels from 16 to 4320", vm.ProblemText, StringComparison.Ordinal);
        Assert.Equal(string.Empty, vm.CustomSizeText);
        Assert.False(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void GifAndAudioPresetsHaveNoCustomEntry()
    {
        var vm = Dialog();
        vm.SelectedPreset = vm.Presets.Single(p => p.Preset == ExportPresets.Gif);
        Assert.DoesNotContain(vm.Resolutions, r => r.IsCustom);
        vm.SelectedPreset = vm.Presets.Single(p => p.Preset == ExportPresets.Wav);
        Assert.Empty(vm.Resolutions);
        Assert.False(vm.IsCustomResolution);
    }

    [Fact]
    public void OutputPathsAreValidated()
    {
        var full = Path.Combine(_folder, "out.mp4");
        Assert.Null(ExportDialogViewModel.OutputPathProblem(full, ".mp4", null));
        Assert.Equal("Choose an output file.", ExportDialogViewModel.OutputPathProblem("  ", ".mp4", null));
        Assert.Equal("Enter a full path for the output file.", ExportDialogViewModel.OutputPathProblem("out.mp4", ".mp4", null));
        Assert.Equal("Enter a valid file name for the output.", ExportDialogViewModel.OutputPathProblem(Path.Combine(_folder, " .mp4"), ".mp4", null));
        Assert.Equal("Enter a valid file name for the output.", ExportDialogViewModel.OutputPathProblem(_folder + Path.DirectorySeparatorChar, ".mp4", null));
    }

    [Fact]
    public void TheOutputMayNotReplaceAMediaFileOfTheProject()
    {
        var source = Path.Combine(_folder, "source.mp4");
        var project = Project();
        project = project with { Media = project.Media.Add(Asset("m_src", source, 1_000_000, video: true, audio: true)) };
        Assert.Equal("The output would replace a media file of this project.", ExportDialogViewModel.OutputPathProblem(source, ".mp4", project));
        var vm = Dialog(project);
        vm.OutputPath = Path.Combine(_folder, "source");
        Assert.Equal("The output would replace a media file of this project.", vm.ProblemText);
    }

    [Fact]
    public void AnotherSpellingOfAProjectMediaFileIsCaughtByFileIdentity()
    {
        Directory.CreateDirectory(_folder);
        var source = Path.Combine(_folder, "source.mp4");
        File.WriteAllText(source, "media");
        var project = Project();
        project = project with { Media = project.Media.Add(Asset("m_src", source, 1_000_000, video: true, audio: true)) };

        // A spelling a string comparison cannot match: the \\?\ prefix on Windows, a symbolic link on Linux.
        string other;
        if (OperatingSystem.IsWindows())
        {
            other = @"\\?\" + source;
        }
        else
        {
            other = Path.Combine(_folder, "link.mp4");
            File.CreateSymbolicLink(other, source);
        }

        Assert.NotEqual(Path.GetFullPath(source), Path.GetFullPath(other), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("The output would replace a media file of this project.", ExportDialogViewModel.OutputPathProblem(other, ".mp4", project));

        // A different existing file next to it is fine.
        var unrelated = Path.Combine(_folder, "unrelated.mp4");
        File.WriteAllText(unrelated, "other");
        Assert.Null(ExportDialogViewModel.OutputPathProblem(unrelated, ".mp4", project));
    }

    [Fact]
    public void TheRememberedPresetIsSelectedWhenAvailable()
    {
        Assert.Same(ExportPresets.Vp9, Dialog(preset: "vp9").SelectedPreset.Preset);
        Assert.EndsWith(".webm", Dialog(preset: "vp9").OutputPath, StringComparison.Ordinal);
        Assert.Same(ExportPresets.H264, Dialog(caps: Caps("libvpx-vp9"), preset: "vp9").SelectedPreset.Preset);
        Assert.Same(ExportPresets.H264, Dialog(preset: "no-such-preset").SelectedPreset.Preset);
    }

    [Fact]
    public void ContentDependentPresetsGetARoughEstimate()
    {
        var caps = Caps();
        var rough = ExportDialogViewModel.EstimateRoughBytes(ExportPresets.H264, caps, ExportQuality.Good, 1920, 1080, FrameRate.Fps25, 250);
        Assert.NotNull(rough);

        // 0.08 bits per pixel at 1080p25 for 10 s plus AAC 192k.
        Assert.Equal((long)Math.Round(((0.08 * 1920 * 1080 * 25) + 192_000) / 8 * 10), rough);
        var hevc = ExportDialogViewModel.EstimateRoughBytes(ExportPresets.H265, caps, ExportQuality.Good, 1920, 1080, FrameRate.Fps25, 250);
        Assert.True(hevc < rough);
        Assert.Null(ExportDialogViewModel.EstimateRoughBytes(ExportPresets.Gif, caps, ExportQuality.Good, 480, 270, FrameRate.Fps25, 250));
        Assert.Null(ExportDialogViewModel.EstimateRoughBytes(ExportPresets.Flac, caps, ExportQuality.Good, 0, 0, FrameRate.Fps25, 250));
        Assert.Equal("Roughly 1.2 MB (depends on the content)", ExportDialogViewModel.FormatRoughEstimate(1_234_000));

        var vm = Dialog();
        Assert.StartsWith("Roughly ", vm.EstimatedSizeText, StringComparison.Ordinal);
        vm.SelectedPreset = vm.Presets.Single(p => p.Preset == ExportPresets.Wav);
        Assert.StartsWith("About ", vm.EstimatedSizeText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExistingFileIsNeverReplacedWithoutConfirmation()
    {
        Directory.CreateDirectory(_folder);
        var existing = Path.Combine(_folder, "taken.mp4");
        await File.WriteAllTextAsync(existing, "x");
        var started = new List<ExportRequest>();
        var dialogs = new FakeDialogService();
        var vm = Dialog(start: r => { started.Add(r); return true; }, dialogs: dialogs);
        vm.OutputPath = existing;

        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Empty(started);
        Assert.Single(dialogs.Confirmations);

        dialogs.ConfirmAnswer = true;
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Single(started);

        // Without a dialog service there is nobody to ask, so the file is refused.
        var silent = Dialog(start: r => { started.Add(r); return true; });
        silent.OutputPath = existing;
        await silent.ExportCommand.ExecuteAsync(null);
        Assert.Single(started);
        Assert.Equal("taken.mp4 already exists.", silent.ExportErrorText);
    }

    [Fact]
    public async Task AFolderIsNotAnOutputFile()
    {
        var folder = Path.Combine(_folder, "clip.mp4");
        Directory.CreateDirectory(folder);
        var started = 0;
        var vm = Dialog(start: _ => { started++; return true; });
        vm.OutputPath = folder;
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal(0, started);
        Assert.Equal(folder + " is a folder. Enter a file name.", vm.ExportErrorText);
    }

    [Fact]
    public async Task ShowCommandUsesTheExportPath()
    {
        ExportRequest? seen = null;
        var vm = Dialog(command: (r, _) => { seen = r; return Task.FromResult("ffmpeg -i x"); });
        await vm.ShowCommandCommand.ExecuteAsync(null);
        Assert.Equal("ffmpeg -i x", vm.CommandText);
        Assert.NotNull(seen);
        Assert.Equal(Path.Combine(_folder, "trip.mp4"), seen!.OutputPath);

        var failing = Dialog(command: (_, _) => throw new InvalidOperationException("libx264 is missing"));
        await failing.ShowCommandCommand.ExecuteAsync(null);
        Assert.Equal("The command could not be built: libx264 is missing", failing.CommandText);

        var none = new ExportDialogViewModel(Project(), Caps(), "trip", _folder);
        await none.ShowCommandCommand.ExecuteAsync(null);
        Assert.Equal("The command is available once ffmpeg was found.", none.CommandText);
    }

    [Fact]
    public void TheCommandPreviewFormatsTheExportArguments()
    {
        var project = Project();
        var request = new ExportRequest(project, new EmptyCatalog(), ExportPresets.Wav, ExportQuality.High, 1920, 1080, 0, 50, true, Path.Combine(_folder, "a.wav"), null);
        var encoding = ExportPresets.Wav.Resolve(Caps())!;
        var render = Cutlass.Compiler.Emit.TimelineCompiler.Compile(project, request.Catalog, request.ToRenderRequest());
        var text = ExportCommandPreview.Format(render, encoding, request, Caps(), "ffmpeg");
        Assert.Contains("pcm_s16le", text, StringComparison.Ordinal);
        Assert.Contains("a.partial.wav", text, StringComparison.Ordinal);
    }

    private sealed class EmptyCatalog : IMediaCatalog
    {
        public MediaRenderInfo? Get(string mediaId) => null;
    }
}
