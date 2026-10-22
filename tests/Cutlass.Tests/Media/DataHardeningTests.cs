using Cutlass.App.Services;
using Cutlass.Media.Platform;
using Cutlass.Media.Projects;
using Cutlass.Media.Settings;

namespace Cutlass.Tests.Media;

/// <summary>
/// M7 hardening of the Media layer: autosave recovery rules, the bounded log, corrupt settings, /proc parsing and
/// fontconfig escapes (DESIGN 11, TECH_BRIEF 5.1 and 5.4).
/// </summary>
public sealed class DataHardeningTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cutlass-data-" + Guid.NewGuid().ToString("N")[..12]);

    public DataHardeningTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public async Task SettingsSavesUseAUniqueTempNameNextToTheFile()
    {
        var file = Path.Combine(_root, "settings.json");

        // Another instance is in the middle of a save with the old fixed temp name, holding it open exclusively.
        await using (new FileStream(file + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await new SettingsStore(file).SaveAsync(new AppSettings());
        }

        Assert.True(File.Exists(file));
        Assert.Equal([file + ".tmp"], Directory.GetFiles(_root, "*.tmp"));
        var temp = ProjectStore.TempPath(file);
        Assert.Matches(@"\.\d+\.[0-9a-f]{8}\.tmp$", temp);
        Assert.StartsWith(file + "." + Environment.ProcessId + ".", temp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecentProjectSavesUseAUniqueTempNameNextToTheFile()
    {
        var file = Path.Combine(_root, "data", "recent.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var store = new RecentProjectsStore(file, StringComparer.Ordinal);
        var project = Path.Combine(_root, "a.cutlass");

        await using (new FileStream(file + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await store.SaveAsync([project]);
        }

        Assert.Equal([project], await store.LoadAsync());
        Assert.Equal([file + ".tmp"], Directory.GetFiles(Path.GetDirectoryName(file)!, "*.tmp"));
    }

    [Fact]
    public void RecoveryIsOfferedOnlyForAnAutosaveNewerThanItsProject()
    {
        var t = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(AutosaveRecovery.ShouldOffer(t.AddSeconds(1), t));
        Assert.False(AutosaveRecovery.ShouldOffer(t, t));
        Assert.False(AutosaveRecovery.ShouldOffer(t.AddMinutes(-5), t));
        Assert.True(AutosaveRecovery.ShouldOffer(t, null));
    }

    [Fact]
    public void ForProjectComparesTheFileTimes()
    {
        var project = Path.Combine(_root, "edit.cutlass");
        var autosave = ProjectStore.AutosavePath(project);
        File.WriteAllText(project, "{}");
        File.WriteAllText(autosave, "{}");
        var t = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

        File.SetLastWriteTimeUtc(project, t);
        File.SetLastWriteTimeUtc(autosave, t.AddMinutes(1));
        var candidate = AutosaveRecovery.ForProject(project);
        Assert.NotNull(candidate);
        Assert.Equal(Path.GetFullPath(project), candidate.ProjectPath);
        Assert.Equal("edit", candidate.Name);
        Assert.False(candidate.IsUntitled);

        File.SetLastWriteTimeUtc(autosave, t.AddMinutes(-1));
        Assert.Null(AutosaveRecovery.ForProject(project));

        File.Delete(autosave);
        Assert.Null(AutosaveRecovery.ForProject(project));
    }

    [Fact]
    public void UntitledAutosavesAreListedNewestFirstWithRecentProjects()
    {
        var folder = Path.Combine(_root, "autosave");
        Directory.CreateDirectory(folder);
        var older = AutosaveRecovery.UntitledPath(folder, "aaa");
        var newer = AutosaveRecovery.UntitledPath(folder, "bbb");
        File.WriteAllText(older, "{}");
        File.WriteAllText(newer, "{}");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "not an autosave");
        var t = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(older, t);
        File.SetLastWriteTimeUtc(newer, t.AddMinutes(2));

        var project = Path.Combine(_root, "recent.cutlass");
        File.WriteAllText(project, "{}");
        File.WriteAllText(ProjectStore.AutosavePath(project), "{}");
        File.SetLastWriteTimeUtc(project, t);
        File.SetLastWriteTimeUtc(ProjectStore.AutosavePath(project), t.AddMinutes(1));

        var untitled = AutosaveRecovery.FindUntitled(folder);
        Assert.Equal([newer, older], untitled.Select(u => u.AutosavePath).ToArray());
        Assert.All(untitled, u => Assert.True(u.IsUntitled));
        Assert.Equal("untitled-aaa.cutlass", Path.GetFileName(older));

        var all = AutosaveRecovery.FindAll(folder, [project, project]);
        Assert.Equal([newer, Path.GetFullPath(ProjectStore.AutosavePath(project)), older], all.Select(a => a.AutosavePath).ToArray());
        Assert.Empty(AutosaveRecovery.FindUntitled(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void TheLogIsBoundedPerDayAndKeepsTheNewestFiles()
    {
        var now = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
        var log = new AppLog(Path.Combine(_root, "logs"), () => now);

        Assert.True(log.Write("first"));
        Assert.Equal(Path.Combine(_root, "logs", "cutlass-2026-10-22.log"), log.CurrentFile);
        Assert.Contains("2026-10-22T10:00:00.000Z first", File.ReadAllText(log.CurrentFile), StringComparison.Ordinal);

        var big = new string('x', 200_000);
        var written = 0;
        while (log.Write(big))
        {
            written++;
        }

        Assert.InRange(new FileInfo(log.CurrentFile).Length, 1, AppLog.MaxFileBytes);
        Assert.Contains("log size limit reached", File.ReadAllText(log.CurrentFile), StringComparison.Ordinal);
        Assert.False(log.Write("dropped"));

        for (var day = 1; day <= AppLog.MaxFiles + 3; day++)
        {
            now = now.AddDays(1);
            Assert.True(log.Write("day " + day));
        }

        Assert.Equal(AppLog.MaxFiles, Directory.GetFiles(log.Directory, "cutlass-*.log").Length);
        Assert.True(File.Exists(log.CurrentFile));
    }

    [Fact]
    public async Task CorruptSettingsFallBackToDefaultsAndSayWhy()
    {
        var file = Path.Combine(_root, "settings.json");
        await File.WriteAllTextAsync(file, "{ \"autosaveIntervalMinutes\": ");
        var store = new SettingsStore(file);

        var settings = await store.LoadAsync();

        Assert.Equal(new AppSettings().AutosaveIntervalMinutes, settings.AutosaveIntervalMinutes);
        Assert.NotNull(store.LastLoadProblem);
        Assert.True(File.Exists(file + ".bad"));

        await store.SaveAsync(settings);
        await store.LoadAsync();
        Assert.Null(store.LastLoadProblem);
    }

    [Theory]
    [InlineData("1234 (ffmpeg) S 1 1234 1234 0 -1 4194560 100 0 0 0 1 2 0 0 20 0 1 0 98765 1000 10 18446744073709551615", "ffmpeg", 98765)]
    [InlineData("77 (my (odd) name) R 1 77 77 0 -1 0 0 0 0 0 0 0 0 0 20 0 1 0 5 1000 10 0", "my (odd) name", 5)]
    public void ProcStatReadsTheStartTimeAfterTheLastParenthesis(string line, string name, long jiffies)
    {
        Assert.True(ProcStat.TryParse(line, out var ticks, out var parsedName));
        Assert.Equal(name, parsedName);
        Assert.Equal(jiffies * (TimeSpan.TicksPerSecond / ProcStat.ClockTicksPerSecond), ticks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234 ffmpeg S 1")]
    [InlineData("1234 (ffmpeg) S 1 2 3")]
    public void ProcStatRejectsMalformedLines(string line) =>
        Assert.False(ProcStat.TryParse(line, out _, out _));

    [Fact]
    public void FontconfigEscapesAreRemovedFromFamilies()
    {
        Assert.Equal("Noto Sans-Bold", FontLocator.UnescapeFcValue(@"Noto Sans\-Bold"));
        Assert.Equal(@"a\b:c", FontLocator.UnescapeFcValue(@"a\\b\:c"));
        Assert.Equal("Plain", FontLocator.UnescapeFcValue("Plain"));

        var fonts = FontLocator.ParseFcList("DejaVu Sans\\-Mono|/usr/share/fonts/dejavu/DejaVuSansMono.ttf\n");
        Assert.Equal("DejaVu Sans-Mono", Assert.Single(fonts).Family);
    }

    [Fact]
    public void AppPathsPlaceAutosavesAndLogsInTheDataFolder()
    {
        var linux = AppPaths.Resolve(false, name => name == "XDG_DATA_HOME" ? "/xdg/data" : null, _ => "/home/me");

        Assert.Equal(Path.Combine(linux.DataDirectory, "autosave"), linux.AutosaveDirectory);
        Assert.Equal(Path.Combine(linux.DataDirectory, "logs"), linux.LogsDirectory);
        Assert.StartsWith(Path.GetFullPath("/xdg/data"), linux.AutosaveDirectory, StringComparison.Ordinal);
    }
}
