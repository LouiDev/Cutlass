using System.Text.Json;
using Cutlass.Agent.Api;

namespace Cutlass.Agent.Http;

/// <summary>Reads a JSON request body (AGENT_INTEGRATION 4.6): at most 1 MiB, depth 32, an object or empty.</summary>
public static class BodyReader
{
    /// <summary>The largest body in bytes.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>The deepest JSON nesting.</summary>
    public const int MaxDepth = 32;

    /// <summary>Reads and parses the body.</summary>
    /// <param name="body">The request stream.</param>
    /// <param name="declaredLength">The Content-Length, or -1.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The JSON object (an empty object for an empty body).</returns>
    /// <exception cref="ApiException"><c>payload_too_large</c> or <c>invalid_argument</c>.</exception>
    public static async Task<JsonElement> ReadAsync(Stream body, long declaredLength, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (declaredLength > MaxBytes)
        {
            throw TooLarge();
        }

        var buffer = new byte[Math.Min(MaxBytes + 1, declaredLength > 0 ? declaredLength + 1 : 64 * 1024)];
        var length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                if (buffer.Length > MaxBytes)
                {
                    throw TooLarge();
                }

                Array.Resize(ref buffer, Math.Min(MaxBytes + 1, buffer.Length * 2));
            }

            var read = await body.ReadAsync(buffer.AsMemory(length, buffer.Length - length), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length += read;
            if (length > MaxBytes)
            {
                throw TooLarge();
            }
        }

        return Parse(buffer.AsMemory(0, length));
    }

    /// <summary>Parses body bytes.</summary>
    /// <param name="utf8">The bytes.</param>
    /// <returns>The JSON object.</returns>
    /// <exception cref="ApiException"><c>invalid_argument</c> for bad JSON or a non-object.</exception>
    public static JsonElement Parse(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.Span.Trim(" \t\r\n"u8).IsEmpty)
        {
            return Tools.Dispatch.AgentCall.EmptyArgs;
        }

        try
        {
            using var doc = JsonDocument.Parse(utf8, new JsonDocumentOptions { MaxDepth = MaxDepth });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw ApiException.InvalidArgument("body", "The body must be a JSON object.");
            }

            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw ApiException.InvalidArgument("body", "The body is not valid JSON: " + ex.Message);
        }
    }

    private static ApiException TooLarge() =>
        new(ErrorCode.PayloadTooLarge, "The request body is larger than 1 MiB.", new Dictionary<string, string> { ["field"] = "body" });
}
