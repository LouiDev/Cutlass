using System.Security.Cryptography;

namespace Cutlass.Core.Editing;

/// <summary>
/// Produces ids from 40 random bits written as 8 lowercase Crockford base32 characters (digits and letters without
/// i, l, o and u), for example <c>c_7f2c4k9q</c>. Thread safe.
/// </summary>
public sealed class RandomIdGenerator : IIdGenerator
{
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>A shared instance.</summary>
    public static RandomIdGenerator Shared { get; } = new();

    /// <inheritdoc />
    public string NewId(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        Span<byte> bytes = stackalloc byte[5];
        RandomNumberGenerator.Fill(bytes);
        ulong bits = 0;
        foreach (var b in bytes)
        {
            bits = (bits << 8) | b;
        }

        Span<char> chars = stackalloc char[8];
        for (var i = 7; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(bits & 31)];
            bits >>= 5;
        }

        return string.Concat(prefix, chars);
    }
}
