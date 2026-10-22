using System.Diagnostics;
using Cutlass.App.Services;
using Cutlass.Core.Editing;
using Cutlass.Core.Session;
using Cutlass.Media.Projects;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.App.Shell;

/// <summary>
/// Autosave owner locks (DESIGN 11, two instances): an autosave whose lock names a live process is never offered for
/// recovery, a stale lock is ignored, the autosave service writes and deletes its locks, the crash write is tracked,
/// and atomic writes use unique temp names.
/// </summary>
public sealed class AutosaveLockTests : IDisposable
{
    private const int LivePid = 4242;
    private const long LiveStart = 638_000_000_000_000_000;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "cutlass-autosave-lock-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ProjectSession _session = new(TimelineEdits.NewProject(new SequentialIdGenerator()), () => 0);
    private readonly ProjectStore _store = new();
    private DateTime _now = new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

    public AutosaveLockTests() => Directory.CreateDirectory(_root);

    private string Folder => Path.Combine(_root, "autosave");

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
    public void OwnersRoundTrip()
    {
        var text = AutosaveRecovery.FormatOwner(LivePid, LiveStart);
        Assert.True(AutosaveRecovery.TryParseOwner(text, out var pid, out var start));
        Assert.Equal((LivePid, LiveStart), (pid, start));
        Assert.False(AutosaveRecovery.TryParseOwner(null, out _, out _));
        Assert.False(AutosaveRecovery.TryParseOwner("garbage", out _, out _));
        Assert.False(AutosaveRecovery.TryParseOwner("-1 5", out _, out _));
        Assert.Equal("x.cutlass.lock", AutosaveRecovery.LockPath("x.cutlass"));
    }

    [Fact]
    public void ThisProcessIsAliveAndAReusedIdIsNot()
    {
        using var self = Process.GetCurrentProcess();
        var start = self.StartTime.ToUniversalTime().Ticks;
        Assert.True(AutosaveRecovery.IsProcessAlive(Environment.ProcessId, start));
        Assert.False(AutosaveRecovery.IsProcessAlive(Environment.ProcessId, start - TimeSpan.TicksPerHour));
        Assert.True(AutosaveRecovery.TryParseOwner(AutosaveRecovery.CurrentOwner(), out var pid, out var ticks));
        Assert.True(AutosaveRecovery.IsProcessAlive(pid, ticks));
    }

    [Fact]
    public async Task UntitledAutosavesOfALiveInstanceAreSkipped()
    {
        Directory.CreateDirectory(Folder);
        var live = AutosaveRecovery.UntitledPath(Folder, "live");
        var crashed = AutosaveRecovery.UntitledPath(Folder, "crashed");
        var unlocked = AutosaveRecovery.UntitledPath(Folder, "unlocked");
        var damaged = AutosaveRecovery.UntitledPath(Folder, "damaged");
        foreach (var path in new[] { live, crashed, unlocked, damaged })
        {
            await _store.SaveAutosaveAsync(_session.Current, path, null);
        }

        await File.WriteAllTextAsync(AutosaveRecovery.LockPath(live), AutosaveRecovery.FormatOwner(LivePid, LiveStart));
        await File.WriteAllTextAsync(AutosaveRecovery.LockPath(crashed), AutosaveRecovery.FormatOwner(LivePid + 1, LiveStart));
        await File.WriteAllTextAsync(AutosaveRecovery.LockPath(damaged), "not a lock");

        var found = AutosaveRecovery.FindUntitled(Folder, IsAlive).Select(r => r.AutosavePath).ToList();

        Assert.DoesNotContain(live, found);
        Assert.Contains(crashed, found);
        Assert.Contains(unlocked, found);
        Assert.Contains(damaged, found);
        Assert.Equal(3, found.Count);
        Assert.DoesNotContain(live, AutosaveRecovery.FindAll(Folder, [], IsAlive).Select(r => r.AutosavePath));
    }

    [Fact]
    public async Task AProjectAutosaveOfALiveInstanceIsNotOffered()
    {
        var project = Path.Combine(_root, "edit.cutlass");
        await _store.SaveAsync(_session.Current, project);
        File.SetLastWriteTimeUtc(project, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var autosave = ProjectStore.AutosavePath(project);
        await _store.SaveAutosaveAsync(_session.Current, autosave, project);
        await File.WriteAllTextAsync(AutosaveRecovery.LockPath(autosave), AutosaveRecovery.FormatOwner(LivePid, LiveStart));

        Assert.Null(AutosaveRecovery.ForProject(project, IsAlive));
        Assert.Empty(AutosaveRecovery.FindAll(Folder, [project], IsAlive));

        // The owner crashed: its lock is stale and the autosave is offered.
        Assert.NotNull(AutosaveRecovery.ForProject(project, (_, _) => false));
        Assert.Single(AutosaveRecovery.FindAll(Folder, [project], (_, _) => false));
    }

    [Fact]
    public async Task TheServiceLocksWhatItWritesAndUnlocksWhatItDeletes()
    {
        using var autosave = Create();
        Edit();
        _now = _now.AddMinutes(1);
        Assert.True(await autosave.TickAsync());

        var file = autosave.TargetPath;
        var lockFile = AutosaveRecovery.LockPath(file);
        Assert.True(File.Exists(lockFile));
        Assert.Equal(AutosaveRecovery.FormatOwner(LivePid, LiveStart), await File.ReadAllTextAsync(lockFile));
        Assert.Empty(AutosaveRecovery.FindUntitled(Folder, IsAlive));

        await autosave.ClearAsync(includeProjectAutosave: false);
        Assert.False(File.Exists(file));
        Assert.False(File.Exists(lockFile));
    }

    [Fact]
    public async Task AnAdoptedAutosaveIsLockedAtOnce()
    {
        Directory.CreateDirectory(Folder);
        var recovered = AutosaveRecovery.UntitledPath(Folder, "old");
        await _store.SaveAutosaveAsync(_session.Current, recovered, null);
        Assert.Single(AutosaveRecovery.FindUntitled(Folder, IsAlive));

        using var autosave = Create();
        autosave.Adopt(recovered);

        await WaitUntilAsync(() => File.Exists(AutosaveRecovery.LockPath(recovered)));
        Assert.Empty(AutosaveRecovery.FindUntitled(Folder, IsAlive));
    }

    [Fact]
    public async Task TheCrashWriteIsLockedAndDeletedByACleanClose()
    {
        using var autosave = Create();
        Edit();

        Assert.True(autosave.TrySaveNowBlocking(TimeSpan.FromSeconds(5)));
        var file = autosave.TargetPath;
        Assert.True(File.Exists(file));
        Assert.True(File.Exists(AutosaveRecovery.LockPath(file)));

        await autosave.ClearAsync(includeProjectAutosave: false);
        Assert.False(File.Exists(file));
        Assert.False(File.Exists(AutosaveRecovery.LockPath(file)));
    }

    [Fact]
    public async Task AtomicWritesUseUniqueTempNamesAndLeaveNoneBehind()
    {
        var path = Path.Combine(_root, "unique.cutlass");
        var a = ProjectStore.TempPath(path);
        var b = ProjectStore.TempPath(path);
        Assert.NotEqual(a, b);
        Assert.StartsWith(path + "." + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".", a, StringComparison.Ordinal);
        Assert.EndsWith(".tmp", a, StringComparison.Ordinal);

        await _store.SaveAutosaveAsync(_session.Current, path, null);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    private static bool IsAlive(int pid, long start) => pid == LivePid && start == LiveStart;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private AutosaveService Create()
    {
        var ids = 0;
        return new AutosaveService(
            _store,
            Folder,
            _session,
            () => null,
            () => false,
            () => _now,
            (_, _) => new NoTimer(),
            () => "id" + (++ids).ToString(System.Globalization.CultureInfo.InvariantCulture),
            AutosaveRecovery.FormatOwner(LivePid, LiveStart))
        {
            Interval = TimeSpan.FromMinutes(2),
        };
    }

    private void Edit() => _session.Execute(p => TimelineEdits.SetInOut(p, 5, 15));

    private sealed class NoTimer : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
