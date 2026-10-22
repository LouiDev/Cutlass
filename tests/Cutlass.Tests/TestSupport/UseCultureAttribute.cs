using System.Globalization;
using System.Reflection;
using Xunit.Sdk;

namespace Cutlass.Tests.TestSupport;

/// <summary>
/// Runs a test under a specific culture (for example <c>de-DE</c>, where the decimal separator is a comma) and
/// restores the previous culture afterwards. Number-formatting tests use it to prove invariant output.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class UseCultureAttribute : BeforeAfterTestAttribute
{
    private CultureInfo? _previousCulture;
    private CultureInfo? _previousUiCulture;

    /// <summary>Creates the attribute.</summary>
    /// <param name="culture">The culture name, for example <c>de-DE</c>.</param>
    public UseCultureAttribute(string culture)
    {
        Culture = culture;
    }

    /// <summary>The culture name the test runs under.</summary>
    public string Culture { get; }

    /// <inheritdoc />
    public override void Before(MethodInfo methodUnderTest)
    {
        _previousCulture = CultureInfo.CurrentCulture;
        _previousUiCulture = CultureInfo.CurrentUICulture;
        var culture = CultureInfo.GetCultureInfo(Culture);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <inheritdoc />
    public override void After(MethodInfo methodUnderTest)
    {
        if (_previousCulture is not null)
        {
            CultureInfo.CurrentCulture = _previousCulture;
        }

        if (_previousUiCulture is not null)
        {
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }
}
