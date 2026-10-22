using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Cutlass.App.Services;

/// <summary>
/// Routes key presses to commands (TECH_BRIEF 10.2): it tunnels <c>KeyDown</c> on the main window, looks the chord
/// up in the keymap and runs the registered command. While a text input has focus only the DESIGN 7 pass-through
/// chords reach the app, and Esc goes to the text input first (it reverts or closes its editor) and runs the app's
/// Escape command only when the input left it unhandled. While a timeline drag runs only Esc (to the timeline),
/// snapping and zoom work; every other bound chord is swallowed so it cannot edit the project under the gesture.
/// Holding K and tapping J or L steps one frame back or forward. While an agent holds the project (AGENT_INTEGRATION
/// 3.7) a chord whose command the registry's <see cref="CommandRegistry.Gate"/> refuses is claimed and dropped the
/// same way, and <see cref="CommandBlocked"/> lets the window explain why (a toast).
/// </summary>
public sealed class KeyBindingService
{
    private const KeyModifiers ChordModifiers = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta;

    private static readonly KeyChord EscapeChord = new(Key.Escape, KeyModifiers.None);

    // The commands that may run during a timeline drag: they change only the view or the drag's own snapping.
    private static readonly HashSet<string> DuringGesture = new(StringComparer.Ordinal)
    {
        CommandIds.ToggleSnap, CommandIds.ZoomIn, CommandIds.ZoomOut, CommandIds.ZoomToFit,
    };

    private readonly CommandRegistry _registry;
    private readonly IReadOnlyDictionary<KeyChord, string> _keymap;
    private readonly Func<bool> _isGestureActive;
    private readonly bool _metaIsControl;
    private TopLevel? _attached;

    /// <summary>Creates the service.</summary>
    /// <param name="registry">The commands.</param>
    /// <param name="keymap">Command ids by chord, normally <see cref="DefaultKeymap.Build()"/>.</param>
    /// <param name="isGestureActive">True while a timeline drag runs; Esc then goes to the timeline (cancel).</param>
    /// <param name="metaIsControl">True on macOS, where Cmd acts as Ctrl.</param>
    public KeyBindingService(
        CommandRegistry registry,
        IReadOnlyDictionary<KeyChord, string> keymap,
        Func<bool> isGestureActive,
        bool metaIsControl)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(keymap);
        ArgumentNullException.ThrowIfNull(isGestureActive);
        _registry = registry;
        _keymap = keymap;
        _isGestureActive = isGestureActive;
        _metaIsControl = metaIsControl;
    }

    /// <summary>Raised with the command id when a chord was dropped because the registry's gate refused its command.</summary>
    public event EventHandler<string>? CommandBlocked;

    /// <summary>True while K is held (for the K plus J/L chords).</summary>
    public bool IsKHeld { get; private set; }

    /// <summary>Handles a key press.</summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifiers.</param>
    /// <param name="textInputFocused">True when a text input has focus (focus rule).</param>
    /// <returns>True when the key ran (or was claimed by) a command.</returns>
    public bool HandleKeyDown(Key key, KeyModifiers modifiers, bool textInputFocused)
    {
        var chord = ToChord(key, modifiers);
        if (textInputFocused && (chord == EscapeChord || !DefaultKeymap.TextInputPassThrough.Contains(chord)))
        {
            // Esc goes to the text input first (revert, close the editor); HandleUnclaimedKeyDown runs it afterwards
            // when the input did not use it.
            return false;
        }

        var gesture = _isGestureActive();
        if (chord.Modifiers == KeyModifiers.None && !gesture)
        {
            if (key == Key.K)
            {
                IsKHeld = true;
            }
            else if (IsKHeld && key is Key.J or Key.L)
            {
                return _registry.TryExecute(key == Key.J ? CommandIds.StepBackward : CommandIds.StepForward);
            }
        }

        if (!_keymap.TryGetValue(chord, out var id))
        {
            return false;
        }

        if (gesture)
        {
            if (id == CommandIds.Escape)
            {
                // Let the timeline control cancel its drag.
                return false;
            }

            if (!DuringGesture.Contains(id))
            {
                // An edit, playback or file command would cancel the drag and edit the project under it: swallow it.
                return true;
            }
        }

        if (!_registry.IsAllowed(id))
        {
            // An agent holds the project: claim the chord so nothing else (the focused timeline) edits, and say why.
            CommandBlocked?.Invoke(this, id);
            return true;
        }

        return _registry.TryExecute(id);
    }

    /// <summary>
    /// Handles a key press that bubbled up unhandled. Only Esc in a text input runs here: the input had the first
    /// chance (DESIGN 7 focus rule), and when it did not use the key the app's Escape command runs.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifiers.</param>
    /// <param name="textInputFocused">True when a text input has focus.</param>
    /// <returns>True when the key ran (or was claimed by) a command.</returns>
    public bool HandleUnclaimedKeyDown(Key key, KeyModifiers modifiers, bool textInputFocused)
    {
        if (!textInputFocused || ToChord(key, modifiers) != EscapeChord || !_keymap.TryGetValue(EscapeChord, out var id))
        {
            return false;
        }

        return !_isGestureActive() && _registry.TryExecute(id);
    }

    /// <summary>Handles a key release (tracks K).</summary>
    /// <param name="key">The key.</param>
    public void HandleKeyUp(Key key)
    {
        if (key == Key.K)
        {
            IsKHeld = false;
        }
    }

    /// <summary>Forgets held keys, for example when the window loses focus.</summary>
    public void Reset() => IsKHeld = false;

    /// <summary>Starts routing the key events of a window.</summary>
    /// <param name="topLevel">The main window.</param>
    public void Attach(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        Detach();
        _attached = topLevel;
        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDownBubbled, RoutingStrategies.Bubble);
        topLevel.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        if (topLevel is WindowBase window)
        {
            window.Deactivated += OnDeactivated;
        }
    }

    /// <summary>Stops routing key events.</summary>
    public void Detach()
    {
        if (_attached is null)
        {
            return;
        }

        _attached.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _attached.RemoveHandler(InputElement.KeyDownEvent, OnKeyDownBubbled);
        _attached.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        if (_attached is WindowBase window)
        {
            window.Deactivated -= OnDeactivated;
        }

        _attached = null;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleKeyDown(e.Key, e.KeyModifiers, IsTextInputFocused()))
        {
            e.Handled = true;
        }
    }

    private void OnKeyDownBubbled(object? sender, KeyEventArgs e)
    {
        if (!e.Handled && HandleUnclaimedKeyDown(e.Key, e.KeyModifiers, IsTextInputFocused()))
        {
            e.Handled = true;
        }
    }

    private bool IsTextInputFocused() => _attached?.FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox or NumericUpDown;

    private KeyChord ToChord(Key key, KeyModifiers modifiers)
    {
        var chord = new KeyChord(key, modifiers & ChordModifiers);
        return _metaIsControl ? chord.MetaAsControl() : chord;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e) => HandleKeyUp(e.Key);

    private void OnDeactivated(object? sender, EventArgs e) => Reset();
}
