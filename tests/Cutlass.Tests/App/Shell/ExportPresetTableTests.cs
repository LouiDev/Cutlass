using System.Globalization;
using Cutlass.Compiler.Export;
using Cutlass.Tests.Compiler;

namespace Cutlass.Tests.App.Shell;

/// <summary>
/// The preset tables of DESIGN 9 (names and containers) and TECH_BRIEF 9 (quality values) are the contract: they are
/// read from the docs at test time, so a preset added to either side without the other fails here.
/// </summary>
public sealed class ExportPresetTableTests
{
    [Fact]
    public void EveryDesignPresetExistsWithItsContainer()
    {
        var design = DesignPresets();
        Assert.Equal(10, design.Count);
        foreach (var (name, extension) in design)
        {
            var preset = ExportPresets.All.SingleOrDefault(p => p.DisplayName.StartsWith(name, StringComparison.Ordinal));
            Assert.True(preset is not null, "DESIGN 9 lists " + name + " but ExportPresets has no such preset");
            Assert.Equal(extension, preset!.Extension);
        }
    }

    [Fact]
    public void EveryPresetIsInTheDesignTable()
    {
        var design = DesignPresets();
        var extra = ExportPresets.All
            .Where(p => !design.Any(d => p.DisplayName.StartsWith(d.Name, StringComparison.Ordinal)))
            .Select(p => p.DisplayName)
            .ToList();
        Assert.True(extra.Count == 0, "Not in DESIGN 9: " + string.Join(", ", extra));
    }

    [Fact]
    public void QualityValuesFollowTheBrief()
    {
        var brief = BriefQualities();
        Assert.Equal(ExportPresets.All.Length, brief.Count);
        foreach (var preset in ExportPresets.All)
        {
            Assert.True(brief.TryGetValue(preset.DisplayName, out var values), "TECH_BRIEF 9 has no row for " + preset.DisplayName);
            Assert.Equal(values, preset.QualityValues.IsDefaultOrEmpty ? [] : [.. preset.QualityValues]);
        }
    }

    [Fact]
    public void TheDefaultPresetIsTheOneTheDesignMarks()
    {
        var row = TableRows(Doc("DESIGN.md"), "## 9.", "| Preset |").Single(r => r[0].Contains("(default)", StringComparison.Ordinal));
        Assert.StartsWith(ExportPresets.Default.DisplayName, row[0], StringComparison.Ordinal);
    }

    // (Name, extension) for every preset of the DESIGN 9 table; "MP3 / AAC / WAV / FLAC" spreads over four presets.
    private static List<(string Name, string Extension)> DesignPresets()
    {
        var result = new List<(string, string)>();
        foreach (var cells in TableRows(Doc("DESIGN.md"), "## 9.", "| Preset |"))
        {
            var names = cells[0].Replace("(default)", string.Empty, StringComparison.Ordinal).Split('/', StringSplitOptions.TrimEntries);
            var extensions = cells[1].Split('/', StringSplitOptions.TrimEntries);
            Assert.True(names.Length == extensions.Length, "DESIGN 9 row '" + cells[0] + "' has " + extensions.Length + " containers");
            result.AddRange(names.Zip(extensions));
        }

        return result;
    }

    // Preset display name to its Draft/Good/High/Best values from the TECH_BRIEF 9 table ("fixed" is none).
    private static Dictionary<string, int[]> BriefQualities()
    {
        var result = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var cells in TableRows(Doc("TECH_BRIEF.md"), "## 9.", "| Preset |"))
        {
            var quality = cells[^1];
            int[] values = quality == "fixed"
                ? []
                : [.. quality.Split('/').Select(v => int.Parse(v, NumberStyles.None, CultureInfo.InvariantCulture))];
            foreach (var name in cells[0].Split('/', StringSplitOptions.TrimEntries))
            {
                result[name] = values;
            }
        }

        return result;
    }

    private static string[] Doc(string name) => File.ReadAllLines(Path.Combine(Golden.RepositoryRoot(), "docs", name));

    // The cells of the first table after a header line that starts with tableHeader, inside the section.
    private static List<string[]> TableRows(string[] lines, string section, string tableHeader)
    {
        var start = Array.FindIndex(lines, l => l.StartsWith(section, StringComparison.Ordinal));
        Assert.True(start >= 0, "no section " + section);
        var header = Array.FindIndex(lines, start, l => l.StartsWith(tableHeader, StringComparison.Ordinal));
        Assert.True(header > start, "no preset table in section " + section);
        var rows = new List<string[]>();
        for (var i = header + 2; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
        {
            rows.Add([.. lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim())]);
        }

        return rows;
    }
}
