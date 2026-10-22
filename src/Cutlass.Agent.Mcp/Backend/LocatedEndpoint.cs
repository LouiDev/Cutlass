using Cutlass.Agent.Http;

namespace Cutlass.Agent.Mcp.Backend;

/// <summary>
/// A live agent API found by <see cref="EndpointLocator"/>. A class rather than a record, so the token never ends up
/// in a log line through a generated <c>ToString</c> (4.6: tokens are never logged).
/// </summary>
public sealed class LocatedEndpoint
{
    /// <summary>Creates the value.</summary>
    /// <param name="info">The content of <c>endpoint.json</c>.</param>
    /// <param name="token">The bearer token read from <see cref="EndpointInfo.TokenFile"/>.</param>
    public LocatedEndpoint(EndpointInfo info, string token)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Info = info;
        Token = token;
    }

    /// <summary>The content of <c>endpoint.json</c>.</summary>
    public EndpointInfo Info { get; }

    /// <summary>The bearer token.</summary>
    public string Token { get; }

    /// <inheritdoc />
    public override string ToString() => Info.BaseUrl + " (" + Info.HostKind + ", pid " + Info.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
}
