namespace Cutlass.App.Services;

/// <summary>One line of a smoke script: a lower-case command and its arguments.</summary>
/// <param name="Line">The 1-based line number in the script.</param>
/// <param name="Command">The command, lower case, for example <c>import</c>.</param>
/// <param name="Args">The arguments, with quotes removed.</param>
public sealed record SmokeStep(int Line, string Command, IReadOnlyList<string> Args)
{
    /// <summary>The value of a <c>name=value</c> option argument, or null.</summary>
    /// <param name="name">The option name (case-insensitive).</param>
    /// <returns>The value, or null when the option is absent.</returns>
    public string? Option(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var prefix = name + "=";
        return Args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) is { } arg
            ? arg[prefix.Length..]
            : null;
    }

    /// <summary>True when a bare flag argument (for example <c>bt709</c>) is present.</summary>
    /// <param name="name">The flag (case-insensitive).</param>
    /// <returns>True when present.</returns>
    public bool Flag(string name) => Args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The arguments that are neither options (<c>name=value</c>) nor in the given flag list.</summary>
    /// <param name="flags">Bare flags to leave out.</param>
    /// <returns>The positional arguments.</returns>
    public IReadOnlyList<string> Positional(params string[] flags) =>
        [.. Args.Where(a => !a.Contains('=', StringComparison.Ordinal) && !flags.Contains(a, StringComparer.OrdinalIgnoreCase))];

    /// <inheritdoc />
    public override string ToString() => Args.Count == 0 ? Command : Command + " " + string.Join(' ', Args);
}
