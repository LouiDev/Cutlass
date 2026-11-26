using System.Text.RegularExpressions;
using Avalonia.Input;
using Cutlass.App;
using Cutlass.App.Services.Commands;
using Cutlass.App.ViewModels;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;
using Cutlass.Tests.Compiler;

namespace Cutlass.Tests.App.Shell;

/// <summary>
/// The DESIGN 7 shortcut table is the contract: these tests read it from <c>docs/DESIGN.md</c> at test time, so a new
/// row or a changed chord fails here until the keymap and the main window's command registry follow.
/// </summary>
public sealed partial class KeymapCompletenessTests : IDisposable
{
    // Commands of later milestones that DESIGN 7 already lists; their keys fall through until they exist. Since M7
    // (the settings dialog, Ctrl+,) every listed command exists.
    private static readonly HashSet<string> Deferred = new(StringComparer.Ordinal);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cutlass-keymap-" + Guid.NewGuid().ToString("N"));

    /// <summary>Removes the temporary settings folder.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written.
        }
    }

    [Fact]
    public void TheTableParses()
    {
        var chords = DesignChords();
        Assert.True(chords.Count >= 70, "DESIGN 7 lists " + chords.Count + " chords");
        Assert.Contains(KeyChord.Parse("Shift+Right"), chords);
        Assert.Contains(KeyChord.Parse("Alt+Right"), chords);
        Assert.Contains(KeyChord.Parse("OemComma"), chords);
        Assert.Contains(KeyChord.Parse("Ctrl+OemComma"), chords);
        Assert.Contains(KeyChord.Parse("Back"), chords);
        Assert.Contains(KeyChord.Parse("Escape"), chords);
        Assert.Contains(KeyChord.Parse("Subtract"), chords);
        Assert.Contains(KeyChord.Parse("C"), chords);
    }

    [Fact]
    public void EveryDesignChordIsInTheDefaultKeymap()
    {
        var map = DefaultKeymap.Build();
        var missing = DesignChords().Where(c => !map.ContainsKey(c)).Select(c => c.ToString()).ToList();
        Assert.True(missing.Count == 0, "Not in DefaultKeymap: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryDefaultKeymapChordIsInTheDesignTable()
    {
        var design = DesignChords();
        var extra = DefaultKeymap.Build().Keys.Where(c => !design.Contains(c)).Select(c => c.ToString()).ToList();
        Assert.True(extra.Count == 0, "Bound but not in DESIGN 7: " + string.Join(", ", extra));
    }

    [Fact]
    public void EveryDesignChordRunsARegisteredCommand()
    {
        var vm = MainWindow();
        var map = DefaultKeymap.Build();
        var unregistered = DesignChords()
            .Where(map.ContainsKey)
            .Select(c => (Chord: c, Id: map[c]))
            .Where(x => !Deferred.Contains(x.Id) && !vm.Commands.IsRegistered(x.Id))
            .Select(x => x.Chord + " (" + x.Id + ")")
            .ToList();
        Assert.True(unregistered.Count == 0, "No registered command for: " + string.Join(", ", unregistered));
    }

    [Fact]
    public void NothingIsDeferredAnyMore()
    {
        // M7 added the last DESIGN 7 command, the settings dialog: Ctrl+, opens it.
        Assert.Empty(Deferred);
        Assert.Equal(CommandIds.Settings, DefaultKeymap.Build()[KeyChord.Parse("Ctrl+OemComma")]);
        var vm = MainWindow();
        Assert.True(vm.Commands.IsRegistered(CommandIds.Settings));
        Assert.True(vm.Keys.HandleKeyDown(Key.OemComma, KeyModifiers.Control, textInputFocused: false));
    }

    [Fact]
    public void KHeldWithJOrLStepsThroughRegisteredCommands()
    {
        // The "K held + J / L" row is a chord the key binding service detects, not a keymap entry.
        var vm = MainWindow();
        Assert.True(vm.Commands.IsRegistered(CommandIds.StepBackward));
        Assert.True(vm.Commands.IsRegistered(CommandIds.StepForward));
        Assert.Contains(DesignRows(), r => r.Contains("K held", StringComparison.Ordinal));
    }

    [Fact]
    public void MenuOnlyCommandsAreRegistered()
    {
        var vm = MainWindow();
        string[] ids =
        [
            CommandIds.MoveIntoSync, CommandIds.AddVideoTrack, CommandIds.AddAudioTrack, CommandIds.RemoveSelectedTrack,
            CommandIds.SelectInOutRange, CommandIds.RippleDeleteInOutRange, CommandIds.CloseGap,
            CommandIds.ShowTimelineMonitor, CommandIds.ShowSourceMonitor, CommandIds.About, CommandIds.OpenLogFolder,
            CommandIds.RelinkMedia,
        ];
        Assert.All(ids, id => Assert.True(vm.Commands.IsRegistered(id), id));
    }

    [Fact]
    public void TextInputsStillKeepTheirKeys()
    {
        var vm = MainWindow();
        Assert.False(vm.Keys.HandleKeyDown(Key.F, KeyModifiers.None, textInputFocused: true));
        Assert.False(vm.Keys.HandleKeyDown(Key.OemComma, KeyModifiers.None, textInputFocused: true));
        Assert.False(vm.Keys.HandleKeyDown(Key.C, KeyModifiers.Control, textInputFocused: true));
        Assert.True(vm.Keys.HandleKeyDown(Key.F, KeyModifiers.None, textInputFocused: false));
    }

    /// <summary>The chords of the DESIGN 7 table, parsed the way a reader would.</summary>
    internal static HashSet<KeyChord> DesignChords()
    {
        var chords = new HashSet<KeyChord>();
        foreach (var row in DesignRows())
        {
            var cells = row.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            for (var i = 0; i < cells.Length; i += 2)
            {
                foreach (var text in CellChords(cells[i]))
                {
                    Assert.True(KeyChord.TryParse(text, out var chord), $"'{text}' from the DESIGN 7 cell '{cells[i]}' is not a chord");
                    chords.Add(chord);
                }
            }
        }

        return chords;
    }

    private static List<string> DesignRows()
    {
        var lines = File.ReadAllLines(Path.Combine(Golden.RepositoryRoot(), "docs", "DESIGN.md"));
        var start = Array.FindIndex(lines, l => l.StartsWith("## 7.", StringComparison.Ordinal));
        Assert.True(start >= 0, "DESIGN.md has no section 7");
        var rows = new List<string>();
        for (var i = start + 1; i < lines.Length && !lines[i].StartsWith("## ", StringComparison.Ordinal); i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('|') && !line.StartsWith("| Key", StringComparison.Ordinal) && !line.StartsWith("|---", StringComparison.Ordinal))
            {
                rows.Add(line);
            }
        }

        return rows;
    }

    // One key cell, for example "Shift+Left / Right" (Shift+Left, Shift+Right), "V B N Y C", "Delete, Backspace" or
    // "`+` OemPlus or Add / `-` OemMinus or Subtract". A bare key after a chord with modifiers shares them.
    private static IEnumerable<string> CellChords(string cell)
    {
        if (cell.Length == 0 || cell.Contains("held", StringComparison.Ordinal))
        {
            yield break;
        }

        var text = Backticks().Replace(cell, " ");
        var previousModifiers = string.Empty;
        foreach (var alternative in Separators().Split(text))
        {
            foreach (var token in alternative.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var plus = token.LastIndexOf('+');
                var modifiers = plus > 0 ? token[..(plus + 1)] : string.Empty;
                var key = plus > 0 ? token[(plus + 1)..] : token;
                if (modifiers.Length == 0)
                {
                    modifiers = previousModifiers;
                }

                previousModifiers = modifiers;
                yield return modifiers + Normalize(key);
            }
        }
    }

    private static string Normalize(string key) => key switch
    {
        "Esc" => "Escape",
        "Backspace" => "Back",
        "," => "OemComma",
        "." => "OemPeriod",
        _ => key,
    };

    [GeneratedRegex("`[^`]*`")]
    private static partial Regex Backticks();

    [GeneratedRegex(@"\s/\s|,\s|\sor\s")]
    private static partial Regex Separators();

    private MainWindowViewModel MainWindow()
    {
        var paths = new AppPaths(Path.Combine(_folder, "settings.json"), Path.Combine(_folder, "data"), Path.Combine(_folder, "cache"), OperatingSystem.IsWindows());
        var registry = new ChildProcessRegistry(null);
        var services = new AppServices(paths, registry, new ProcessRunner(registry, null));
        return new MainWindowViewModel(services, new FakeDialogService(), action => action());
    }
}
