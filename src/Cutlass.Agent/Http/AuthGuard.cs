using System.Net;
using Cutlass.Agent.Api;

namespace Cutlass.Agent.Http;

/// <summary>
/// The request checks of AGENT_INTEGRATION 4.1, in order: the peer is loopback, <c>Host</c> is
/// <c>localhost:&lt;port&gt;</c> or <c>127.0.0.1:&lt;port&gt;</c> (against DNS rebinding), no <c>Origin</c> header is
/// present (browsers send one), and <c>Authorization: Bearer &lt;token&gt;</c> matches in constant time.
/// </summary>
public static class AuthGuard
{
    /// <summary>Checks one request.</summary>
    /// <param name="remote">The peer address, or null.</param>
    /// <param name="host">The Host header, or null.</param>
    /// <param name="origin">The Origin header, or null.</param>
    /// <param name="authorization">The Authorization header, or null.</param>
    /// <param name="port">The port the server listens on.</param>
    /// <param name="token">The expected token.</param>
    /// <returns>Null when allowed, else the error.</returns>
    public static ApiError? Check(IPAddress? remote, string? host, string? origin, string? authorization, int port, string token)
    {
        if (remote is null || !IPAddress.IsLoopback(remote))
        {
            return new ApiError(ErrorCode.Forbidden, "Only local (loopback) connections are allowed.", new Dictionary<string, string> { ["reason"] = "not_loopback" });
        }

        if (!IsLocalHost(host, port))
        {
            return new ApiError(ErrorCode.Forbidden, "The Host header must be localhost or 127.0.0.1 with the server's port.", new Dictionary<string, string> { ["reason"] = "host" });
        }

        if (origin is not null)
        {
            return new ApiError(ErrorCode.Forbidden, "Requests with an Origin header (browsers) are refused.", new Dictionary<string, string> { ["reason"] = "origin" });
        }

        const string scheme = "Bearer ";
        var presented = authorization is not null && authorization.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            ? authorization[scheme.Length..].Trim()
            : null;
        if (!TokenStore.Matches(token, presented))
        {
            return new ApiError(ErrorCode.Unauthorized, "Missing or wrong bearer token. Read the token file named in endpoint.json.");
        }

        return null;
    }

    /// <summary>True when the Host header names this server on loopback.</summary>
    /// <param name="host">The Host header.</param>
    /// <param name="port">The port.</param>
    /// <returns>True for <c>localhost:port</c> or <c>127.0.0.1:port</c>.</returns>
    public static bool IsLocalHost(string? host, int port)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        var suffix = ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return string.Equals(host, "localhost" + suffix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1" + suffix, StringComparison.Ordinal);
    }
}
