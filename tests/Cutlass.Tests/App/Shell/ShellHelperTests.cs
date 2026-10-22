using Cutlass.App.Services;
using Cutlass.App.ViewModels;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Media.Settings;

namespace Cutlass.Tests.App.Shell;

public sealed class ShellHelperTests
{
    [Fact]
    public void ParsesTheCommandLine()
    {
        var options = CommandLineOptions.Parse(["D:/p/trip.cutlass", "--import", "a.mp4", "b.mov"]);
        Assert.Equal("D:/p/trip.cutlass", options.ProjectPath);
        Assert.Equal(["a.mp4", "b.mov"], options.ImportPaths);
        Assert.Empty(options.Unknown);
        Assert.Same(CommandLineOptions.Empty, CommandLineOptions.Parse([]));
        var odd = CommandLineOptions.Parse(["--verbose"]);
        Assert.Null(odd.ProjectPath);
        Assert.Equal(["--verbose"], odd.Unknown);
    }

    [Fact]
    public void RecentListPutsTheNewestFirstWithoutDuplicates()
    {
        IReadOnlyList<string> list = [];
        for (var i = 0; i < 12; i++)
        {
            list = RecentProjectsStore.Touch(list, $"C:/p/{i}.cutlass", StringComparer.OrdinalIgnoreCase);
        }

        Assert.Equal(RecentProjectsStore.MaxCount, list.Count);
        Assert.Equal("C:/p/11.cutlass", list[0]);
        list = RecentProjectsStore.Touch(list, "c:/P/5.CUTLASS", StringComparer.OrdinalIgnoreCase);
        Assert.Equal("c:/P/5.CUTLASS", list[0]);
        Assert.Equal(RecentProjectsStore.MaxCount, list.Count);
        Assert.Single(list, p => p.Equals("C:/p/5.cutlass", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RecentListSurvivesDamageAndRoundTrips()
    {
        Assert.Empty(RecentProjectsStore.Parse("{not json"));
        Assert.Empty(RecentProjectsStore.Parse("{\"a\":1}"));
        Assert.Equal(["x", "y"], RecentProjectsStore.Parse("[\"x\", 3, null, \"y\", \"\"]"));
        Assert.Equal(["D:/ä/b.cutlass"], RecentProjectsStore.Parse(RecentProjectsStore.Serialize(["D:/ä/b.cutlass"])));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(64_200_000, "01:04")]
    [InlineData(3_723_000_000, "1:02:03")]
    public void FormatsMediaDurations(long micros, string expected) =>
        Assert.Equal(expected, MediaItemViewModel.FormatDuration(micros));

    [Fact]
    public void ExpandsFoldersTwoLevelsDeepWithSupportedFilesOnly()
    {
        var dirs = new Dictionary<string, string[]>
        {
            ["root"] = ["root/sub"],
            ["root/sub"] = ["root/sub/deep"],
            ["root/sub/deep"] = [],
        };
        var files = new Dictionary<string, string[]>
        {
            ["root"] = ["root/b.MP4", "root/a.txt", "root/a.wav"],
            ["root/sub"] = ["root/sub/c.png"],
            ["root/sub/deep"] = ["root/sub/deep/too-deep.mp4"],
        };
        var result = MediaBinViewModel.ExpandPaths(
            ["single.xyz", "root", "single.xyz"],
            dirs.ContainsKey,
            d => files[d],
            d => dirs[d]);
        Assert.Equal(["single.xyz", "root/a.wav", "root/b.MP4", "root/sub/c.png"], result);
    }

    [Fact]
    public void PreviewSizesFollowTheQuality()
    {
        var settings = new SequenceSettings(1920, 1080, FrameRate.Fps30);
        Assert.Equal((1920, 1080), Size(ViewerViewModel.SizeFor(PreviewQuality.Full, settings, 100, 100)));
        Assert.Equal((960, 540), Size(ViewerViewModel.SizeFor(PreviewQuality.Half, settings, 4000, 4000)));
        Assert.Equal((480, 270), Size(ViewerViewModel.SizeFor(PreviewQuality.Quarter, settings, 4000, 4000)));
        // Auto: the viewer's physical size, never above the sequence or 1920x1080.
        var auto = ViewerViewModel.SizeFor(PreviewQuality.Auto, settings, 1000, 1000);
        Assert.True(auto.Width <= 1000 && auto.Width % 2 == 0 && auto.Height % 2 == 0);
        Assert.Equal((1920, 1080), Size(ViewerViewModel.SizeFor(PreviewQuality.Auto, settings, 5000, 3000)));
    }

    [Fact]
    public void ErrorDialogKeepsTheLast30Lines()
    {
        var log = Enumerable.Range(1, 31).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var vm = new ErrorDialogViewModel("", " boom ", log, " ");
        Assert.Equal("Error", vm.Title);
        Assert.Equal("boom", vm.Summary);
        Assert.False(vm.HasCommand);
        Assert.StartsWith("2\n", vm.LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("Command:", vm.DetailsText, StringComparison.Ordinal);
    }

    private static (int, int) Size(Cutlass.Media.Preview.PreviewSize size) => (size.Width, size.Height);
}
