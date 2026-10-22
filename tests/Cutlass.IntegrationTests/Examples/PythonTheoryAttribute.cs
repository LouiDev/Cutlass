namespace Cutlass.IntegrationTests.Examples;

/// <summary>A theory that needs Python on the PATH; skipped without it.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PythonTheoryAttribute : TheoryAttribute
{
    /// <summary>Creates the attribute and sets <see cref="FactAttribute.Skip"/> when Python is missing.</summary>
    public PythonTheoryAttribute()
    {
        if (ExampleScripts.Python is null)
        {
            Skip = "Python was not found on the PATH.";
        }
    }
}
