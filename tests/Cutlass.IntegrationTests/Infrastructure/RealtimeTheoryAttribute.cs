using Xunit;

namespace Cutlass.IntegrationTests.Infrastructure;

/// <summary>The theory counterpart of <see cref="RealtimeFactAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RealtimeTheoryAttribute : TheoryAttribute
{
    /// <summary>Creates the attribute and decides whether the theory is skipped.</summary>
    public RealtimeTheoryAttribute()
    {
        Skip = RealtimeFactAttribute.SkipReason();
    }
}
