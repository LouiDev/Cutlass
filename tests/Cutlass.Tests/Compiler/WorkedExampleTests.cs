using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Emit;
using Cutlass.Compiler.Export;
using Cutlass.Core.Time;
using Cutlass.Media.Ffmpeg;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Compiler;

/// <summary>
/// The worked example of TECH_BRIEF 7.8 is the golden reference: the compiled script and the export command must be
/// byte-identical to the brief, which was verified on real ffmpeg.
/// </summary>
[UseCulture("de-DE")]
public sealed partial class WorkedExampleTests
{
    /// <summary>Capabilities of a full master build, for argument tests.</summary>
    internal static FfmpegCapabilities MasterCapabilities(FilterScriptSyntax syntax = FilterScriptSyntax.Slash, params string[] withoutEncoders)
    {
        string[] encoders = ["libx264", "libopenh264", "libx265", "libvpx-vp9", "libsvtav1", "libaom-av1", "prores_ks", "gif", "aac", "libopus", "libvorbis", "libmp3lame", "pcm_s16le", "flac"];
        return new FfmpegCapabilities(
            "N-126308-test",
            12,
            0,
            syntax,
            ImmutableSortedSet.CreateRange(StringComparer.Ordinal, [.. FfmpegCapabilities.RequiredFilters, "drawtext"]),
            ImmutableSortedSet.CreateRange(StringComparer.Ordinal, encoders.Except(withoutEncoders)));
    }

    [Fact]
    public void ScriptMatchesTheGoldenFile()
    {
        var project = CompilerKit.WorkedExample();
        ModelAssert.Valid(project);

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), CompilerKit.Export(0, 270));

        Golden.Verify("worked_example_7_8", render);
        Assert.Equal(270, render.FrameCount);
        Assert.Equal(432_000, render.SampleCount);
        Assert.Equal(2, render.AudioChannels);
        Assert.Equal("[vout]", render.VideoLabel);
        Assert.Equal("[aout]", render.AudioLabel);
        Assert.Empty(render.Warnings);
        var aux = Assert.Single(render.AuxFiles);
        Assert.Equal("Day One", aux.Utf8Content);
        Assert.Matches("^t_[0-9a-f]{8}\\.txt$", aux.FileName);
    }

    [Fact]
    public void ScriptIsByteIdenticalToTheTechBrief()
    {
        var project = CompilerKit.WorkedExample();
        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), CompilerKit.Export(0, 270));
        var section = BriefSection();

        var blocks = CodeBlocks(section);
        Assert.Equal(blocks[0], render.GraphText);

        // The script name in the brief is the real content address of that script.
        var scriptName = "g_" + WorkDirectory.ShortHash(System.Text.Encoding.UTF8.GetBytes(render.GraphText), 12) + ".ffg";
        Assert.Contains("`" + scriptName + "`", section, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportCommandIsTheOneInTheTechBrief()
    {
        var project = CompilerKit.WorkedExample();
        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), CompilerKit.Export(0, 270));
        var scriptName = "g_" + WorkDirectory.ShortHash(System.Text.Encoding.UTF8.GetBytes(render.GraphText), 12) + ".ffg";
        var encoding = ExportPresets.H264.Resolve(MasterCapabilities())!;

        var args = FfmpegArgsBuilder.Export(
            render,
            encoding,
            ExportQuality.High,
            FrameRate.Fps30,
            1920,
            1080,
            FfmpegArgsBuilder.Graph(render.GraphText, scriptName, FilterScriptSyntax.Slash),
            "D:/Out/final.partial.mp4");

        var command = CodeBlocks(BriefSection())[1];
        var tokens = WhitespaceRegex().Split(command.Trim()).Select(t => t.Trim('"')).ToList();
        Assert.Equal("ffmpeg", tokens[0]);
        Assert.Equal(tokens.Skip(1).ToList(), args);
    }

    [Fact]
    public void ScrubVersionMatchesTheBrief()
    {
        // Frame 150 at Half quality: 30-frame segments at 960x540 and a trim back to the one frame.
        var project = CompilerKit.WorkedExample();

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), CompilerKit.PreviewFrame(150, 151, 960, 540));

        Assert.Equal(
            ["-ss 6.500000 -t 1.100000 -i D:/Clips/a.mp4", "-ss 9.500000 -t 1.100000 -i D:/Clips/b.mp4"],
            Golden.InputsText(render).Split('\n'));
        Assert.Contains("tpad=stop_mode=clone:stop=30,trim=end_frame=30,scale=w=960:h=540:flags=bilinear", render.GraphText, StringComparison.Ordinal);
        Assert.Contains("xfade=transition=fade:duration=1.000000:offset=0.000000,trim=start_frame=15:end_frame=16,setpts=PTS-STARTPTS+0[v0r0]", render.GraphText, StringComparison.Ordinal);
        Assert.Equal(1, render.FrameCount);
        Golden.Verify("window_mid_crossfade", render);
    }

    private static string BriefSection()
    {
        var text = File.ReadAllText(Path.Combine(Golden.RepositoryRoot(), "docs", "TECH_BRIEF.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = text.IndexOf("### 7.8", StringComparison.Ordinal);
        Assert.True(start >= 0, "TECH_BRIEF 7.8 not found.");
        var end = text.IndexOf("\n## 8.", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    private static ImmutableArray<string> CodeBlocks(string section)
    {
        var blocks = ImmutableArray.CreateBuilder<string>();
        var lines = section.Split('\n');
        List<string>? current = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (current is null)
                {
                    current = [];
                }
                else
                {
                    blocks.Add(string.Join('\n', current));
                    current = null;
                }
            }
            else
            {
                current?.Add(line);
            }
        }

        return blocks.ToImmutable();
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}
