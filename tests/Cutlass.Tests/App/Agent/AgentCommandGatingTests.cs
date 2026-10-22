using Avalonia.Input;
using Cutlass.App.Services;
using Cutlass.App.ViewModels;
using Cutlass.Core.Editing;
using Cutlass.Core.Session;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// AGENT_INTEGRATION 3.7: while an agent holds the project only <see cref="CommandIds.AllowedWhileAgentHolds"/> runs,
/// keys of gated commands are claimed and dropped with the toast, every editing surface turns read-only, and a write
/// that slips past the UI is refused by the session's gate with the throttled toast.
/// </summary>
public sealed class AgentCommandGatingTests : IDisposable
{
    private readonly AgentShellFixture _f = new();

    /// <summary>Removes the fixture's folders.</summary>
    public void Dispose() => _f.Dispose();

    [Fact]
    public void TheAllowedSetIsExactlyTheSpecList()
    {
        string[] expected =
        [
            CommandIds.PlayPause, CommandIds.PlayInToOut, CommandIds.ShuttleForward, CommandIds.Pause, CommandIds.ShuttleReverse, CommandIds.ToggleLoop,
            CommandIds.StepBackward, CommandIds.StepForward, CommandIds.StepBackward10, CommandIds.StepForward10,
            CommandIds.PreviousEdit, CommandIds.NextEdit, CommandIds.GoToStart, CommandIds.GoToEnd, CommandIds.GoToIn, CommandIds.GoToOut,
            CommandIds.NextMarker, CommandIds.PreviousMarker,
            CommandIds.ZoomIn, CommandIds.ZoomOut, CommandIds.ZoomToFit, CommandIds.ToggleGuides, CommandIds.ToggleProxies, CommandIds.ToggleSnap,
            CommandIds.ToolSelect, CommandIds.ToolRipple, CommandIds.ToolRoll, CommandIds.ToolSlip, CommandIds.ToolRazor,
            CommandIds.ShowTimelineMonitor, CommandIds.ShowSourceMonitor, CommandIds.MatchFrame,
            CommandIds.SelectAll, CommandIds.Copy, CommandIds.Escape,
            CommandIds.Save, CommandIds.SaveAs, CommandIds.Export, CommandIds.Settings, CommandIds.Quit,
            CommandIds.About, CommandIds.OpenLogFolder,
            CommandIds.AgentTakeOver, CommandIds.AgentPauseResume, CommandIds.AgentToggleFollow, CommandIds.ShowAgentPanel,
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), CommandIds.AllowedWhileAgentHolds.Order(StringComparer.Ordinal));
        Assert.Equal(
            new[] { CommandIds.ClearInOut, CommandIds.MarkClip, CommandIds.MarkIn, CommandIds.MarkOut },
            CommandIds.RoutedToSource.Order(StringComparer.Ordinal));
        foreach (var edit in new[] { CommandIds.Lift, CommandIds.Split, CommandIds.Undo, CommandIds.Redo, CommandIds.Insert, CommandIds.Paste, CommandIds.NewProject, CommandIds.OpenProject, CommandIds.Import, CommandIds.RelinkMedia, CommandIds.Speed, CommandIds.SequenceSettings })
        {
            Assert.DoesNotContain(edit, CommandIds.AllowedWhileAgentHolds);
        }
    }

    [Fact]
    public async Task GatedCommandsAreDisabledWhileHeldAndBackAfterTakeOver()
    {
        var vm = await OpenProjectAsync();
        vm.Timeline.Select(["c1"], additive: false);
        Assert.True(vm.Commands.CanExecute(CommandIds.Lift));

        _f.Grant();
        Assert.True(vm.Agent.IsLocked);
        Assert.False(vm.Commands.CanExecute(CommandIds.Lift));
        Assert.False(vm.Commands.CanExecute(CommandIds.Split));
        Assert.False(vm.Commands.CanExecute(CommandIds.MarkIn));
        Assert.False(vm.RunCommandCommand.CanExecute(CommandIds.Lift));
        Assert.True(vm.RunCommandCommand.CanExecute(CommandIds.ZoomIn));
        Assert.True(vm.Commands.CanExecute(CommandIds.ZoomIn));
        Assert.True(vm.Commands.CanExecute(CommandIds.GoToEnd));
        Assert.False(vm.NewCommand.CanExecute(null));
        Assert.False(vm.OpenCommand.CanExecute(null));
        Assert.False(vm.Timeline.UndoCommand.CanExecute(null));
        Assert.True(vm.Timeline.IsReadOnly);
        Assert.True(vm.Inspector.IsReadOnly);
        Assert.True(vm.MediaBin.IsReadOnly);
        Assert.True(vm.EffectsTab.IsReadOnly);
        Assert.False(vm.CanEditProject);
        Assert.False(vm.MediaBin.RemoveCommand.CanExecute(null));

        // TryExecute claims a gated id but does not run it.
        var before = vm.Session.Current;
        Assert.True(vm.Commands.TryExecute(CommandIds.Lift));
        Assert.Same(before, vm.Session.Current);

        // Take over: the UI is editable in the same turn.
        Assert.True(_f.Lock.Revoke(_f.Now));
        Assert.False(vm.Agent.IsLocked);
        Assert.False(vm.Timeline.IsReadOnly);
        Assert.False(vm.Inspector.IsReadOnly);
        Assert.True(vm.Commands.CanExecute(CommandIds.Lift));
        Assert.True(vm.NewCommand.CanExecute(null));
        Assert.True(vm.CanEditProject);
    }

    [Fact]
    public async Task GatedKeysAreClaimedDroppedAndExplained()
    {
        var vm = await OpenProjectAsync();
        vm.Timeline.Select(["c1"], additive: false);
        _f.Grant();
        var blocked = new List<string>();
        vm.Keys.CommandBlocked += (_, id) => blocked.Add(id);
        var before = vm.Session.Current;

        Assert.True(vm.Keys.HandleKeyDown(Key.Delete, KeyModifiers.None, textInputFocused: false));
        Assert.True(vm.Keys.HandleKeyDown(Key.Z, KeyModifiers.Control, textInputFocused: false));
        Assert.True(vm.Keys.HandleKeyDown(Key.K, KeyModifiers.Control, textInputFocused: false));

        Assert.Same(before, vm.Session.Current);
        Assert.Equal([CommandIds.Lift, CommandIds.Undo, CommandIds.Split], blocked);
        Assert.Equal(MainWindowViewModel.AgentBlockedMessage, vm.ToastText);
        Assert.Equal(0, vm.AgentWriteRefusedCount);

        // Allowed keys still work: the playhead steps and the zoom changes.
        var playhead = vm.Timeline.Playhead;
        Assert.True(vm.Keys.HandleKeyDown(Key.Right, KeyModifiers.None, textInputFocused: false));
        Assert.Equal(playhead + 1, vm.Timeline.Playhead);
        Assert.True(vm.Keys.HandleKeyDown(Key.B, KeyModifiers.None, textInputFocused: false));
        Assert.Equal(EditTool.Ripple, vm.Timeline.Tool);
    }

    [Fact]
    public void AKeyBindingServiceWithAGateDropsTheChordAndRaisesBlocked()
    {
        var registry = new CommandRegistry();
        var ran = 0;
        registry.Register(CommandIds.Lift, () => ran++);
        registry.Register(CommandIds.ZoomIn, () => ran += 10);
        var keys = new KeyBindingService(registry, DefaultKeymap.Build(), () => false, metaIsControl: false);
        var blocked = new List<string>();
        keys.CommandBlocked += (_, id) => blocked.Add(id);
        registry.Gate = id => CommandIds.AllowedWhileAgentHolds.Contains(id);

        Assert.True(keys.HandleKeyDown(Key.Delete, KeyModifiers.None, false));
        Assert.True(keys.HandleKeyDown(Key.OemPlus, KeyModifiers.None, false));
        Assert.Equal(10, ran);
        Assert.Equal([CommandIds.Lift], blocked);
        Assert.False(registry.CanExecute(CommandIds.Lift));

        registry.Gate = null;
        Assert.True(keys.HandleKeyDown(Key.Delete, KeyModifiers.None, false));
        Assert.Equal(11, ran);
    }

    [Fact]
    public async Task ARefusedWriteShowsTheToastAtMostEveryThreeSeconds()
    {
        var vm = await OpenProjectAsync();
        _f.Grant();
        var before = vm.Session.Current;

        // A user write that slipped past the UI (here: straight through the timeline) is refused by the gate.
        var result = vm.Timeline.Run(p => TimelineEdits.Lift(p, ["c1"]));
        Assert.Same(before, vm.Session.Current);
        Assert.Contains(ProjectSession.RefusedMessage, result.Warnings);
        Assert.Equal(1, vm.AgentWriteRefusedCount);
        Assert.Equal(MainWindowViewModel.AgentBlockedMessage, vm.ToastText);

        // A second refusal right after does not toast again.
        vm.ShowToast("something else");
        vm.Timeline.Run(p => TimelineEdits.Lift(p, ["c1"]));
        Assert.Equal(2, vm.AgentWriteRefusedCount);
        Assert.Equal("something else", vm.ToastText);

        // The agent itself writes through.
        using (vm.Session.WriteAs(SessionWriter.Agent))
        {
            vm.Session.Execute(p => TimelineEdits.Lift(p, ["c1"]));
        }

        Assert.NotSame(before, vm.Session.Current);
    }

    [Fact]
    public async Task UndoLabelsOfAgentBatchesShowInTheEditMenu()
    {
        var vm = await OpenProjectAsync();
        _f.Grant();
        using (vm.Session.WriteAs(SessionWriter.Agent))
        {
            vm.Session.BeginGroup("Agent: Remove silences");
            vm.Session.Execute(p => TimelineEdits.Lift(p, ["c1"]));
            Assert.Equal("_Undo", vm.UndoMenuText.Split(' ')[0]);
            vm.Session.EndGroup();
        }

        Assert.Equal("_Undo Agent: Remove silences", vm.UndoMenuText);
        Assert.Equal("_Undo Agent: A__B", MainWindowViewModel.HistoryMenuText("Undo", "Agent: A_B"));
        _f.Lock.Revoke(_f.Now);
        Assert.True(vm.Timeline.UndoCommand.CanExecute(null));
        vm.Timeline.UndoCommand.Execute(null);
        Assert.Equal("_Redo Agent: Remove silences", vm.RedoMenuText);
    }

    [Fact]
    public void ThePortIgnoredWithoutAgentServerAndParsedWithIt()
    {
        var plain = CommandLineOptions.Parse(["--port", "47399"]);
        Assert.False(plain.AgentServer);
        Assert.True(plain.PortIgnored);

        var both = CommandLineOptions.Parse(["--agent-server", "--port", "47399", "--import", "a.mp4", "b.mp4", "--smoke-script", "m9.smoke"]);
        Assert.True(both.AgentServer);
        Assert.Equal(47399, both.AgentPort);
        Assert.False(both.PortIgnored);
        Assert.Equal(["a.mp4", "b.mp4"], both.ImportPaths);
        Assert.Equal("m9.smoke", both.SmokeScriptPath);

        var bad = CommandLineOptions.Parse(["--agent-server", "--port", "80"]);
        Assert.Null(bad.AgentPort);
        Assert.Contains("--port", bad.Unknown);
    }

    [Fact]
    public async Task PreparingForAnAgentClosesTheInlineEditorsBeforeTheGrant()
    {
        // AGENT_INTEGRATION 3.2: the marker editor and the rename box are canvas overlays that clearing focus alone
        // leaves open, so the grant asks the timeline view to commit and close them, and clears focus after.
        var vm = await OpenProjectAsync();
        var order = new List<string>();
        vm.Timeline.InlineEditorsCloseRequested += (_, _) => order.Add("editors");
        vm.FocusResetRequested += (_, _) => order.Add("focus");

        vm.PrepareForAgent();

        Assert.Equal(["editors", "focus"], order);
    }

    private async Task<MainWindowViewModel> OpenProjectAsync()
    {
        var vm = _f.Vm;
        await vm.Welcome.NewProjectCommand.ExecuteAsync(null);
        vm.Session.Replace(new ProjectBuilder().With(ProjectBuilder.V1, V("c1", 0, 50), V("c2", 50, 50)).Build());
        return vm;
    }
}
