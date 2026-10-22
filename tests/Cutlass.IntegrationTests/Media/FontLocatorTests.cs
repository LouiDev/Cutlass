using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Platform;

namespace Cutlass.IntegrationTests.Media;

/// <summary>FontLocator: bundled Inter next to the executable, user fonts, fallbacks and drawtext with fontfile=.</summary>
public sealed class FontLocatorTests
{
    [Fact]
    public void BundledFontsAreCopiedIntoTheWorkDirectory()
    {
        using var temp = new TempDirectory("cutlass-fonts");
        var work = new Cutlass.Media.Ffmpeg.WorkDirectory(temp.Combine("work"));
        var locator = new FontLocator(AppContext.BaseDirectory, work, runner: null);

        var present = locator.EnsureBundledFonts();

        Assert.Equal(FontLocator.BundledFontFiles, present);
        foreach (var file in FontLocator.BundledFontFiles)
        {
            var source = locator.FindBundledFont(file);
            Assert.NotNull(source);
            var copy = Path.Combine(work.Path, file);
            Assert.True(File.Exists(copy), file);
            Assert.Equal(new FileInfo(source).Length, new FileInfo(copy).Length);
        }

        // A copy with a different size is replaced; one with the right size is left alone.
        var bold = Path.Combine(work.Path, "Inter-Bold.ttf");
        File.WriteAllText(bold, "truncated");
        locator.EnsureBundledFonts();
        Assert.Equal(new FileInfo(locator.FindBundledFont("Inter-Bold.ttf")!).Length, new FileInfo(bold).Length);
    }

    [Fact]
    public async Task BuiltinReferenceResolvesToTheBareInterName()
    {
        using var temp = new TempDirectory("cutlass-fonts");
        var work = new Cutlass.Media.Ffmpeg.WorkDirectory(temp.Combine("work"));
        var locator = new FontLocator(AppContext.BaseDirectory, work, runner: null);

        var resolved = await locator.ResolveAsync("builtin:Inter-SemiBold", CancellationToken.None);

        Assert.Equal(new FontResolution("Inter-SemiBold.ttf", IsFallback: false), resolved);
        Assert.True(File.Exists(Path.Combine(work.Path, "Inter-SemiBold.ttf")));
    }

    [Fact]
    public async Task UserFontFromAnOddPathIsImportedOnceUnderAContentName()
    {
        using var temp = new TempDirectory("cutlass-fonts");
        var work = new Cutlass.Media.Ffmpeg.WorkDirectory(temp.Combine("work"));
        var locator = new FontLocator(AppContext.BaseDirectory, work, runner: null);
        var oddDir = temp.Combine("it's, [odd] Ä");
        Directory.CreateDirectory(oddDir);
        var userFont = Path.Combine(oddDir, "My Font ü.TTF");
        File.Copy(locator.FindBundledFont("Inter-Regular.ttf")!, userFont);

        var first = await locator.ResolveAsync(userFont, CancellationToken.None);
        var second = await locator.ResolveAsync(userFont, CancellationToken.None);

        Assert.NotNull(first);
        Assert.False(first.IsFallback);
        Assert.Matches("^font_[0-9a-f]{8}\\.ttf$", first.BareName);
        Assert.Equal(first, second);
        Assert.Single(Directory.GetFiles(work.Path, "font_*"));
    }

    [Fact]
    public async Task UnsafeUserFontPathsAreNeverReadAndTakeTheFallback()
    {
        // A font path comes from a project file: a folder, a wrong extension, an oversized file or a device must not
        // be read whole, and must not throw.
        using var temp = new TempDirectory("cutlass-fonts");
        var work = new Cutlass.Media.Ffmpeg.WorkDirectory(temp.Combine("work"));
        var locator = new FontLocator(AppContext.BaseDirectory, work, runner: null);
        var folder = temp.Combine("folder.ttf");
        Directory.CreateDirectory(folder);
        var text = temp.Combine("notes.txt");
        File.WriteAllText(text, "not a font");
        var huge = temp.Combine("huge.ttf");
        using (var stream = File.Create(huge))
        {
            stream.SetLength(FontLocator.MaxFontFileBytes + 1);
        }

        var device = OperatingSystem.IsWindows() ? @"\\.\NUL" : "/dev/zero";
        foreach (var path in new[] { folder, text, huge, device })
        {
            Assert.False(FontLocator.IsAcceptableFontFile(path), path);
            var resolved = await locator.ResolveAsync(path, CancellationToken.None);
            if (resolved is not null)
            {
                Assert.True(resolved.IsFallback, path);
                Assert.True(File.Exists(Path.Combine(work.Path, resolved.BareName)));
            }
        }

        Assert.DoesNotContain(Directory.GetFiles(work.Path, "font_*"), f => new FileInfo(f).Length > FontLocator.MaxFontFileBytes);
        Assert.True(FontLocator.IsAcceptableFontFile(locator.FindBundledFont("Inter-Regular.ttf")!));
    }

    [Fact]
    public async Task WithoutAnySystemFontTheBundledInterIsTheLastResort()
    {
        if (!OperatingSystem.IsWindows())
        {
            // The Linux fallbacks come from fontconfig and a fixed path, which the test cannot hide.
            return;
        }

        using var temp = new TempDirectory("cutlass-fonts");
        var work = new Cutlass.Media.Ffmpeg.WorkDirectory(temp.Combine("work"));
        var locator = new FontLocator(AppContext.BaseDirectory, work, runner: null) { WindowsFontsOverride = temp.Combine("nowhere") };

        var resolved = await locator.ResolveAsync(temp.Combine("missing.otf"), CancellationToken.None);

        Assert.Equal(new FontResolution("Inter-Regular.ttf", IsFallback: true), resolved);
    }

    [Fact]
    public async Task MissingFontFallsBackToASystemFont()
    {
        using var temp = new TempDirectory("cutlass-fonts");
        var work = new Cutlass.Media.Ffmpeg.WorkDirectory(temp.Combine("work"));
        await using var h = TestFfmpeg.IsAvailable ? await MediaHarness.CreateAsync() : null;
        var locator = new FontLocator(temp.Path, work, h?.Runner);

        var resolved = await locator.ResolveAsync("builtin:Inter-Bold", CancellationToken.None);

        // temp.Path has no fonts/ folder, so the bundled font is "missing" here.
        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(resolved);
        }

        if (resolved is not null)
        {
            Assert.True(resolved.IsFallback);
            Assert.True(File.Exists(Path.Combine(work.Path, resolved.BareName)));
        }
    }

    [Fact]
    public async Task SystemFontListIsNotEmptyOnWindows()
    {
        using var temp = new TempDirectory("cutlass-fonts");
        await using var h = TestFfmpeg.IsAvailable ? await MediaHarness.CreateAsync() : null;
        var locator = new FontLocator(AppContext.BaseDirectory, new Cutlass.Media.Ffmpeg.WorkDirectory(temp.Path), h?.Runner);

        var fonts = await locator.ListSystemFontsAsync(CancellationToken.None);

        if (OperatingSystem.IsWindows())
        {
            Assert.NotEmpty(fonts);
            Assert.Contains(fonts, f => f.Path.EndsWith("arial.ttf", StringComparison.OrdinalIgnoreCase));
        }

        Assert.All(fonts, f => Assert.True(Path.IsPathRooted(f.Path)));
    }

    [Fact]
    public void FcListOutputIsParsed()
    {
        var fonts = FontLocator.ParseFcList(
            "DejaVu Sans|/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf\n" +
            "Noto Sans CJK JP|/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc\n" +
            "Bitstream|/usr/share/fonts/type1/b.pfb\n" +
            "\n" +
            "broken line\n");

        Assert.Equal(
            new[]
            {
                new SystemFont("DejaVu Sans", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
                new SystemFont("Noto Sans CJK JP", "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc"),
            },
            fonts);
    }

    [FfmpegFact]
    public async Task DrawtextRendersWithBareFontfileAndTextfileFromTheWorkDirectory()
    {
        await using var h = await MediaHarness.CreateAsync();
        var locator = new FontLocator(AppContext.BaseDirectory, h.Work, h.Runner);
        var font = await locator.ResolveAsync("builtin:Inter-Bold", CancellationToken.None);
        var text = h.Work.WriteText("it's, [odd] Ä: 50%\r\n");

        var result = await h.Runner.RunAsync(
            h.Ffmpeg(["-hide_banner", "-nostdin", "-v", "error", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=25:d=0.04",
                      "-vf", $"drawtext=fontfile={font!.BareName}:textfile={text}:expansion=none:fontsize=24:fontcolor=white:x=10:y=10",
                      "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "gray", "pipe:1"]),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.Equal(0, result.EnsureSuccess().ExitCode);
        Assert.DoesNotContain(result.StderrTail, l => l.Contains("Fontconfig", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(320 * 180, result.Stdout.Length);
        Assert.Contains(result.Stdout, b => b > 128);
    }
}
