using Avalonia.Input;

namespace Cutlass.App.Services.Commands;

/// <summary>
/// A key with its modifiers, written like <c>Ctrl+Shift+K</c>. Keys are named by their Avalonia <see cref="Key"/>
/// value (<c>OemComma</c>, <c>Back</c>, <c>Add</c>) so a binding works on QWERTY and QWERTZ alike (DESIGN 7).
/// </summary>
/// <param name="Key">The key.</param>
/// <param name="Modifiers">Control, Shift, Alt and Meta.</param>
public readonly record struct KeyChord(Key Key, KeyModifiers Modifiers)
{
    /// <summary>Parses a chord such as <c>Ctrl+Shift+K</c>, <c>Shift+Delete</c> or <c>OemPlus</c>.</summary>
    /// <param name="text">The chord text. Modifiers are <c>Ctrl</c>, <c>Shift</c>, <c>Alt</c> and <c>Meta</c>
    /// (also <c>Control</c> and <c>Cmd</c>), case-insensitive, in any order, before the key.</param>
    /// <param name="chord">The parsed chord.</param>
    /// <returns>True when the text is a valid chord.</returns>
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(p => p.Length == 0))
        {
            return false;
        }

        var modifiers = KeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            KeyModifiers flag = parts[i].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => KeyModifiers.Control,
                "SHIFT" => KeyModifiers.Shift,
                "ALT" => KeyModifiers.Alt,
                "META" or "CMD" or "WIN" => KeyModifiers.Meta,
                _ => (KeyModifiers)(-1),
            };
            if ((int)flag == -1 || (modifiers & flag) != 0)
            {
                return false;
            }

            modifiers |= flag;
        }

        var keyName = parts[^1];
        // Enum.TryParse also accepts numbers ("65"), which are never key names in a keymap.
        if (keyName.All(char.IsAsciiDigit) && keyName.Length > 1)
        {
            return false;
        }

        if (keyName.Length == 1 && char.IsAsciiDigit(keyName[0]))
        {
            keyName = "D" + keyName;
        }

        if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key) || key == Key.None || !Enum.IsDefined(key))
        {
            return false;
        }

        chord = new KeyChord(key, modifiers);
        return true;
    }

    /// <summary>Parses a chord, throwing on invalid text.</summary>
    /// <param name="text">The chord text.</param>
    /// <returns>The chord.</returns>
    /// <exception cref="FormatException">When the text is not a valid chord.</exception>
    public static KeyChord Parse(string text) =>
        TryParse(text, out var chord) ? chord : throw new FormatException($"'{text}' is not a key chord.");

    /// <summary>The chord with Meta (Cmd) treated as Control, as on macOS (DESIGN 7).</summary>
    /// <returns>The normalized chord.</returns>
    public KeyChord MetaAsControl() =>
        (Modifiers & KeyModifiers.Meta) != 0
            ? new KeyChord(Key, (Modifiers & ~KeyModifiers.Meta) | KeyModifiers.Control)
            : this;

    /// <summary>Formats the chord in the parse syntax, modifiers in the order Ctrl, Shift, Alt, Meta.</summary>
    /// <returns>For example <c>Ctrl+Shift+K</c>.</returns>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & KeyModifiers.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((Modifiers & KeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((Modifiers & KeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((Modifiers & KeyModifiers.Meta) != 0)
        {
            parts.Add("Meta");
        }

        parts.Add(Key.ToString());
        return string.Join('+', parts);
    }
}
