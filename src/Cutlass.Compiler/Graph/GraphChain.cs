using System.Text;

namespace Cutlass.Compiler.Graph;

/// <summary>
/// One filter chain of a <see cref="FilterGraphBuilder"/>: optional input pads, filters joined with commas and the
/// output labels. A chain stays open until <see cref="Close(string)"/>, so a single-segment run or track can continue
/// in the same chain (TECH_BRIEF 7.7).
/// </summary>
public sealed class GraphChain
{
    private readonly FilterGraphBuilder _owner;
    private readonly List<string> _inputs;
    private readonly List<string> _filters = [];
    private readonly List<string> _outputs = [];

    internal GraphChain(FilterGraphBuilder owner, IEnumerable<string> inputs)
    {
        _owner = owner;
        _inputs = [.. inputs];
    }

    /// <summary>True once the chain has its output labels.</summary>
    public bool IsClosed => _outputs.Count > 0;

    /// <summary>The number of filters in the chain.</summary>
    public int FilterCount => _filters.Count;

    /// <summary>Appends a filter.</summary>
    /// <param name="filter">The filter with its options, for example <c>setsar=1</c>.</param>
    /// <returns>This chain.</returns>
    /// <exception cref="InvalidOperationException">When the chain is closed.</exception>
    public GraphChain Then(string filter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);
        EnsureOpen();
        _filters.Add(filter);
        return this;
    }

    /// <summary>
    /// Appends a two-input filter whose first input is a label and whose second input is this chain, written as
    /// <c>,[label]filter</c>. Labelled pads are linked first, so the label becomes the main input.
    /// </summary>
    /// <param name="label">The label of the first input (without brackets).</param>
    /// <param name="filter">The filter, for example <c>overlay=x=0:y=0</c>.</param>
    /// <returns>This chain.</returns>
    /// <exception cref="InvalidOperationException">When the chain is closed or has no filter yet.</exception>
    public GraphChain ThenWithMainInput(string label, string filter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);
        EnsureOpen();
        if (_filters.Count == 0)
        {
            throw new InvalidOperationException("A mid-chain input needs a preceding filter, so the labelled pad comes first.");
        }

        _owner.Consume(label);
        _filters.Add("[" + label + "]" + filter);
        return this;
    }

    /// <summary>Closes the chain with one output label.</summary>
    /// <param name="label">The label (without brackets), unique in the graph.</param>
    /// <returns>The label.</returns>
    public string Close(string label)
    {
        Close([label]);
        return label;
    }

    /// <summary>Closes the chain with several output labels (for <c>split</c>).</summary>
    /// <param name="labels">The labels (without brackets), unique in the graph.</param>
    /// <exception cref="InvalidOperationException">When the chain is closed or empty.</exception>
    public void Close(IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        EnsureOpen();
        if (_filters.Count == 0)
        {
            throw new InvalidOperationException("A chain needs at least one filter.");
        }

        if (labels.Count == 0)
        {
            throw new ArgumentException("A chain needs at least one output label.", nameof(labels));
        }

        foreach (var label in labels)
        {
            _owner.Define(label);
            _outputs.Add(label);
        }
    }

    internal string Render()
    {
        var sb = new StringBuilder();
        foreach (var input in _inputs)
        {
            sb.Append('[').Append(input).Append(']');
        }

        sb.AppendJoin(',', _filters);
        foreach (var output in _outputs)
        {
            sb.Append('[').Append(output).Append(']');
        }

        return sb.ToString();
    }

    private void EnsureOpen()
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("The chain is already closed.");
        }
    }
}
