using System.Collections.Immutable;
using Cutlass.App.ViewModels.Dialogs;
using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App.Shell;

public sealed class ExportDialogViewModelTests
{
    private static readonly string[] AllEncoders =
        ["libx264", "libopenh264", "libx265", "libvpx-vp9", "libopus", "libvorbis", "libsvtav1", "libaom-av1", "aac", "prores_ks", "libmp3lame", "pcm_s16le", "flac", "gif"];

    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "cutlass-export-tests");

    private static FfmpegCapabilities Caps(params string[] without) => new(
        "N-126308-test",
        12,
        0,
        FilterScriptSyntax.Slash,
        ImmutableSortedSet.CreateRange(StringComparer.Ordinal, FfmpegCapabilities.RequiredFilters),
        ImmutableSortedSet.CreateRange(StringComparer.Ordinal, AllEncoders.Except(without)));

    private static Project Project(long? inFrame = null, long? outFrame = null) =>
        new ProjectBuilder().With(V1, V("v1", 0, 50)).InOut(inFrame, outFrame).Build();

    private static ExportDialogViewModel Dialog(FfmpegCapabilities? caps = null, Project? project = null) =>
        new(project ?? Project(), caps ?? Caps(), "beach day", Folder);

    [Fact]
    public void UnavailablePresetsAreGreyedWithTheReason()
    {
        var vm = Dialog(Caps("libx265", "libvpx-vp9"));
        var h265 = vm.Presets.Single(p => p.Preset == ExportPresets.H265);
        Assert.False(h265.IsAvailable);
        Assert.Equal("libx265 is not in this ffmpeg build", h265.UnavailableReason);
        Assert.Equal(h265.UnavailableReason, h265.ToolTip);
        Assert.False(vm.Presets.Single(p => p.Preset == ExportPresets.Vp9).IsAvailable);
        Assert.True(vm.Presets.Single(p => p.Preset == ExportPresets.H264).IsAvailable);
    }

    [Fact]
    public void DefaultsToH264HighWholeRangeAndLimitPeaks()
    {
        var vm = Dialog();
        Assert.Same(ExportPresets.H264, vm.SelectedPreset.Preset);
        Assert.Equal(ExportQuality.High, vm.SelectedQuality);
        Assert.True(vm.IsWholeRange);
        Assert.False(vm.HasInOut);
        Assert.True(vm.LimitPeaks);
        Assert.Equal(Path.Combine(Folder, "beach day.mp4"), vm.OutputPath);
        Assert.Null(vm.ProblemText);
    }

    [Fact]
    public void FallsBackToTheFirstAvailablePresetWithoutH264()
    {
        var vm = Dialog(Caps("libx264", "libopenh264"));
        Assert.Same(ExportPresets.H265, vm.SelectedPreset.Preset);
        Assert.EndsWith(".mp4", vm.OutputPath, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingThePresetFollowsTheExtension()
    {
        var vm = Dialog();
        vm.SelectedPreset = vm.Presets.Single(p => p.Preset == ExportPresets.Wav);
        Assert.Equal(Path.Combine(Folder, "beach day.wav"), vm.OutputPath);
        Assert.False(vm.HasResolution);
        Assert.False(vm.IsQualityEnabled);
        vm.SelectedPreset = vm.Presets.Single(p => p.Preset == ExportPresets.ProRes);
        Assert.Equal(Path.Combine(Folder, "beach day.mov"), vm.OutputPath);
    }

    [Theory]
    [InlineData("C:/out/trip.mp4", ".webm", "C:/out/trip.webm")]
    [InlineData("C:/out/trip", ".mp4", "C:/out/trip.mp4")]
    [InlineData("C:/out/trip.v2", ".mp4", "C:/out/trip.v2.mp4")]
    [InlineData("C:/out/trip.MP4", ".mp4", "C:/out/trip.mp4")]
    [InlineData("C:/out/trip.m4a", ".gif", "C:/out/trip.gif")]
    [InlineData("", ".mp4", "")]
    public void WithExtensionReplacesOnlyPresetExtensions(string path, string extension, string expected) =>
        Assert.Equal(expected, ExportDialogViewModel.WithExtension(path, extension));

    [Theory]
    [InlineData("beach day", "beach day.mp4")]
    [InlineData("a/b:c", "a_b_c.mp4")]
    [InlineData("  ", "Untitled.mp4")]
    [InlineData("trailing.", "trailing.mp4")]
    public void SuggestedNamesAreFileNameSafe(string baseName, string expected) =>
        Assert.Equal(Path.Combine("out", expected), ExportDialogViewModel.SuggestOutputPath("out", baseName, ".mp4"));

    [Fact]
    public void GifOffersWidthsAndDefaultsTo480()
    {
        var vm = Dialog();
        vm.SelectedPreset = vm.Presets.Single(p => p.Preset == ExportPresets.Gif);
        Assert.Equal([320, 480, 640, 800], vm.Resolutions.Select(r => r.Width));
        Assert.Equal(480, vm.SelectedResolution!.Width);
    }

    [Fact]
    public void VideoResolutionsStartWithTheSequence()
    {
        var vm = Dialog();
        var settings = Project().Sequence.Settings;
        Assert.Equal(settings.Width, vm.Resolutions[0].Width);
        // Sequence, 2160p, 1080p, 720p and Custom (DESIGN 9).
        Assert.Equal(5, vm.Resolutions.Count);
        Assert.True(vm.Resolutions[^1].IsCustom);
        Assert.Equal(1080, vm.Resolutions.Single(r => r.Label.StartsWith("1080p", StringComparison.Ordinal)).Height);
    }

    [Fact]
    public void InOutRangeIsUsedWhenMarked()
    {
        var vm = Dialog(project: Project(10, 30));
        Assert.True(vm.HasInOut);
        Assert.True(vm.IsInOutRange);
        Assert.Equal((10L, 30L), vm.SelectedRange());
        vm.IsWholeRange = true;
        Assert.False(vm.IsInOutRange);
        Assert.Equal((0L, 50L), vm.SelectedRange());
    }

    [Fact]
    public void EmptyRangeAndMissingPathDisableExport()
    {
        var empty = new ProjectBuilder().Build();
        var vm = Dialog(project: empty);
        Assert.Equal("Nothing to export: the range is empty.", vm.ProblemText);
        var ok = Dialog();
        ok.OutputPath = " ";
        Assert.Equal("Choose an output file.", ok.ProblemText);
        Assert.False(ok.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void UnavailableSelectionExplainsItself()
    {
        var vm = Dialog(Caps("libx265"));
        vm.SelectedPreset = vm.Presets.Single(p => p.Preset == ExportPresets.H265);
        Assert.Equal("Libx265 is not in this ffmpeg build.", vm.ProblemText);
    }

    [Fact]
    public void BuildsTheRequestFromTheChoices()
    {
        var vm = Dialog(project: Project(5, 25));
        vm.SelectedQuality = ExportQuality.Draft;
        vm.SelectedResolution = vm.Resolutions.Single(r => r.Label.StartsWith("720p", StringComparison.Ordinal));
        vm.LimitPeaks = false;
        vm.OutputPath = Path.Combine(Folder, "cut");
        var request = vm.BuildRequest(new EmptyCatalog());
        Assert.Same(ExportPresets.H264, request.Preset);
        Assert.Equal(ExportQuality.Draft, request.Quality);
        Assert.Equal(720, request.Height);
        Assert.Equal((5L, 25L), (request.StartFrame, request.EndFrame));
        Assert.False(request.LimitPeaks);
        Assert.Equal(Path.Combine(Folder, "cut.mp4"), request.OutputPath);
    }

    [Fact]
    public void EstimatesFixedBitratePresets()
    {
        var caps = Caps();
        // 2 s of 16-bit stereo PCM at 48 kHz.
        Assert.Equal(384_000, ExportDialogViewModel.EstimateBytes(ExportPresets.Wav, caps, ExportQuality.High, 0, 0, FrameRate.Fps25, 50));
        Assert.Equal(64_000, ExportDialogViewModel.EstimateBytes(ExportPresets.Aac, caps, ExportQuality.High, 0, 0, FrameRate.Fps25, 50));
        Assert.Null(ExportDialogViewModel.EstimateBytes(ExportPresets.H264, caps, ExportQuality.High, 1920, 1080, FrameRate.Fps25, 50));
        Assert.NotNull(ExportDialogViewModel.EstimateBytes(ExportPresets.H264, Caps("libx264"), ExportQuality.High, 1920, 1080, FrameRate.Fps25, 50));
        Assert.Null(ExportDialogViewModel.EstimateBytes(ExportPresets.Flac, caps, ExportQuality.High, 0, 0, FrameRate.Fps25, 50));
        Assert.Equal("About 384.0 KB", ExportDialogViewModel.FormatEstimate(384_000));
        Assert.Equal("Size depends on the content", ExportDialogViewModel.FormatEstimate(null));
    }

    private sealed class EmptyCatalog : IMediaCatalog
    {
        public MediaRenderInfo? Get(string mediaId) => null;
    }
}
