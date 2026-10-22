using Cutlass.Agent.Api;

namespace Cutlass.Agent.Tools;

/// <summary>The camelCase wire spelling of enums (<c>DipToBlack</c> is <c>dipToBlack</c>) and its parsing.</summary>
public static class EnumWire
{
    /// <summary>The camelCase name of a value.</summary>
    /// <typeparam name="T">The enum.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>For example <c>lowerThird</c>.</returns>
    public static string Name<T>(T value)
        where T : struct, Enum
    {
        var name = value.ToString();
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>Every value's camelCase name.</summary>
    /// <typeparam name="T">The enum.</typeparam>
    /// <returns>The names in declaration order.</returns>
    public static List<string> Names<T>()
        where T : struct, Enum => [.. Enum.GetValues<T>().Select(Name)];

    /// <summary>Parses a camelCase (or any case) name, or returns the fallback for null.</summary>
    /// <typeparam name="T">The enum.</typeparam>
    /// <param name="text">The text, or null.</param>
    /// <param name="field">The argument name for the error.</param>
    /// <param name="fallback">The value for null.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ApiException"><c>invalid_argument</c> for an unknown name.</exception>
    public static T Parse<T>(string? text, string field, T fallback)
        where T : struct, Enum
    {
        if (text is null)
        {
            return fallback;
        }

        foreach (var value in Enum.GetValues<T>())
        {
            if (string.Equals(value.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        throw ApiException.InvalidArgument(field, $"'{text}' is not a valid {field}. Use one of: {string.Join(", ", Names<T>())}.");
    }
}
