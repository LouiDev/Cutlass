namespace Cutlass.Compiler.Graph;

/// <summary>
/// Assembles a filtergraph from chains (TECH_BRIEF 7.7). Chains are written in the order they were started, one per
/// line, separated by <c>;\n</c>. The builder checks that every label is defined once and consumed once (outputs
/// excepted) and that files are referenced only by bare names.
/// </summary>
public sealed class FilterGraphBuilder
{
    private readonly List<GraphChain> _chains = [];
    private readonly Dictionary<string, int> _defined = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _consumed = new(StringComparer.Ordinal);

    /// <summary>The number of chains started so far.</summary>
    public int ChainCount => _chains.Count;

    /// <summary>Validates a file name for use inside the graph.</summary>
    /// <param name="bareName">The name, for example <c>Inter-Bold.ttf</c>.</param>
    /// <returns>The same name.</returns>
    /// <exception cref="ArgumentException">When the name is not a bare name.</exception>
    public static string FileName(string bareName) => FilterEscaper.RequireBareName(bareName);

    /// <summary>Starts a chain with a source filter, for example <c>color=c=black:s=1920x1080:r=30</c>.</summary>
    /// <param name="sourceFilter">The source filter.</param>
    /// <returns>The open chain.</returns>
    public GraphChain StartSource(string sourceFilter)
    {
        var chain = new GraphChain(this, []);
        _chains.Add(chain);
        return chain.Then(sourceFilter);
    }

    /// <summary>Starts a chain reading an input stream, written <c>[input:stream]</c>.</summary>
    /// <param name="inputIndex">The input number.</param>
    /// <param name="streamIndex">The absolute stream index in that input.</param>
    /// <returns>The open chain (without a filter yet).</returns>
    public GraphChain StartInput(int inputIndex, int streamIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
        var chain = new GraphChain(this, [FfNum.Integer(inputIndex) + ":" + FfNum.Integer(streamIndex)]);
        _chains.Add(chain);
        return chain;
    }

    /// <summary>Starts a chain whose first filter takes labelled inputs, for example <c>[a][b]xfade=...</c>.</summary>
    /// <param name="labels">The input labels (without brackets), in pad order.</param>
    /// <param name="filter">The multi-input filter.</param>
    /// <returns>The open chain.</returns>
    public GraphChain StartJoin(IReadOnlyList<string> labels, string filter)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count == 0)
        {
            throw new ArgumentException("A join needs at least one input label.", nameof(labels));
        }

        foreach (var label in labels)
        {
            Consume(label);
        }

        var chain = new GraphChain(this, labels);
        _chains.Add(chain);
        return chain.Then(filter);
    }

    /// <summary>
    /// Renders the graph. Every chain must be closed, every label defined exactly once and consumed exactly once,
    /// except the <paramref name="outputs"/>, which must be defined and not consumed.
    /// </summary>
    /// <param name="outputs">The labels mapped with <c>-map</c>, without brackets.</param>
    /// <returns>The graph text, without a trailing newline.</returns>
    /// <exception cref="InvalidOperationException">When the graph is inconsistent (a compiler bug).</exception>
    public string Build(IReadOnlyCollection<string> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        if (_chains.Count == 0)
        {
            throw new InvalidOperationException("The graph is empty.");
        }

        foreach (var chain in _chains)
        {
            if (!chain.IsClosed)
            {
                throw new InvalidOperationException("A chain was left open: " + chain.Render());
            }
        }

        foreach (var (label, count) in _defined)
        {
            if (count != 1)
            {
                throw new InvalidOperationException($"Label [{label}] is defined {count} times.");
            }

            var consumed = _consumed.GetValueOrDefault(label);
            var expected = outputs.Contains(label) ? 0 : 1;
            if (consumed != expected)
            {
                throw new InvalidOperationException($"Label [{label}] is consumed {consumed} times, expected {expected}.");
            }
        }

        foreach (var label in _consumed.Keys)
        {
            if (!_defined.ContainsKey(label))
            {
                throw new InvalidOperationException($"Label [{label}] is consumed but never defined.");
            }
        }

        foreach (var output in outputs)
        {
            if (!_defined.ContainsKey(output))
            {
                throw new InvalidOperationException($"Output [{output}] is never defined.");
            }
        }

        return string.Join(";\n", _chains.Select(c => c.Render()));
    }

    internal void Define(string label)
    {
        ValidateLabel(label);
        _defined[label] = _defined.GetValueOrDefault(label) + 1;
    }

    internal void Consume(string label)
    {
        ValidateLabel(label);
        _consumed[label] = _consumed.GetValueOrDefault(label) + 1;
    }

    private static void ValidateLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        foreach (var c in label)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                throw new ArgumentException($"'{label}' is not a valid graph label.", nameof(label));
            }
        }
    }
}
