using System.Text;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.IntegrationTests.Media;

/// <summary>WorkDirectory: bare names, content-addressed files and the startup cleanup (TECH_BRIEF 5.5).</summary>
public sealed class WorkDirectoryTests
{
    [Theory]
    [InlineData("Inter-Bold.ttf", true)]
    [InlineData("g_0123456789ab.ffg", true)]
    [InlineData("a_b.c-d", true)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("C:font.ttf", false)]
    [InlineData("it's.txt", false)]
    [InlineData("a b.txt", false)]
    [InlineData("dir/a.txt", false)]
    [InlineData("dir\\a.txt", false)]
    [InlineData("Ä.txt", false)]
    [InlineData("a.txt\n", false)]
    public void BareNames(string name, bool expected)
    {
        Assert.Equal(expected, WorkDirectory.IsBareName(name));
    }

    [Fact]
    public void TextIsUtf8WithoutBomOrTrailingNewline()
    {
        using var temp = new TempDirectory("cutlass-work");
        var work = new WorkDirectory(temp.Path);

        var name = work.WriteText("Day Ä\r\nTwo\n\n");
        var again = work.WriteText("Day Ä\nTwo");

        Assert.Matches("^t_[0-9a-f]{8}\\.txt$", name);
        Assert.Equal(name, again);
        var bytes = File.ReadAllBytes(work.GetFullPath(name));
        Assert.Equal(Encoding.UTF8.GetBytes("Day Ä\nTwo"), bytes);
    }

    [Fact]
    public void ScriptsAreContentAddressed()
    {
        using var temp = new TempDirectory("cutlass-work");
        var work = new WorkDirectory(temp.Path);

        var a = work.WriteScript("nullsrc[v]\n");
        var b = work.WriteScript("nullsrc[v]\n");
        var c = work.WriteScript("color[v]\n");

        Assert.Matches("^g_[0-9a-f]{12}\\.ffg$", a);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal("nullsrc[v]\n", File.ReadAllText(work.GetFullPath(a)));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp-*"));
    }

    [Fact]
    public void WriteFileReplacesDifferentContentAndRejectsNonBareNames()
    {
        using var temp = new TempDirectory("cutlass-work");
        var work = new WorkDirectory(temp.Path);

        work.WriteFile("aux.txt", "one");
        work.WriteFile("aux.txt", "two");

        Assert.Equal("two", File.ReadAllText(work.GetFullPath("aux.txt")));
        Assert.Throws<ArgumentException>(() => work.WriteFile("it's.txt", "x"));
        Assert.Throws<ArgumentException>(() => work.GetFullPath("../escape.txt"));
    }

    [Fact]
    public void StaleScratchFilesAreDeleted()
    {
        using var temp = new TempDirectory("cutlass-work");
        var work = new WorkDirectory(temp.Path);
        var now = DateTime.UtcNow;
        string Make(string name, TimeSpan age)
        {
            var path = Path.Combine(temp.Path, name);
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, now - age);
            return path;
        }

        var oldTxt = Make("t_00000000.txt", TimeSpan.FromDays(3));
        var oldFfg = Make("g_000000000000.ffg", TimeSpan.FromDays(3));
        var freshFfg = Make("g_111111111111.ffg", TimeSpan.FromHours(1));
        var oldFont = Make("Inter-Bold.ttf", TimeSpan.FromDays(30));
        var oldTemp = Make("g_2.ffg.tmp-abcdef12", TimeSpan.FromHours(2));
        var freshTemp = Make("g_3.ffg.tmp-abcdef12", TimeSpan.FromMinutes(1));

        Assert.Equal(3, work.DeleteStale(now));

        Assert.False(File.Exists(oldTxt));
        Assert.False(File.Exists(oldFfg));
        Assert.False(File.Exists(oldTemp));
        Assert.True(File.Exists(freshFfg));
        Assert.True(File.Exists(oldFont));
        Assert.True(File.Exists(freshTemp));
    }
}
