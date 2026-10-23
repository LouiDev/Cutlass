using System.Collections.Frozen;
using Avalonia.Input;

namespace Cutlass.App.Services.Commands;

/// <summary>
/// The static default keymap of DESIGN 7. Non-letter keys use Avalonia <see cref="Key"/> names so they work on any
/// keyboard layout. The K plus J/L chords are not entries: <see cref="KeyBindingService"/> detects them.
/// </summary>
public static class DefaultKeymap
{
    /// <summary>Every default binding, in the order of the DESIGN 7 table.</summary>
    public static IReadOnlyList<KeymapEntry> Entries { get; } =
    [
        new("Space", CommandIds.PlayPause),
        new("Shift+Space", CommandIds.PlayInToOut),
        new("L", CommandIds.ShuttleForward),
        new("K", CommandIds.Pause),
        new("J", CommandIds.ShuttleReverse),
        new("Left", CommandIds.StepBackward),
        new("Right", CommandIds.StepForward),
        new("Shift+Left", CommandIds.StepBackward10),
        new("Shift+Right", CommandIds.StepForward10),
        new("Up", CommandIds.PreviousEdit),
        new("Down", CommandIds.NextEdit),
        new("Home", CommandIds.GoToStart),
        new("End", CommandIds.GoToEnd),
        new("I", CommandIds.MarkIn),
        new("O", CommandIds.MarkOut),
        new("Ctrl+Shift+X", CommandIds.ClearInOut),
        new("X", CommandIds.MarkClip),
        new("Shift+I", CommandIds.GoToIn),
        new("Shift+O", CommandIds.GoToOut),
        new("M", CommandIds.AddMarker),
        new("Shift+M", CommandIds.NextMarker),
        new("Ctrl+Shift+M", CommandIds.PreviousMarker),
        new("V", CommandIds.ToolSelect),
        new("B", CommandIds.ToolRipple),
        new("N", CommandIds.ToolRoll),
        new("Y", CommandIds.ToolSlip),
        new("C", CommandIds.ToolRazor),
        new("S", CommandIds.ToggleSnap),
        new("Ctrl+K", CommandIds.Split),
        new("Ctrl+Shift+K", CommandIds.SplitAll),
        new("Delete", CommandIds.Lift),
        new("Back", CommandIds.Lift),
        new("Shift+Delete", CommandIds.RippleDelete),
        new("Q", CommandIds.RippleTrimStart),
        new("W", CommandIds.RippleTrimEnd),
        new("Alt+Left", CommandIds.NudgeLeft),
        new("Alt+Right", CommandIds.NudgeRight),
        new("OemComma", CommandIds.Insert),
        new("OemPeriod", CommandIds.Overwrite),
        new("F", CommandIds.MatchFrame),
        new("Ctrl+D", CommandIds.AddTransition),
        new("Ctrl+Shift+D", CommandIds.AddAudioCrossfade),
        new("Ctrl+T", CommandIds.NewTitle),
        new("Ctrl+R", CommandIds.Speed),
        new("Ctrl+L", CommandIds.LinkUnlink),
        new("Shift+E", CommandIds.ToggleEnabled),
        new("Ctrl+C", CommandIds.Copy),
        new("Ctrl+X", CommandIds.Cut),
        new("Ctrl+V", CommandIds.Paste),
        new("Ctrl+Shift+V", CommandIds.PasteInsert),
        new("Ctrl+A", CommandIds.SelectAll),
        new("Escape", CommandIds.Escape),
        new("Ctrl+Z", CommandIds.Undo),
        new("Ctrl+Shift+Z", CommandIds.Redo),
        new("Ctrl+Y", CommandIds.Redo),
        new("OemPlus", CommandIds.ZoomIn),
        new("Add", CommandIds.ZoomIn),
        new("OemMinus", CommandIds.ZoomOut),
        new("Subtract", CommandIds.ZoomOut),
        new("Shift+Z", CommandIds.ZoomToFit),
        new("G", CommandIds.ToggleGuides),
        new("P", CommandIds.ToggleProxies),
        new("Ctrl+Shift+L", CommandIds.ToggleLoop),
        new("Ctrl+OemComma", CommandIds.Settings),
        new("Ctrl+N", CommandIds.NewProject),
        new("Ctrl+O", CommandIds.OpenProject),
        new("Ctrl+S", CommandIds.Save),
        new("Ctrl+Shift+S", CommandIds.SaveAs),
        new("Ctrl+I", CommandIds.Import),
        new("Ctrl+E", CommandIds.Export),
        new("Ctrl+Q", CommandIds.Quit),
        new("F9", CommandIds.AgentToggleServer),
    ];

    /// <summary>
    /// The chords that still reach the app while a text input has focus (DESIGN 7 focus rule): Ctrl+S, Ctrl+E,
    /// Ctrl+N, Ctrl+O and Esc.
    /// </summary>
    public static IReadOnlySet<KeyChord> TextInputPassThrough { get; } = new HashSet<KeyChord>
    {
        new(Key.S, KeyModifiers.Control),
        new(Key.E, KeyModifiers.Control),
        new(Key.N, KeyModifiers.Control),
        new(Key.O, KeyModifiers.Control),
        new(Key.Escape, KeyModifiers.None),
    }.ToFrozenSet();

    /// <summary>Parses a keymap into a lookup table.</summary>
    /// <param name="entries">The entries.</param>
    /// <returns>Command ids by chord.</returns>
    /// <exception cref="FormatException">When a chord does not parse.</exception>
    /// <exception cref="InvalidOperationException">When two entries bind the same chord.</exception>
    public static IReadOnlyDictionary<KeyChord, string> Build(IEnumerable<KeymapEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var map = new Dictionary<KeyChord, string>();
        foreach (var entry in entries)
        {
            var chord = KeyChord.Parse(entry.Chord);
            if (!map.TryAdd(chord, entry.CommandId))
            {
                throw new InvalidOperationException($"{chord} is bound to both {map[chord]} and {entry.CommandId}.");
            }
        }

        return map.ToFrozenDictionary();
    }

    /// <summary>The parsed default keymap.</summary>
    /// <returns>Command ids by chord.</returns>
    public static IReadOnlyDictionary<KeyChord, string> Build() => Build(Entries);
}
