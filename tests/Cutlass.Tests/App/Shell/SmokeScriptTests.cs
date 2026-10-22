using Cutlass.App.Services;
using Cutlass.Core.Time;

namespace Cutlass.Tests.App.Shell;

public sealed class SmokeScriptTests
{
    [Fact]
    public void ParsesCommandsArgumentsQuotesAndComments()
    {
        var steps = SmokeScript.Parse(
            "# comment\r\n\r\nimport \"my clips/a b.mp4\" c.mp4\n  EXPORT h264 out/x.mp4 quality=draft\nprobe x.mp4 frames=export bt709\n");

        Assert.Equal(3, steps.Count);
        Assert.Equal(new[] { 3, 4, 5 }, steps.Select(s => s.Line));
        Assert.Equal("import", steps[0].Command);
        Assert.Equal(["my clips/a b.mp4", "c.mp4"], steps[0].Args);
        Assert.Equal("export", steps[1].Command);
        Assert.Equal("draft", steps[1].Option("quality"));
        Assert.Null(steps[1].Option("range"));
        Assert.Equal(["h264", "out/x.mp4"], steps[1].Positional());
        Assert.True(steps[2].Flag("BT709"));
        Assert.Equal("export", steps[2].Option("frames"));
        Assert.Equal(["x.mp4"], steps[2].Positional("bt709"));
    }

    [Fact]
    public void RejectsUnknownCommandsAndOpenQuotes()
    {
        var unknown = Assert.Throws<FormatException>(() => SmokeScript.Parse("import a.mp4\nexplode now"));
        Assert.Contains("Line 2", unknown.Message, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => SmokeScript.Parse("import \"a.mp4"));
    }

    [Fact]
    public void TheShippedSmokeScriptsParse()
    {
        // build/smoke/*.smoke run only on a desktop with ffmpeg (build/smoke.ps1); a typo there should fail here first.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Cutlass.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var scripts = Directory.GetFiles(Path.Combine(directory.FullName, "build", "smoke"), "*.smoke");
        Assert.Contains(scripts, s => Path.GetFileName(s) == "m4.smoke");
        Assert.Contains(scripts, s => Path.GetFileName(s) == "m5.smoke");
        foreach (var script in scripts)
        {
            Assert.NotEmpty(SmokeScript.Parse(File.ReadAllText(script)));
        }
    }

    [Fact]
    public void EveryCommandHasAUsageLine()
    {
        foreach (var (command, usage) in SmokeScript.Commands)
        {
            Assert.StartsWith(command, usage, StringComparison.Ordinal);
            Assert.Equal(command, command.ToLowerInvariant());
        }
    }

    [Theory]
    [InlineData("start", 0)]
    [InlineData("middle", 180)]
    [InlineData("end", 361)]
    [InlineData("42", 42)]
    [InlineData("+10", 110)]
    [InlineData("-10", 90)]
    [InlineData("-500", 0)]
    [InlineData("00:00:02:15", 75)]
    public void ResolvesPositions(string text, long expected)
    {
        Assert.True(SmokeScript.TryResolvePosition(text, 361, 100, FrameRate.Fps30, out var frame));
        Assert.Equal(expected, frame);
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("1:99")]
    public void RejectsOtherPositions(string text) =>
        Assert.False(SmokeScript.TryResolvePosition(text, 361, 100, FrameRate.Fps30, out _));

    [Fact]
    public void ParsesTheSmokeOptionsWhereverTheyAppear()
    {
        var options = CommandLineOptions.Parse(["--import", "a.mp4", "--smoke-script", "s.smoke", "b.mp4", "--smoke-log", "l.txt"]);
        Assert.Equal("s.smoke", options.SmokeScriptPath);
        Assert.Equal("l.txt", options.SmokeLogPath);
        Assert.Equal(["a.mp4", "b.mp4"], options.ImportPaths);
        Assert.Empty(options.Unknown);

        var dangling = CommandLineOptions.Parse(["--smoke-script"]);
        Assert.Null(dangling.SmokeScriptPath);
        Assert.Equal(["--smoke-script"], dangling.Unknown);
        Assert.Null(CommandLineOptions.Parse(["p.cutlass"]).SmokeScriptPath);
    }
}
