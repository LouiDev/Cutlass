using System.Globalization;
using Cutlass.Core.Editing;

namespace Cutlass.Tests.TestSupport;

/// <summary>Deterministic ids for tests: the prefix plus an 8-digit counter shared by all prefixes (c_00000001, ...).</summary>
internal sealed class SequentialIdGenerator : IIdGenerator
{
    private int _next;

    /// <summary>Creates a generator whose first id uses <paramref name="start"/>.</summary>
    /// <param name="start">The first counter value.</param>
    public SequentialIdGenerator(int start = 1)
    {
        _next = start;
    }

    /// <inheritdoc />
    public string NewId(string prefix) => prefix + (_next++).ToString("D8", CultureInfo.InvariantCulture);
}
