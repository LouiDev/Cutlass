using Cutlass.Media.Platform;

namespace Cutlass.Tests.Media;

/// <summary>
/// The startup sweep's decision per record file (TECH_BRIEF 5.4), with fake process facts: a live owner keeps its
/// file, a gone owner's children are killed, and files from another boot are only deleted.
/// </summary>
public sealed class ChildProcessSweepDecisionTests
{
    private const int Self = 100;
    private const long SelfStart = 5_000_000;
    private const long OwnerStart = 4_000_000;
    private const string Boot = "boot-a";

    private static readonly ChildProcessRecord Child = new(300, 4_100_000, "/usr/bin/ffmpeg");

    [Fact]
    public void ALiveOwnersFileIsKept()
    {
        var file = new ChildProcessFile(200, OwnerStart, Boot, [Child]);

        var action = Decide(file, (200, OwnerStart + 1000, "cutlass"));

        Assert.Equal(OrphanSweepAction.Keep, action);
    }

    [Fact]
    public void AGoneOwnersChildrenAreKilled()
    {
        var file = new ChildProcessFile(200, OwnerStart, Boot, [Child]);

        Assert.Equal(OrphanSweepAction.KillAndDelete, Decide(file));
    }

    [Fact]
    public void AReusedOwnerPidCountsAsGone()
    {
        var file = new ChildProcessFile(200, OwnerStart, Boot, [Child]);
        var later = OwnerStart + ChildProcessRegistry.StartTimeTolerance.Ticks + 1;

        Assert.Equal(OrphanSweepAction.KillAndDelete, Decide(file, (200, later, "bash")));
    }

    [Fact]
    public void AFileFromAnotherBootIsDeletedWithoutKilling()
    {
        // After a reboot the owner's pid may even be alive with a matching start time by chance.
        var file = new ChildProcessFile(200, OwnerStart, "boot-old", [Child]);

        Assert.Equal(OrphanSweepAction.DeleteOnly, Decide(file, (200, OwnerStart, "cutlass")));
    }

    [Fact]
    public void ThisInstancesOwnFileIsKept()
    {
        var file = new ChildProcessFile(Self, SelfStart, Boot, [Child]);

        Assert.Equal(OrphanSweepAction.Keep, Decide(file));
    }

    [Fact]
    public void WithoutBootIdsOnBothSidesOnlyTheOwnerDecides()
    {
        var file = new ChildProcessFile(200, OwnerStart, null, [Child]);

        Assert.Equal(OrphanSweepAction.Keep, ChildProcessRegistry.Decide(file, Self, SelfStart, null, pid => pid == 200 ? (OwnerStart, "cutlass") : null));
        Assert.Equal(OrphanSweepAction.KillAndDelete, ChildProcessRegistry.Decide(file, Self, SelfStart, null, _ => null));
        Assert.Equal(OrphanSweepAction.DeleteOnly, ChildProcessRegistry.Decide(file, Self, SelfStart, Boot, _ => null));
    }

    [Fact]
    public void RecordFileNamesCarryTheOwnerIdentity()
    {
        Assert.Equal("pids-4242-638900000000000000.json", ChildProcessRegistry.FileNameFor(4242, 638_900_000_000_000_000));
    }

    private static OrphanSweepAction Decide(ChildProcessFile file, params (int Pid, long Start, string Name)[] live) =>
        ChildProcessRegistry.Decide(
            file,
            Self,
            SelfStart,
            Boot,
            pid => live.Where(l => l.Pid == pid).Select(l => ((long, string)?)(l.Start, l.Name)).FirstOrDefault());
}
