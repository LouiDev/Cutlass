using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Agent.Tools.Dispatch;

namespace Cutlass.Agent.Http;

/// <summary>
/// Maps an HTTP request to an <see cref="AgentCall"/> (AGENT_INTEGRATION 4.4): <c>POST /v1/call</c> names the tool
/// in its body; every other route comes from <see cref="ToolCatalog"/>. The arguments are the JSON body with the route
/// segments and the query parameters merged in (they win). Query values that look like integers or booleans become
/// numbers and booleans; <c>tracks=a,b</c> and any <c>...Ids=a,b</c> become string arrays (<c>tracks</c> is
/// <c>trackIds</c>). <c>/v1/call</c> takes the catalog name or the MCP name (AGENT_INTEGRATION 9.2), and image tools
/// called through it default to <c>format=base64</c> so the answer is the envelope with <c>pngBase64</c> (9.1).
/// </summary>
public static class Router
{
    /// <summary>The generic call route.</summary>
    public const string CallRoute = "/v1/call";

    /// <summary>True when <paramref name="path"/> is the generic call route, whose answers are always the envelope.</summary>
    /// <param name="path">The path without query.</param>
    /// <returns>True for <c>/v1/call</c>.</returns>
    public static bool IsCallRoute(string path) =>
        string.Equals("/" + (path ?? string.Empty).Trim('/'), CallRoute, StringComparison.Ordinal);

    /// <summary>Builds the call.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path without query.</param>
    /// <param name="query">The query parameters in order.</param>
    /// <param name="body">The JSON body (an object).</param>
    /// <param name="lockId">The <c>X-Cutlass-Lock</c> header, or null.</param>
    /// <param name="agentName">The <c>X-Cutlass-Agent</c> header, or null.</param>
    /// <returns>The call.</returns>
    /// <exception cref="ApiException"><c>not_found</c> for unknown routes, <c>invalid_argument</c> for a bad call body.</exception>
    public static AgentCall Route(
        string method,
        string path,
        IReadOnlyList<KeyValuePair<string, string>> query,
        JsonElement body,
        string? lockId,
        string? agentName)
    {
        ArgumentNullException.ThrowIfNull(query);
        var cleanPath = "/" + path.Trim('/');
        if (IsCallRoute(cleanPath))
        {
            if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiException(ErrorCode.NotFound, "/v1/call takes POST.");
            }

            var call = body.Deserialize(AgentJsonContext.Relaxed.CallRequest) ?? new Api.Requests.CallRequest();
            var tool = call.Tool ?? throw ApiException.InvalidArgument("tool", "'tool' is required, for example cutlass_split or edit.split.");
            var args = call.Args is { ValueKind: JsonValueKind.Object } a ? a : AgentCall.EmptyArgs;
            if (ToolCatalog.FindAny(tool) is not { } target)
            {
                // The dispatcher answers not_found with the name the caller used.
                return new AgentCall(tool, args, lockId, agentName);
            }

            if (target.Result == ToolResultKind.Png && !args.TryGetProperty("format", out _))
            {
                args = Merge(args, FrozenDictionary<string, string>.Empty, [new("format", "base64")]);
            }

            return new AgentCall(target.Name, args, lockId, agentName);
        }

        var info = ToolCatalog.Match(method, cleanPath, out var routeValues)
            ?? throw new ApiException(ErrorCode.NotFound, $"There is no route {method} {cleanPath}. See the tool catalog for the routes.");
        var merged = Merge(body, routeValues, query);
        return new AgentCall(info.Name, merged, lockId, agentName);
    }

    /// <summary>Merges route values and query parameters into a JSON object.</summary>
    /// <param name="body">The body object.</param>
    /// <param name="routeValues">The route segment values.</param>
    /// <param name="query">The query parameters.</param>
    /// <returns>The merged object.</returns>
    public static JsonElement Merge(JsonElement body, IReadOnlyDictionary<string, string> routeValues, IReadOnlyList<KeyValuePair<string, string>> query)
    {
        ArgumentNullException.ThrowIfNull(routeValues);
        ArgumentNullException.ThrowIfNull(query);
        if (routeValues.Count == 0 && query.Count == 0)
        {
            return body;
        }

        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in routeValues)
        {
            extra[key] = value;
        }

        foreach (var (key, value) in query)
        {
            extra[string.Equals(key, "tracks", StringComparison.Ordinal) ? "trackIds" : key] = value;
        }

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, AgentJsonContext.WriterOptions))
        {
            w.WriteStartObject();
            if (body.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in body.EnumerateObject())
                {
                    if (!extra.ContainsKey(p.Name))
                    {
                        p.WriteTo(w);
                    }
                }
            }

            foreach (var (key, value) in extra)
            {
                w.WritePropertyName(key);
                WriteValue(w, key, value);
            }

            w.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private static void WriteValue(Utf8JsonWriter w, string key, string value)
    {
        if (key.EndsWith("Ids", StringComparison.Ordinal))
        {
            w.WriteStartArray();
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                w.WriteStringValue(part);
            }

            w.WriteEndArray();
        }
        else if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
        {
            w.WriteNumberValue(n);
        }
        else if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            w.WriteBooleanValue(string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            w.WriteStringValue(value);
        }
    }
}
