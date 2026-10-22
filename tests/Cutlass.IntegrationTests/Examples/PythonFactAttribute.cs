namespace Cutlass.IntegrationTests.Examples;

/// <summary>A fact that needs Python on the PATH; skipped without it.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PythonFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute and sets <see cref="FactAttribute.Skip"/> when Python is missing.</summary>
    public PythonFactAttribute()
    {
        if (ExampleScripts.Python is null)
        {
            Skip = "Python was not found on the PATH.";
        }
    }
}
