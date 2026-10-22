using Avalonia.Input;
using Cutlass.App.Services;

namespace Cutlass.Tests.App.Shell;

public sealed class KeymapTests
{
    [Theory]
    [InlineData("Ctrl+Shift+K", Key.K, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData("shift+delete", Key.Delete, KeyModifiers.Shift)]
    [InlineData("OemComma", Key.OemComma, KeyModifiers.None)]
    [InlineData("Ctrl+OemComma", Key.OemComma, KeyModifiers.Control)]
    [InlineData("Alt+Left", Key.Left, KeyModifiers.Alt)]
    [InlineData("Back", Key.Back, KeyModifiers.None)]
    [InlineData("Cmd+S", Key.S, KeyModifiers.Meta)]
    [InlineData("Ctrl+1", Key.D1, KeyModifiers.Control)]
    public void ParsesChords(string text, Key key, KeyModifiers modifiers)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(new KeyChord(key, modifiers), chord);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+K")]
    [InlineData("Ctrl+Ctrl+K")]
    [InlineData("NotAKey")]
    [InlineData("65")]
    [InlineData("+")]
    public void RejectsInvalidChords(string text) => Assert.False(KeyChord.TryParse(text, out _));

    [Fact]
    public void ToStringRoundTrips()
    {
        var chord = KeyChord.Parse("shift+ctrl+z");
        Assert.Equal("Ctrl+Shift+Z", chord.ToString());
        Assert.Equal(chord, KeyChord.Parse(chord.ToString()));
    }

    [Fact]
    public void DefaultKeymapHasNoConflictsAndKnownIds()
    {
        var map = DefaultKeymap.Build();
        Assert.Equal(DefaultKeymap.Entries.Count, map.Count);
        var known = typeof(CommandIds).GetFields().Select(f => (string)f.GetValue(null)!).ToHashSet();
        Assert.All(DefaultKeymap.Entries, e => Assert.Contains(e.CommandId, known));
        Assert.Equal(CommandIds.Redo, map[KeyChord.Parse("Ctrl+Y")]);
        Assert.Equal(CommandIds.Redo, map[KeyChord.Parse("Ctrl+Shift+Z")]);
        Assert.Equal(CommandIds.Lift, map[KeyChord.Parse("Back")]);
        Assert.Equal(CommandIds.ZoomIn, map[KeyChord.Parse("Add")]);
    }

    [Fact]
    public void DuplicateChordsAreRejected() =>
        Assert.Throws<InvalidOperationException>(() => DefaultKeymap.Build([new("Ctrl+K", "a"), new("ctrl+k", "b")]));

    [Fact]
    public void RunsTheBoundCommandAndIgnoresUnregisteredOnes()
    {
        var (service, log) = Service();
        Assert.True(service.HandleKeyDown(Key.K, KeyModifiers.Control, textInputFocused: false));
        Assert.Equal([CommandIds.Split], log);
        // Ctrl+D (transitions) is in the keymap but not registered yet: the key falls through.
        Assert.False(service.HandleKeyDown(Key.D, KeyModifiers.Control, textInputFocused: false));
    }

    [Fact]
    public void TextInputsKeepTheirKeysExceptThePassThroughChords()
    {
        var (service, log) = Service();
        Assert.False(service.HandleKeyDown(Key.Left, KeyModifiers.None, textInputFocused: true));
        Assert.False(service.HandleKeyDown(Key.K, KeyModifiers.Control, textInputFocused: true));
        Assert.True(service.HandleKeyDown(Key.S, KeyModifiers.Control, textInputFocused: true));
        Assert.Equal([CommandIds.Save], log);
    }

    [Fact]
    public void EscapeInATextInputGoesToTheInputFirst()
    {
        var (service, log) = Service();

        // The tunnel leaves Esc to the focused text input (revert, close its editor)...
        Assert.False(service.HandleKeyDown(Key.Escape, KeyModifiers.None, textInputFocused: true));
        Assert.Empty(log);

        // ...and when it bubbles back unhandled, the app's Escape runs (DESIGN 7 focus rule).
        Assert.True(service.HandleUnclaimedKeyDown(Key.Escape, KeyModifiers.None, textInputFocused: true));
        Assert.Equal([CommandIds.Escape], log);

        // Outside text inputs the tunnel already ran it; other keys never run on the way back.
        Assert.False(service.HandleUnclaimedKeyDown(Key.Escape, KeyModifiers.None, textInputFocused: false));
        Assert.False(service.HandleUnclaimedKeyDown(Key.S, KeyModifiers.Control, textInputFocused: true));
        Assert.Equal([CommandIds.Escape], log);
    }

    [Fact]
    public void ADragLetsOnlySnappingAndZoomThroughAndSwallowsTheRest()
    {
        var dragging = true;
        var (service, log) = Service(() => dragging);

        // Editing, undo, transport and file shortcuts are claimed but do not run (they would cancel the drag).
        Assert.True(service.HandleKeyDown(Key.K, KeyModifiers.Control, false));
        Assert.True(service.HandleKeyDown(Key.Z, KeyModifiers.Control, false));
        Assert.True(service.HandleKeyDown(Key.S, KeyModifiers.Control, false));
        Assert.True(service.HandleKeyDown(Key.Space, KeyModifiers.None, false));
        Assert.Empty(log);

        // Snapping and zoom run; Esc goes to the timeline; plain modifier keys are not claimed.
        Assert.True(service.HandleKeyDown(Key.S, KeyModifiers.None, false));
        Assert.True(service.HandleKeyDown(Key.OemPlus, KeyModifiers.None, false));
        Assert.False(service.HandleKeyDown(Key.Escape, KeyModifiers.None, false));
        Assert.False(service.HandleKeyDown(Key.LeftCtrl, KeyModifiers.Control, false));
        Assert.Equal([CommandIds.ToggleSnap, CommandIds.ZoomIn], log);

        // K held with J or L does not step during the drag either.
        service.HandleKeyDown(Key.K, KeyModifiers.None, false);
        service.HandleKeyDown(Key.L, KeyModifiers.None, false);
        Assert.Equal([CommandIds.ToggleSnap, CommandIds.ZoomIn], log);

        dragging = false;
        Assert.True(service.HandleKeyDown(Key.K, KeyModifiers.Control, false));
        Assert.Equal([CommandIds.ToggleSnap, CommandIds.ZoomIn, CommandIds.Split], log);
    }

    [Fact]
    public void KHeldWithJOrLStepsOneFrame()
    {
        var (service, log) = Service();
        service.HandleKeyDown(Key.K, KeyModifiers.None, false);
        Assert.True(service.IsKHeld);
        service.HandleKeyDown(Key.J, KeyModifiers.None, false);
        service.HandleKeyDown(Key.L, KeyModifiers.None, false);
        service.HandleKeyUp(Key.K);
        service.HandleKeyDown(Key.L, KeyModifiers.None, false);
        Assert.Equal([CommandIds.Pause, CommandIds.StepBackward, CommandIds.StepForward, CommandIds.ShuttleForward], log);
    }

    [Fact]
    public void EscapeGoesToTheTimelineDuringADrag()
    {
        var dragging = true;
        var (service, log) = Service(() => dragging);
        Assert.False(service.HandleKeyDown(Key.Escape, KeyModifiers.None, false));
        dragging = false;
        Assert.True(service.HandleKeyDown(Key.Escape, KeyModifiers.None, false));
        Assert.Equal([CommandIds.Escape], log);
    }

    [Fact]
    public void MetaActsAsControlOnMac()
    {
        var log = new List<string>();
        var registry = Registry(log);
        var mac = new KeyBindingService(registry, DefaultKeymap.Build(), () => false, metaIsControl: true);
        Assert.True(mac.HandleKeyDown(Key.S, KeyModifiers.Meta, false));
        Assert.Equal([CommandIds.Save], log);
    }

    [Fact]
    public void DisabledCommandsClaimTheKeyWithoutRunning()
    {
        var registry = new CommandRegistry();
        var ran = false;
        registry.Register(CommandIds.Export, () => ran = true, () => false);
        Assert.True(registry.TryExecute(CommandIds.Export));
        Assert.False(ran);
        Assert.False(registry.CanExecute(CommandIds.Export));
        Assert.False(registry.TryExecute(CommandIds.Import));
    }

    private static CommandRegistry Registry(List<string> log)
    {
        var registry = new CommandRegistry();
        foreach (var id in new[]
                 {
                     CommandIds.Split, CommandIds.Save, CommandIds.Escape, CommandIds.Pause, CommandIds.StepBackward,
                     CommandIds.StepForward, CommandIds.ShuttleForward, CommandIds.ToggleSnap, CommandIds.ZoomIn,
                     CommandIds.Undo, CommandIds.PlayPause,
                 })
        {
            registry.Register(id, () => log.Add(id));
        }

        return registry;
    }

    private static (KeyBindingService Service, List<string> Log) Service(Func<bool>? dragging = null)
    {
        var log = new List<string>();
        return (new KeyBindingService(Registry(log), DefaultKeymap.Build(), dragging ?? (() => false), metaIsControl: false), log);
    }
}
