using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Cutlass.Agent.Http;
using Cutlass.IntegrationTests.Infrastructure;

namespace Cutlass.IntegrationTests.Agent;

/// <summary>The security rules of AGENT_INTEGRATION 4.1 against the headless host's real listener (7.2).</summary>
[Collection(TestMediaCollection.Name)]
public sealed class HttpSecurityTests
{
    private readonly TestMediaFixture _media;

    public HttpSecurityTests(TestMediaFixture media)
    {
        _media = media;
    }

    private static async Task<HttpStatusCode> SendAsync(AgentKit kit, Action<HttpRequestMessage> configure)
    {
        using var http = new HttpClient { BaseAddress = new Uri(kit.Server.BaseUrl!) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AgentKit.Token);
        configure(request);
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    [FfmpegFact]
    public async Task TokenOriginAndHostAreChecked()
    {
        await using var kit = await AgentKit.CreateAsync(_media);
        Assert.Equal(HttpStatusCode.OK, await SendAsync(kit, _ => { }));
        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(kit, r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong")));
        Assert.Equal(HttpStatusCode.Forbidden, await SendAsync(kit, r => r.Headers.Add("Origin", "https://example.com")));
        var foreign = await SendAsync(kit, r => r.Headers.Host = "attacker.example:" + kit.Server.Port.ToString(CultureInfo.InvariantCulture));
        // The guard answers 403; http.sys on Windows answers 400 for an unregistered host; the managed listener on
        // Linux answers 404 because no prefix matches the foreign host. All three stop the request before any tool.
        Assert.True(foreign is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or HttpStatusCode.NotFound, foreign.ToString());

        // The prefix follows the platform rule: localhost on Windows (a normal user may register it), else 127.0.0.1.
        Assert.StartsWith(OperatingSystem.IsWindows() ? "http://localhost:" : "http://127.0.0.1:", kit.Server.BaseUrl, StringComparison.Ordinal);
        Assert.True(kit.Server.IsRunning);
    }

    [FfmpegFact]
    public async Task AFixedPortThatIsTakenFailsClearly()
    {
        await using var kit = await AgentKit.CreateAsync(_media);
        await using var second = new AgentHttpServer(kit.Dispatcher, AgentKit.Token);
        var ex = await Assert.ThrowsAsync<HttpListenerException>(() => second.StartAsync(kit.Server.Port, fixedPort: true));
        Assert.Contains(kit.Server.Port.ToString(CultureInfo.InvariantCulture), ex.Message, StringComparison.Ordinal);

        // Without a fixed port the next free one is used.
        await using var third = new AgentHttpServer(kit.Dispatcher, AgentKit.Token);
        await third.StartAsync(kit.Server.Port);
        Assert.NotEqual(kit.Server.Port, third.Port);
    }
}
