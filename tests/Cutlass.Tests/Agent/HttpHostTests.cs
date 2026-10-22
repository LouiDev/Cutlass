using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Agent.Http;
using Cutlass.Tests.Agent.Kit;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Agent;

/// <summary>The real HTTP host on a free loopback port over a <see cref="FakeAgentHost"/> (AGENT_INTEGRATION 4.1 and 7.1).</summary>
public sealed class HttpHostTests : IAsyncLifetime
{
    private const string Token = "test-token-0123456789-abcdefghijklmnop";
    private readonly FakeAgentHost _host = new(new ProjectBuilder().With(V1, V("v1", 0, 100)).With(A1, A("a1", 0, 100)).Build());
    private readonly ManualTimeProvider _limiterClock = new();
    private AgentHttpServer _server = null!;
    private AgentClient _client = null!;

    public async Task InitializeAsync()
    {
        _server = new AgentHttpServer(_host.Dispatcher, Token, new RateLimiter(30, 60, _limiterClock));
        await _server.StartAsync(FreeLoopbackPort());
        _client = new AgentClient(_server.BaseUrl!, Token);
    }

    /// <summary>
    /// A loopback port the OS reports as free. Windows runners exclude whole port ranges (Hyper-V, WSL), where http.sys
    /// answers a sharing violation for every port, so a random number is not enough; a port the kernel hands out on
    /// bind to port 0 never lies in such a range.
    /// </summary>
    /// <returns>The port.</returns>
    internal static int FreeLoopbackPort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _server.DisposeAsync();
        _host.Dispose();
    }

    private async Task<HttpResponseMessage> RawAsync(HttpMethod method, string path, string? body = null, Action<HttpRequestHeaders>? headers = null)
    {
        using var http = new HttpClient { BaseAddress = new Uri(_server.BaseUrl!) };
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        headers?.Invoke(request.Headers);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var response = await http.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    private static async Task<JsonElement> ErrorOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("error").Clone();
    }

    [Fact]
    public async Task StatusWorksWithTheToken()
    {
        var status = await _client.StatusAsync();
        Assert.Equal("headless", status.HostKind);
        Assert.Equal("9.9.9", status.Version);
        Assert.StartsWith("http://", _client.BaseUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingOrWrongTokenIsUnauthorized()
    {
        var wrong = await RawAsync(HttpMethod.Get, "/v1/status", headers: h => h.Authorization = new AuthenticationHeaderValue("Bearer", "nope"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("unauthorized", (await ErrorOf(wrong)).GetProperty("code").GetString());
        var missing = await RawAsync(HttpMethod.Get, "/v1/status", headers: h => h.Authorization = null);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
    }

    [Fact]
    public async Task AnOriginHeaderIsForbidden()
    {
        var response = await RawAsync(HttpMethod.Get, "/v1/status", headers: h => h.Add("Origin", "http://evil.example"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("origin", (await ErrorOf(response)).GetProperty("details").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task AForeignHostIsRefused()
    {
        var port = _server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var response = await RawAsync(HttpMethod.Get, "/v1/status", headers: h => h.Host = "evil.example:" + port);

        // Which layer refuses depends on the platform: the guard answers 403, http.sys on Windows answers 400 for a
        // host it was not registered for, and the managed listener on Linux answers 404 because no prefix matches the
        // foreign host. All three mean the request never reached a tool; the guard itself is checked directly below.
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            response.StatusCode.ToString());
        Assert.Null(AuthGuard.Check(IPAddress.Loopback, "localhost:" + port, null, "Bearer " + Token, _server.Port, Token));
        Assert.Equal(ErrorCode.Forbidden, AuthGuard.Check(IPAddress.Loopback, "evil.example:" + port, null, "Bearer " + Token, _server.Port, Token)!.Code);
        Assert.Equal(ErrorCode.Forbidden, AuthGuard.Check(IPAddress.Parse("192.168.1.20"), "localhost:" + port, null, "Bearer " + Token, _server.Port, Token)!.Code);
        Assert.Equal(ErrorCode.Forbidden, AuthGuard.Check(IPAddress.IPv6Loopback, "localhost:1", null, "Bearer " + Token, _server.Port, Token)!.Code);
    }

    [Fact]
    public async Task AnOversizedBodyIsTooLarge()
    {
        var body = "{\"pad\":\"" + new string('x', BodyReader.MaxBytes + 10) + "\"}";
        var response = await RawAsync(HttpMethod.Post, "/v1/lock/acquire", body);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("payload_too_large", (await ErrorOf(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task BadJsonAndUnknownRoutesHaveTheErrorShape()
    {
        var bad = await RawAsync(HttpMethod.Post, "/v1/lock/acquire", "{not json");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("invalid_argument", (await ErrorOf(bad)).GetProperty("code").GetString());
        var unknown = await RawAsync(HttpMethod.Get, "/v1/nothing");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var doc = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("revision", out _));
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("error").GetProperty("message").ValueKind);
    }

    [Fact]
    public async Task TheRateLimiterAnswersWithRetryAfter()
    {
        for (var i = 0; i < 60; i++)
        {
            Assert.Null((await _client.CallAsync("lock.get")).Error);
        }

        var limited = await _client.CallAsync("lock.get");
        Assert.Equal(ErrorCode.RateLimited, limited.Error!.Code);
        Assert.InRange(limited.Error.RetryAfterMs ?? 0, 1, 1000);
        _limiterClock.AdvanceMs(100);
        Assert.Null((await _client.CallAsync("lock.get")).Error);
    }

    [Fact]
    public async Task TheLockHeaderCarriesTheLockAndErrorsUse423()
    {
        var none = await RawAsync(HttpMethod.Post, "/v1/edit/split", """{"frame":50}""");
        Assert.Equal((HttpStatusCode)423, none.StatusCode);
        Assert.Equal("lock_required", (await ErrorOf(none)).GetProperty("code").GetString());

        await _client.AcquireLockAsync("Claude Code", "Test");
        var split = await _client.SplitAsync(new Cutlass.Agent.Api.Requests.SplitRequest { Frame = 50 });
        Assert.True(split.Changed);
        Assert.Equal("Agent: Split", split.UndoLabel);
        var lockState = await _client.GetLockAsync();
        Assert.True(lockState.Mine);
        Assert.Equal("lock.get", lockState.LastTool);
        await _client.ReleaseLockAsync();
        Assert.Null(_client.LockId);
    }

    [Fact]
    public async Task TheGenericCallMatchesTheDirectRoute()
    {
        var direct = await _client.CallAsync("project.get", JsonDocument.Parse("""{"trackIds":["tr_v1"],"from":0}""").RootElement);
        var generic = await _client.CallGenericAsync("project.get", JsonDocument.Parse("""{"trackIds":["tr_v1"],"from":0}""").RootElement);
        Assert.Null(direct.Error);
        Assert.Equal(direct.Result!.Value.GetRawText(), generic.Result!.Value.GetRawText());
        Assert.Single(direct.Result.Value.GetProperty("tracks").EnumerateArray());

        var query = await RawAsync(HttpMethod.Get, "/v1/project?tracks=tr_a1&from=0");
        using var doc = JsonDocument.Parse(await query.Content.ReadAsStringAsync());
        Assert.Equal("tr_a1", doc.RootElement.GetProperty("result").GetProperty("tracks")[0].GetProperty("id").GetString());

        var describe = await _client.DescribeProjectAsync();
        Assert.Contains("V1 tr_v1:", describe, StringComparison.Ordinal);
        var unknownTool = await _client.CallGenericAsync("no.such.tool");
        Assert.Equal(ErrorCode.NotFound, unknownTool.Error!.Code);
    }

    [Theory]
    [InlineData("/v1/tools", "application/json")]
    [InlineData("/v1/tools?format=openai", "application/json")]
    [InlineData("/v1/tools?format=anthropic", "application/json")]
    [InlineData("/v1/tools?format=gemini", "application/json")]
    [InlineData("/v1/tools?format=markdown", "text/markdown")]
    [InlineData("/v1/openapi.json", "application/json")]
    [InlineData("/v1/instructions", "application/json")]
    public async Task DiscoveryRoutesNeedTheTokenAndServeTheirDocuments(string path, string mediaType)
    {
        var missing = await RawAsync(HttpMethod.Get, path, headers: h => h.Authorization = null);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("unauthorized", (await ErrorOf(missing)).GetProperty("code").GetString());

        var ok = await RawAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(mediaType, ok.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", ok.Content.Headers.ContentType?.CharSet);
        var body = await ok.Content.ReadAsStringAsync();
        var question = path.IndexOf('?', StringComparison.Ordinal);
        var expected = path switch
        {
            "/v1/openapi.json" => Cutlass.Agent.Api.Export.OpenApiDocument.Build("9.9.9"),
            "/v1/instructions" => null,
            _ => Cutlass.Agent.Api.Export.ToolExport.TryRender(question < 0 ? null : path[(question + 8)..], out var text, out _) ? text : "?",
        };
        if (expected is not null)
        {
            Assert.Equal(expected, body);
        }
        else
        {
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(AgentInstructions.Http(), doc.RootElement.GetProperty("text").GetString());
        }

        // Discovery is a read like any other: no lock, and it lands in the audit log.
        Assert.Contains(_host.Audit.Snapshot(), e => e.Tool.StartsWith("discovery.", StringComparison.Ordinal) && e.Code == "ok");
    }

    [Fact]
    public async Task AnUnknownToolsFormatIsInvalid()
    {
        var response = await RawAsync(HttpMethod.Get, "/v1/tools?format=yaml");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("format", (await ErrorOf(response)).GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task TheGenericCallTakesMcpNamesAndAlwaysAnswersTheEnvelope()
    {
        var args = JsonDocument.Parse("""{"trackIds":["tr_v1"],"from":0}""").RootElement;
        var byCatalog = await _client.CallGenericAsync("project.get", args);
        var byMcp = await _client.CallGenericAsync("cutlass_get_project", args);
        Assert.Null(byMcp.Error);
        Assert.Equal(byCatalog.Result!.Value.GetRawText(), byMcp.Result!.Value.GetRawText());
        Assert.Equal("project.get", _host.Audit.Snapshot()[^1].Tool);

        // Describe answers text on its route, but the envelope with result.text through /v1/call.
        var describe = await RawAsync(HttpMethod.Post, "/v1/call", """{"tool":"cutlass_describe_project","args":{}}""");
        Assert.Equal("application/json", describe.Content.Headers.ContentType?.MediaType);
        using (var doc = JsonDocument.Parse(await describe.Content.ReadAsStringAsync()))
        {
            Assert.Contains("V1 tr_v1:", doc.RootElement.GetProperty("result").GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.True(doc.RootElement.TryGetProperty("revision", out _));
        }

        // A discovery document through /v1/call is the envelope's result.
        var tools = await _client.CallGenericAsync("discovery.tools", JsonDocument.Parse("""{"format":"anthropic"}""").RootElement);
        Assert.Null(tools.Error);
        Assert.Equal(ToolCatalog.McpTools.Count(), tools.Result!.Value.GetArrayLength());
        var instructions = await _client.CallGenericAsync("discovery.instructions");
        Assert.Equal(AgentInstructions.Http(), instructions.Result!.Value.GetProperty("text").GetString());

        // An edit by MCP name still needs the lock, and unknown names keep the name the caller used.
        var split = await _client.CallGenericAsync("cutlass_split", JsonDocument.Parse("""{"frame":50}""").RootElement);
        Assert.Equal(ErrorCode.LockRequired, split.Error!.Code);
        var unknown = await _client.CallGenericAsync("cutlass_no_such_tool");
        Assert.Equal(ErrorCode.NotFound, unknown.Error!.Code);
        Assert.Contains("cutlass_no_such_tool", unknown.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventsLongPollReturnsWhenSomethingHappens()
    {
        var first = await _client.EventsAsync(0, 0);
        var poll = _client.EventsAsync(first.Next, 5000);
        await Task.Delay(100);
        Assert.False(poll.IsCompleted);
        await _client.AcquireLockAsync("Claude Code", "Test");
        var events = await poll;
        Assert.Contains(events.Events, e => e.Kind == "lockChanged");
        await _client.ReleaseLockAsync();
    }

    [Fact]
    public async Task TheEndpointFileAndTheTokenStoreRoundTrip()
    {
        var dir = Path.Combine(_host.Root, "agent");
        var tokens = new TokenStore(dir);
        var token = tokens.LoadOrCreate();
        Assert.Equal(token, tokens.LoadOrCreate());
        Assert.True(token.Length >= 43);
        var again = tokens.Regenerate();
        Assert.NotEqual(token, again);
        Assert.True(TokenStore.Matches(again, again));
        Assert.False(TokenStore.Matches(again, token));
        Assert.False(TokenStore.Matches(again, null));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(tokens.TokenFile));
        }

        var endpoint = new EndpointFile(dir);
        endpoint.Write(EndpointFile.ForThisProcess(_server.BaseUrl!, "headless", tokens.TokenFile, null));
        var read = endpoint.Read()!;
        Assert.Equal(_server.BaseUrl, read.BaseUrl);
        Assert.Equal(Environment.ProcessId, read.Pid);
        Assert.Null(endpoint.ReadLiveOther());
        using var client = AgentClient.FromAgentDirectory(dir);
        Assert.NotNull(client);
        endpoint.DeleteIfOwned();
        Assert.Null(endpoint.Read());
        await Task.CompletedTask;
    }
}
