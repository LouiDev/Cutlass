using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.IntegrationTests.Infrastructure;

namespace Cutlass.IntegrationTests.Agent;

/// <summary>
/// The discovery routes and the generic call contract of AGENT_INTEGRATION 9.1 and 9.2 against the headless host's
/// real listener (7.2): the OpenAPI document describes routes that exist, and image tools answer
/// <c>pngBase64</c> through <c>/v1/call</c> under either name.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class DiscoveryTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly TestMediaFixture _media;

    public DiscoveryTests(TestMediaFixture media)
    {
        _media = media;
    }

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpClient http, HttpMethod method, string path, string? body)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path.TrimStart('/'));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AgentKit.Token);
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            using var response = await http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt > 20)
            {
                return (response.StatusCode, text);
            }

            // The default limiter allows a burst of 60; walking every route may exceed it.
            await Task.Delay(TimeSpan.FromSeconds(response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 1));
        }
    }

    [FfmpegFact]
    public async Task EveryOpenApiPathAnswersItsDocumentedMethod()
    {
        await using var kit = await AgentKit.CreateAsync(_media);
        using var http = new HttpClient { BaseAddress = new Uri(kit.Server.BaseUrl!), Timeout = TimeSpan.FromMinutes(2) };
        var (status, text) = await SendAsync(http, HttpMethod.Get, "/v1/openapi.json", null);
        Assert.Equal(HttpStatusCode.OK, status);
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("3.1.0", doc.RootElement.GetProperty("openapi").GetString());
        Assert.Equal("0.0.0-it", doc.RootElement.GetProperty("info").GetProperty("version").GetString());

        var before = await kit.Client.StatusAsync();
        var checkedOperations = 0;
        foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
        {
            // Unknown ids are fine: an id-level not_found still proves the route exists.
            var concrete = path.Name.Replace("{mediaId}", "m_missing", StringComparison.Ordinal)
                .Replace("{frame}", "0", StringComparison.Ordinal)
                .Replace("{exportId}", "x_missing", StringComparison.Ordinal);
            Assert.DoesNotContain('{', concrete);
            foreach (var op in path.Value.EnumerateObject())
            {
                var method = new HttpMethod(op.Name.ToUpperInvariant());
                var (code, body) = await SendAsync(http, method, concrete, method == HttpMethod.Post ? "{}" : null);
                if (code == HttpStatusCode.NotFound)
                {
                    using var error = JsonDocument.Parse(body);
                    var message = error.RootElement.GetProperty("error").GetProperty("message").GetString() ?? string.Empty;
                    Assert.False(message.StartsWith("There is no route", StringComparison.Ordinal), $"{method} {concrete}: {message}");
                    Assert.False(message.Contains("takes POST", StringComparison.Ordinal), $"{method} {concrete}: {message}");
                }

                Assert.True(code is not (HttpStatusCode.MethodNotAllowed or HttpStatusCode.InternalServerError or HttpStatusCode.Unauthorized), $"{method} {concrete}: {(int)code} {body}");
                checkedOperations++;
            }
        }

        Assert.True(checkedOperations > ToolCatalog.All.Length, checkedOperations.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Nothing above held the lock, so no route changed the project.
        var after = await kit.Client.StatusAsync();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Null(after.Batch);
    }

    [FfmpegFact]
    public async Task ImageToolsAnswerPngBase64ThroughTheGenericCallUnderBothNames()
    {
        await using var kit = await AgentKit.CreateAsync(_media);
        foreach (var (tool, args) in new[]
        {
            ("cutlass_render_frame", """{"frame":0,"width":64}"""),
            ("frame.render", """{"frame":0,"width":64}"""),
            ("cutlass_contact_sheet", """{"frames":[0,1],"cellWidth":32}"""),
        })
        {
            using var parsed = JsonDocument.Parse(args);
            var result = await kit.Client.CallGenericAsync(tool, parsed.RootElement.Clone());
            Assert.Null(result.Error);
            Assert.Null(result.Png);
            var base64 = result.Result!.Value.GetProperty("pngBase64").GetString();
            var png = Convert.FromBase64String(base64!);
            Assert.True(png.AsSpan(0, 8).SequenceEqual(PngSignature), tool);
        }

        // The route itself still answers PNG bytes by default.
        var direct = await kit.Client.CallAsync("frame.render", JsonDocument.Parse("""{"frame":0,"width":64}""").RootElement);
        Assert.NotNull(direct.Png);
        Assert.True(direct.Png.AsSpan(0, 8).SequenceEqual(PngSignature));
    }
}
