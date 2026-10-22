using Cutlass.Compiler.Emit;
using Cutlass.Compiler.Graph;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Compiler;

/// <summary>FilterEscaper: bare names, quoting, two-level escaping and drawtext text files (TECH_BRIEF 5.5).</summary>
[UseCulture("de-DE")]
public sealed class FilterEscaperTests
{
    [Theory]
    [InlineData("Inter-Bold.ttf")]
    [InlineData("t_5f2a9c1d.txt")]
    [InlineData("g_3f9a1c27b0d4.ffg")]
    [InlineData("font_0123abcd.otf")]
    [InlineData("a")]
    [InlineData("..a")]
    public void AcceptsBareNames(string name)
    {
        Assert.True(FilterEscaper.IsBareName(name));
        Assert.Equal(name, FilterEscaper.RequireBareName(name));
        Assert.Equal(name, FilterGraphBuilder.FileName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("C:/Fonts/arial.ttf")]
    [InlineData("C:arial.ttf")]
    [InlineData("fonts\\arial.ttf")]
    [InlineData("it's.txt")]
    [InlineData("a b.txt")]
    [InlineData("a,b.txt")]
    [InlineData("a;b.txt")]
    [InlineData("[a].txt")]
    [InlineData("Ä.txt")]
    [InlineData("日本.txt")]
    [InlineData("a.txt\n")]
    [InlineData("a:b")]
    public void RejectsAnythingElse(string name)
    {
        Assert.False(FilterEscaper.IsBareName(name));
        Assert.Throws<ArgumentException>(() => FilterEscaper.RequireBareName(name));
    }

    [Fact]
    public void RejectsNull()
    {
        Assert.False(FilterEscaper.IsBareName(null));
    }

    [Fact]
    public void QuotesExpressionsAndRefusesQuotesInside()
    {
        Assert.Equal("'pow(10,(-6)/20)'", FilterEscaper.QuoteExpression("pow(10,(-6)/20)"));
        Assert.Throws<ArgumentException>(() => FilterEscaper.QuoteExpression("it's"));
        Assert.Throws<ArgumentException>(() => FilterEscaper.QuoteExpression("a\\b"));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a:b", "a\\\\:b")]
    [InlineData("it's", "it\\\\\\'s")]
    [InlineData("back\\slash", "back\\\\\\\\slash")]
    [InlineData("one, two", "one\\, two")]
    [InlineData("x;y", "x\\;y")]
    [InlineData("[label]", "\\[label\\]")]
    [InlineData(" lead", "\\\\ lead")]
    [InlineData("trail ", "trail\\\\ ")]
    [InlineData("line\nbreak", "line\nbreak")]
    [InlineData("50%", "50%")]
    public void EscapesOptionValuesOnBothLevels(string raw, string expected)
    {
        Assert.Equal(expected, FilterEscaper.EscapeOptionValue(raw));
    }

    [Fact]
    public void EscapingMatchesTheFfmpegDocumentationExample()
    {
        // ffmpeg-filters "Notes on filtergraph escaping": the drawtext text example, escaped on both levels.
        var raw = "this is a 'string': may contain one, or more, special characters";

        Assert.Equal(
            "this is a \\\\\\'string\\\\\\'\\\\: may contain one\\, or more\\, special characters",
            FilterEscaper.EscapeOptionValue(raw));
    }

    [Theory]
    [InlineData("Day One", "Day One")]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\rb", "a\nb")]
    [InlineData("trailing\n\n", "trailing")]
    [InlineData("trailing\r\n", "trailing")]
    [InlineData("\nleading", "\nleading")]
    [InlineData("it's: 50%, [odd] \\ {x}", "it's: 50%, [odd] \\ {x}")]
    public void NormalizesDrawtextText(string raw, string expected)
    {
        Assert.Equal(expected, FilterEscaper.NormalizeTitleText(raw));
    }

    [Fact]
    public void TitleTextNeverReachesTheGraph()
    {
        var text = "Colons: 'quotes', back\\slashes, commas; [brackets] %{pts}\r\nsecond line\n";
        var project = CompilerKit.Project(
            new SequenceSettings(1920, 1080, Cutlass.Core.Time.FrameRate.Fps30),
            [],
            CompilerKit.VideoTrack("tr_v1", [CompilerKit.Title("c_t", 0, 30, TitleSpec.Default with { Text = text })]));

        var render = TimelineCompiler.Compile(project, new TestCatalog(), CompilerKit.Export(0, 30, audio: false));

        var aux = Assert.Single(render.AuxFiles);
        Assert.Equal("Colons: 'quotes', back\\slashes, commas; [brackets] %{pts}\nsecond line", aux.Utf8Content);
        Assert.Equal(TitleEmitter.TextFileName(aux.Utf8Content), aux.FileName);
        Assert.Contains("textfile=" + aux.FileName + ":expansion=none", render.GraphText, StringComparison.Ordinal);
        Assert.DoesNotContain("Colons", render.GraphText, StringComparison.Ordinal);
        Assert.DoesNotContain("'quotes'", render.GraphText, StringComparison.Ordinal);
    }

    [Fact]
    public void TextFileNamesMatchTheWorkDirectory()
    {
        // The compiler names the file; the work directory writes it. Both must use the same content address.
        using var temp = new TempFolder();
        var work = new Cutlass.Media.Ffmpeg.WorkDirectory(temp.Path);
        var text = FilterEscaper.NormalizeTitleText("Straße 日本\r\nzwei");

        Assert.Equal(work.WriteText(text), TitleEmitter.TextFileName(text));
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cutlass-escaper-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }
}
