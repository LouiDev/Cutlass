using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Cutlass.Agent.Api.Dto;

namespace Cutlass.Agent.Api;

/// <summary>
/// The outcome of one tool call, shared by the HTTP host, the MCP backends and <see cref="Http.AgentClient"/>
/// (AGENT_INTEGRATION 4.2 and 4.3). A success carries the revision, warnings and a JSON <see cref="Result"/>; mutating
/// calls add <see cref="Changed"/>, <see cref="Select"/>, <see cref="UndoLabel"/> and optionally the
/// <see cref="Project"/> summary. Image tools set <see cref="Png"/> (with metadata in <see cref="Result"/>), and the
/// describe tool sets <see cref="Text"/>. A failure carries <see cref="Error"/>.
/// </summary>
public sealed class ApiResult
{
    /// <summary>The revision after the call.</summary>
    public long Revision { get; init; }

    /// <summary>Warnings (clamps, notes); empty when the call did exactly what was asked.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>The JSON result, or null (errors, or a bare PNG).</summary>
    public JsonElement? Result { get; init; }

    /// <summary>Mutating calls: false when the edit clamped to nothing. Null for reads.</summary>
    public bool? Changed { get; init; }

    /// <summary>Mutating calls: the ids the UI selects after the edit. Null for reads.</summary>
    public IReadOnlyList<string>? Select { get; init; }

    /// <summary>Mutating calls: the label Undo would revert now. Null for reads.</summary>
    public string? UndoLabel { get; init; }

    /// <summary>Mutating calls with <c>returnProject</c>: the project summary.</summary>
    public JsonElement? Project { get; init; }

    /// <summary>The PNG of an image tool, or null.</summary>
    public byte[]? Png { get; init; }

    /// <summary>The text of a text tool (describe), or null.</summary>
    public string? Text { get; init; }

    /// <summary>Extra response headers (for example the contact sheet layout), or null.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>The error, or null on success.</summary>
    public ApiError? Error { get; init; }

    /// <summary>True when the call failed.</summary>
    public bool IsError => Error is not null;

    /// <summary>The HTTP status: 200 or the error's status.</summary>
    public int HttpStatus => Error?.HttpStatus ?? 200;

    /// <summary>The content type of the HTTP body.</summary>
    public string ContentType => Error is null && Png is not null ? "image/png"
        : Error is null && Text is not null ? "text/plain; charset=utf-8"
        : "application/json; charset=utf-8";

    /// <summary>A success with a JSON result.</summary>
    /// <param name="revision">The revision.</param>
    /// <param name="result">The result.</param>
    /// <param name="warnings">Warnings, or null.</param>
    /// <returns>The result.</returns>
    public static ApiResult Ok(long revision, JsonElement? result, IEnumerable<string>? warnings = null) =>
        new() { Revision = revision, Result = result, Warnings = warnings?.ToList() ?? [] };

    /// <summary>A success that serialises a typed value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="revision">The revision.</param>
    /// <param name="value">The value.</param>
    /// <param name="typeInfo">Its type info from <see cref="AgentJsonContext"/>.</param>
    /// <param name="warnings">Warnings, or null.</param>
    /// <returns>The result.</returns>
    public static ApiResult Ok<T>(long revision, T value, JsonTypeInfo<T> typeInfo, IEnumerable<string>? warnings = null) =>
        Ok(revision, JsonSerializer.SerializeToElement(value, typeInfo), warnings);

    /// <summary>A failure.</summary>
    /// <param name="revision">The revision.</param>
    /// <param name="error">The error.</param>
    /// <returns>The result.</returns>
    public static ApiResult Fail(long revision, ApiError error) => new() { Revision = revision, Error = error };

    /// <summary>Deserialises <see cref="Result"/>.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <param name="typeInfo">Its type info from <see cref="AgentJsonContext"/>.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ApiException">When the call failed (the error is rethrown).</exception>
    public T ResultAs<T>(JsonTypeInfo<T> typeInfo)
    {
        if (Error is { } error)
        {
            throw new ApiException(error);
        }

        if (Result is not { } result)
        {
            throw new InvalidOperationException("The call returned no JSON result.");
        }

        return result.Deserialize(typeInfo) ?? throw new InvalidOperationException("The call returned a null result.");
    }

    /// <summary>The JSON envelope: <c>{revision, warnings, result, ...}</c> or <c>{revision, error}</c>.</summary>
    /// <param name="indented">True to indent.</param>
    /// <returns>The JSON text.</returns>
    public string ToJson(bool indented = false)
    {
        var envelope = new ApiEnvelopeDto
        {
            Revision = Revision,
            Warnings = Error is null ? [.. Warnings] : null,
            Result = Error is null ? Result : null,
            Changed = Changed,
            Select = Select is null ? null : [.. Select],
            UndoLabel = UndoLabel,
            Project = Project,
            Error = Error is { } e
                ? new ApiErrorDto
                {
                    Code = e.CodeName,
                    Message = e.Message,
                    Details = e.Details is null ? null : new Dictionary<string, string>(e.Details, StringComparer.Ordinal),
                    RetryAfterMs = e.RetryAfterMs,
                }
                : null,
        };
        var info = indented ? AgentJsonContext.Indented.ApiEnvelopeDto : AgentJsonContext.Relaxed.ApiEnvelopeDto;
        return JsonSerializer.Serialize(envelope, info);
    }

    /// <summary>Parses a JSON envelope written by <see cref="ToJson"/>.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The result.</returns>
    /// <exception cref="JsonException">When the text is not an envelope.</exception>
    public static ApiResult FromJson(string json)
    {
        var envelope = JsonSerializer.Deserialize(json, AgentJsonContext.Relaxed.ApiEnvelopeDto)
            ?? throw new JsonException("The response is not an API envelope.");
        ApiError? error = null;
        if (envelope.Error is { } e)
        {
            ApiError.TryParseCode(e.Code, out var code);
            error = new ApiError(code, e.Message ?? string.Empty, e.Details, e.RetryAfterMs);
        }

        return new ApiResult
        {
            Revision = envelope.Revision,
            Warnings = envelope.Warnings ?? [],
            Result = envelope.Result,
            Changed = envelope.Changed,
            Select = envelope.Select,
            UndoLabel = envelope.UndoLabel,
            Project = envelope.Project,
            Error = error,
        };
    }
}
