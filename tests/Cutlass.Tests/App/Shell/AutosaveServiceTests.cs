using Cutlass.App.Services;
using Cutlass.Core.Editing;
using Cutlass.Core.Session;
using Cutlass.Media.Projects;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.App.Shell;

/// <summary>Autosave timing, suppression and clearing (DESIGN 11) with a fake clock and a fake timer.</summary>
public sealed class AutosaveServiceTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "cutlass-autosave-" + Guid.NewGuid().ToString("N")[..12]);
    private readonly ProjectSession _session = new(TimelineEdits.NewProject(new SequentialIdGenerator()), () => 0);
    private readonly ProjectStore _store = new();
    private DateTime _now = new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
    private string? _projectPath;
    private bool _suppressed;
    private int _inOut;

    public AutosaveServiceTests() => Directory.CreateDirectory(_root);

    private string UntitledFolder => Path.Combine(_root, "autosave");

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
    public void TheRuleWaitsForIdleTimeAndKeepsOneIntervalBetweenAutosaves()
    {
        var t = _now;

        // Off, nothing to save, or a gesture running: never.
        Assert.False(AutosaveService.IsDue(t, TimeSpan.Zero, true, false, t.AddMinutes(-9), t.AddMinutes(-9), null));
        Assert.False(AutosaveService.IsDue(t, Interval, false, false, null, t.AddMinutes(-9), null));
        Assert.False(AutosaveService.IsDue(t, Interval, true, true, t.AddMinutes(-9), t.AddMinutes(-9), null));

        // 30 s after the last edit.
        Assert.False(AutosaveService.IsDue(t, Interval, true, false, t.AddSeconds(-29), t.AddSeconds(-29), null));
        Assert.True(AutosaveService.IsDue(t, Interval, true, false, t.AddSeconds(-30), t.AddSeconds(-30), null));

        // Continuous editing: the oldest waiting edit is one interval old.
        Assert.True(AutosaveService.IsDue(t, Interval, true, false, t - Interval, t.AddSeconds(-1), null));

        // At most once per interval.
        Assert.False(AutosaveService.IsDue(t, Interval, true, false, t.AddMinutes(-1), t.AddMinutes(-1), t.AddMinutes(-1)));
        Assert.True(AutosaveService.IsDue(t, Interval, true, false, t.AddMinutes(-1), t.AddMinutes(-1), t - Interval));
    }

    [Fact]
    public async Task AnUntitledProjectAutosaves30SecondsAfterTheLastEdit()
    {
        using var autosave = Create();
        Edit();

        _now = _now.AddSeconds(29);
        Assert.False(await autosave.TickAsync());
        Assert.False(Directory.Exists(UntitledFolder));

        _now = _now.AddSeconds(1);
        Assert.True(await autosave.TickAsync());
        var file = autosave.TargetPath;
        Assert.Equal(AutosaveRecovery.UntitledPath(UntitledFolder, "id1"), file);
        Assert.True(File.Exists(file));
        Assert.Equal(_session.Current.Sequence.InFrame, (await _store.LoadAsync(file)).Sequence.InFrame);
        Assert.False(autosave.HasPendingEdits);

        // Nothing new: nothing to write.
        _now = _now.AddMinutes(10);
        Assert.False(await autosave.TickAsync());
        Assert.Equal(1, autosave.WriteCount);
    }

    [Fact]
    public async Task AutosavesAreAtLeastOneIntervalApartEvenWhenEditingNeverPauses()
    {
        using var autosave = Create();
        Edit();
        _now = _now.AddSeconds(30);
        Assert.True(await autosave.TickAsync());

        // An edit every 10 s: never idle for 30 s, so the interval decides.
        var start = _now;
        var writes = 0;
        while (_now < start.AddMinutes(5))
        {
            _now = _now.AddSeconds(10);
            Edit();
            if (await autosave.TickAsync())
            {
                writes++;
                Assert.True(_now - start >= Interval);
            }
        }

        Assert.Equal(2, writes);
    }

    [Fact]
    public async Task GesturesSuppressTheAutosaveUntilTheyEnd()
    {
        using var autosave = Create();
        Edit();
        _now = _now.AddMinutes(5);

        _suppressed = true;
        Assert.False(await autosave.TickAsync());
        _session.BeginGesture();
        Assert.True(_session.InGesture);
        Assert.False(await autosave.TickAsync());
        _session.CancelGesture();

        _suppressed = false;
        Assert.True(await autosave.TickAsync());
    }

    [Fact]
    public async Task ZeroIntervalTurnsAutosaveOff()
    {
        using var autosave = Create();
        autosave.Interval = AutosaveService.IntervalFromMinutes(0);
        Edit();
        _now = _now.AddHours(1);

        Assert.False(autosave.IsEnabled);
        Assert.False(await autosave.TickAsync());
        Assert.False(autosave.TrySaveNowBlocking(TimeSpan.FromSeconds(5)));
        Assert.Equal(TimeSpan.FromMinutes(3), AutosaveService.IntervalFromMinutes(3));
        Assert.Equal(TimeSpan.Zero, AutosaveService.IntervalFromMinutes(-1));
    }

    [Fact]
    public async Task UndoBackToTheSavedStateLeavesNothingToAutosave()
    {
        using var autosave = Create();
        Edit();
        Assert.True(autosave.HasPendingEdits);

        _session.Undo();

        Assert.False(_session.IsDirty);
        Assert.False(autosave.HasPendingEdits);
        _now = _now.AddMinutes(5);
        Assert.False(await autosave.TickAsync());
    }

    [Fact]
    public async Task ASavedProjectAutosavesNextToItAndASaveDeletesTheAutosave()
    {
        _projectPath = Path.Combine(_root, "edit.cutlass");
        using var autosave = Create();
        Edit();
        _now = _now.AddSeconds(31);

        Assert.True(await autosave.TickAsync());
        var file = ProjectStore.AutosavePath(_projectPath);
        Assert.Equal(file, autosave.TargetPath);
        Assert.True(File.Exists(file));

        await _store.SaveAsync(_session.Current, _projectPath);
        _session.MarkSaved();
        await autosave.ClearAsync(includeProjectAutosave: true);

        Assert.False(File.Exists(file));
        Assert.False(autosave.HasPendingEdits);
    }

    [Fact]
    public async Task ACleanCloseDeletesOnlyWhatThisSessionWrote()
    {
        _projectPath = Path.Combine(_root, "edit.cutlass");
        var foreign = ProjectStore.AutosavePath(_projectPath);
        await File.WriteAllTextAsync(foreign, "an autosave of an earlier session");
        using var autosave = Create();

        await autosave.ClearAsync(includeProjectAutosave: false);
        Assert.True(File.Exists(foreign));

        Edit();
        _now = _now.AddMinutes(1);
        Assert.True(await autosave.TickAsync());
        await autosave.ClearAsync(includeProjectAutosave: false);
        Assert.False(File.Exists(foreign));
    }

    [Fact]
    public async Task ReplacingTheProjectDeletesTheOldAutosave()
    {
        using var autosave = Create();
        Edit();
        _now = _now.AddMinutes(1);
        Assert.True(await autosave.TickAsync());
        var file = autosave.TargetPath;
        Assert.True(File.Exists(file));

        _session.Replace(TimelineEdits.NewProject(new SequentialIdGenerator(100)));
        await WaitUntilAsync(() => !File.Exists(file));

        Assert.False(autosave.HasPendingEdits);
        Assert.NotEqual(file, autosave.TargetPath);
    }

    [Fact]
    public async Task AnAdoptedUntitledAutosaveIsOverwrittenThenDeletedOnClose()
    {
        Directory.CreateDirectory(UntitledFolder);
        var recovered = AutosaveRecovery.UntitledPath(UntitledFolder, "old");
        await _store.SaveAutosaveAsync(_session.Current, recovered, null);
        using var autosave = Create();
        _session.Replace(await _store.LoadAsync(recovered), markSaved: false);
        autosave.Adopt(recovered);

        Assert.Equal(recovered, autosave.TargetPath);
        Assert.True(autosave.HasPendingEdits);
        Edit();
        _now = _now.AddMinutes(1);
        Assert.True(await autosave.TickAsync());
        Assert.True(File.Exists(recovered));

        await autosave.ClearAsync(includeProjectAutosave: false);
        Assert.False(File.Exists(recovered));
    }

    [Fact]
    public void TheCrashPathWritesTheDirtyProjectSynchronously()
    {
        using var autosave = Create();
        Assert.False(autosave.TrySaveNowBlocking(TimeSpan.FromSeconds(5)));

        Edit();

        Assert.True(autosave.TrySaveNowBlocking(TimeSpan.FromSeconds(5)));
        Assert.True(File.Exists(autosave.TargetPath));
    }

    [Fact]
    public async Task StartRunsTheCheckOnTheTimerPeriod()
    {
        TimeSpan? period = null;
        Action? tick = null;
        var stopped = false;
        using var autosave = Create((p, a) =>
        {
            period = p;
            tick = a;
            return new Stopper(() => stopped = true);
        });

        autosave.Start();
        autosave.Start();
        Assert.Equal(AutosaveService.TickPeriod, period);

        Edit();
        _now = _now.AddMinutes(1);
        tick!();
        await WaitUntilAsync(() => autosave.WriteCount == 1);

        autosave.Stop();
        Assert.True(stopped);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private AutosaveService Create(Func<TimeSpan, Action, IDisposable>? timer = null)
    {
        var ids = 0;
        return new AutosaveService(
            _store,
            UntitledFolder,
            _session,
            () => _projectPath,
            () => _suppressed,
            () => _now,
            timer ?? ((_, _) => new Stopper(() => { })),
            () => "id" + (++ids).ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            Interval = Interval,
        };
    }

    private void Edit()
    {
        _inOut++;
        _session.Execute(p => TimelineEdits.SetInOut(p, _inOut, _inOut + 10));
    }

    private sealed class Stopper(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }
}
