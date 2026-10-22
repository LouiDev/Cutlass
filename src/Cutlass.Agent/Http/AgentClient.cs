using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto;
using Cutlass.Agent.Api.Requests;

namespace Cutlass.Agent.Http;

/// <summary>
/// An <see cref="HttpClient"/> wrapper over the agent API (AGENT_INTEGRATION 2.1), used by the MCP proxy, the smoke
/// runner and the tests. <see cref="CallAsync"/> runs any tool by catalog name through its route (GET arguments go
/// into the query); the typed methods cover every tool and throw <see cref="ApiException"/> on errors. After
/// <see cref="AcquireLockAsync"/> the client sends <c>X-Cutlass-Lock</c> itself. Thread-safe for concurrent calls.
/// </summary>
public sealed class AgentClient : IDisposable
{
    private readonly HttpClient _http;

    /// <summary>Creates a client.</summary>
    /// <param name="baseUrl">The base URL from <c>endpoint.json</c>, for example <c>http://localhost:47390/</c>.</param>
    /// <param name="token">The bearer token.</param>
    /// <param name="handler">A message handler, or null for a default one.</param>
    public AgentClient(string baseUrl, string token, HttpMessageHandler? handler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        BaseUrl = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.BaseAddress = new Uri(BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(120);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>The base URL.</summary>
    public string BaseUrl { get; }

    /// <summary>The lock id sent as <c>X-Cutlass-Lock</c>, or null. Set by <see cref="AcquireLockAsync"/>.</summary>
    public string? LockId { get; set; }

    /// <summary>The name sent as <c>X-Cutlass-Agent</c>, or null.</summary>
    public string? AgentName { get; set; }

    /// <summary>The request timeout (120 s by default).</summary>
    public TimeSpan Timeout
    {
        get => _http.Timeout;
        set => _http.Timeout = value;
    }

    /// <summary>Creates a client from the endpoint and token files in an agent folder.</summary>
    /// <param name="agentDirectory">The agent folder, <c>AppPaths.AgentDirectory</c>.</param>
    /// <returns>The client, or null when no live server is recorded.</returns>
    public static AgentClient? FromAgentDirectory(string agentDirectory)
    {
        var info = new EndpointFile(agentDirectory).Read();
        if (info is null || !File.Exists(info.TokenFile))
        {
            return null;
        }

        return new AgentClient(info.BaseUrl, File.ReadAllText(info.TokenFile).Trim());
    }

    /// <summary>Runs a tool by catalog name through its own route.</summary>
    /// <param name="tool">The catalog name, for example <c>edit.split</c>.</param>
    /// <param name="args">The arguments as a JSON object, or null.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The result (errors included; nothing is thrown for API errors).</returns>
    public Task<ApiResult> CallAsync(string tool, JsonElement? args = null, CancellationToken ct = default)
    {
        var info = ToolCatalog.Find(tool) ?? throw new ArgumentException($"Unknown tool '{tool}'.", nameof(tool));
        var a = args is { ValueKind: JsonValueKind.Object } o ? o : Tools.AgentCall.EmptyArgs;
        var used = new HashSet<string>(StringComparer.Ordinal);
        var path = ToolCatalog.FillRoute(info, name =>
        {
            used.Add(name);
            return a.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;
        });
        if (string.Equals(info.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            var query = new List<string>();
            foreach (var p in a.EnumerateObject().Where(p => !used.Contains(p.Name)))
            {
                var value = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString() ?? string.Empty,
                    JsonValueKind.Array => string.Join(',', p.Value.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText())),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => p.Value.GetRawText(),
                };
                if (value is not null)
                {
                    query.Add(Uri.EscapeDataString(p.Name) + "=" + Uri.EscapeDataString(value));
                }
            }

            return SendAsync(HttpMethod.Get, path + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty), null, ct);
        }

        return SendAsync(new HttpMethod(info.Method), path, a.GetRawText(), ct);
    }

    /// <summary>Runs a tool through <c>POST /v1/call</c>.</summary>
    /// <param name="tool">The catalog name.</param>
    /// <param name="args">The arguments, or null.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The result.</returns>
    public Task<ApiResult> CallGenericAsync(string tool, JsonElement? args = null, CancellationToken ct = default)
    {
        var body = "{\"tool\":" + JsonSerializer.Serialize(tool, AgentJsonContext.Relaxed.String) + ",\"args\":" + (args?.GetRawText() ?? "{}") + "}";
        return SendAsync(HttpMethod.Post, "v1/call", body, ct);
    }

    /// <summary>Sends a raw request and parses the response (JSON envelope, PNG or text).</summary>
    /// <param name="method">The method.</param>
    /// <param name="pathAndQuery">The path relative to the base URL, for example <c>/v1/status</c>.</param>
    /// <param name="jsonBody">The JSON body, or null.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <param name="extraHeaders">Extra request headers, or null (tests).</param>
    /// <returns>The result.</returns>
    public async Task<ApiResult> SendAsync(HttpMethod method, string pathAndQuery, string? jsonBody, CancellationToken ct, IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        using var request = new HttpRequestMessage(method, pathAndQuery.TrimStart('/'));
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        if (LockId is { } id)
        {
            request.Headers.TryAddWithoutValidation(ApiVersion.LockHeader, id);
        }

        if (AgentName is { } name)
        {
            request.Headers.TryAddWithoutValidation(ApiVersion.AgentHeader, name);
        }

        foreach (var (key, value) in extraHeaders ?? new Dictionary<string, string>())
        {
            request.Headers.Remove(key);
            request.Headers.TryAddWithoutValidation(key, value);
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var type = response.Content.Headers.ContentType?.MediaType;
        var revision = response.Headers.TryGetValues("X-Cutlass-Revision", out var revs)
            && long.TryParse(revs.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 0;
        if (response.IsSuccessStatusCode && string.Equals(type, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            JsonElement? layout = null;
            if (response.Headers.TryGetValues(ApiVersion.LayoutHeader, out var values) && values.FirstOrDefault() is { } text)
            {
                using var doc = JsonDocument.Parse(text);
                layout = doc.RootElement.Clone();
            }

            return new ApiResult { Revision = revision, Png = bytes, Result = layout };
        }

        if (response.IsSuccessStatusCode && string.Equals(type, "text/plain", StringComparison.OrdinalIgnoreCase))
        {
            return new ApiResult { Revision = revision, Text = Encoding.UTF8.GetString(bytes) };
        }

        try
        {
            return ApiResult.FromJson(Encoding.UTF8.GetString(bytes));
        }
        catch (JsonException)
        {
            // Not an envelope: http.sys or a proxy answered (for example 400 for a foreign Host).
            var code = (int)response.StatusCode switch
            {
                401 => ErrorCode.Unauthorized,
                403 => ErrorCode.Forbidden,
                404 => ErrorCode.NotFound,
                413 => ErrorCode.PayloadTooLarge,
                429 => ErrorCode.RateLimited,
                >= 500 => ErrorCode.Internal,
                _ => ErrorCode.InvalidArgument,
            };
            return ApiResult.Fail(revision, new ApiError(code, string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}")));
        }
    }

    /// <summary>Disposes the HTTP client.</summary>
    public void Dispose() => _http.Dispose();

    /// <summary><c>status</c>.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The status.</returns>
    public Task<StatusDto> StatusAsync(CancellationToken ct = default) => GetAsync("status", null, AgentJsonContext.Relaxed.StatusDto, ct);

    /// <summary><c>capabilities</c>.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The capabilities.</returns>
    public Task<CapabilitiesDto> CapabilitiesAsync(CancellationToken ct = default) => GetAsync("capabilities", null, AgentJsonContext.Relaxed.CapabilitiesDto, ct);

    /// <summary><c>lock.acquire</c>; on success <see cref="LockId"/> is set.</summary>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="reason">The reason.</param>
    /// <param name="waitMs">How long to wait.</param>
    /// <param name="leaseSeconds">The lease, or null.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The grant.</returns>
    public async Task<LockGrantDto> AcquireLockAsync(string agentName, string reason, int waitMs = 0, int? leaseSeconds = null, CancellationToken ct = default)
    {
        var req = new AcquireLockRequest { AgentName = agentName, Reason = reason, WaitMs = waitMs, LeaseSeconds = leaseSeconds };
        var grant = await GetAsync("lock.acquire", Args(req, AgentJsonContext.Relaxed.AcquireLockRequest), AgentJsonContext.Relaxed.LockGrantDto, ct).ConfigureAwait(false);
        LockId = grant.LockId;
        return grant;
    }

    /// <summary><c>lock.renew</c>.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The lease.</returns>
    public Task<LockGrantDto> RenewLockAsync(CancellationToken ct = default) => GetAsync("lock.renew", null, AgentJsonContext.Relaxed.LockGrantDto, ct);

    /// <summary><c>lock.release</c>; clears <see cref="LockId"/>.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when released.</returns>
    public async Task ReleaseLockAsync(CancellationToken ct = default)
    {
        await GetAsync("lock.release", null, AgentJsonContext.Relaxed.EmptyDto, ct).ConfigureAwait(false);
        LockId = null;
    }

    /// <summary><c>lock.get</c>.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The lock state.</returns>
    public Task<LockStateDto> GetLockAsync(CancellationToken ct = default) => GetAsync("lock.get", null, AgentJsonContext.Relaxed.LockStateDto, ct);

    /// <summary><c>events</c>.</summary>
    /// <param name="after">The last sequence number seen.</param>
    /// <param name="timeoutMs">The longest wait.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The events.</returns>
    public Task<EventsDto> EventsAsync(long after, int timeoutMs, CancellationToken ct = default) =>
        GetAsync("events", Args(new EventsRequest { After = after, TimeoutMs = timeoutMs }, AgentJsonContext.Relaxed.EventsRequest), AgentJsonContext.Relaxed.EventsDto, ct);

    /// <summary><c>shutdown</c> (serve only).</summary>
    /// <param name="save">Save first.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when acknowledged.</returns>
    public Task ShutdownAsync(bool save = false, CancellationToken ct = default) =>
        GetAsync("shutdown", Args(new ShutdownRequest { Save = save }, AgentJsonContext.Relaxed.ShutdownRequest), AgentJsonContext.Relaxed.EmptyDto, ct);

    /// <summary><c>project.get</c>.</summary>
    /// <param name="filter">Filters, or null.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The summary.</returns>
    public Task<ProjectSummaryDto> GetProjectAsync(ProjectQueryRequest? filter = null, CancellationToken ct = default) =>
        GetAsync("project.get", filter is null ? null : Args(filter, AgentJsonContext.Relaxed.ProjectQueryRequest), AgentJsonContext.Relaxed.ProjectSummaryDto, ct);

    /// <summary><c>project.describe</c>.</summary>
    /// <param name="from">Only clips ending after this frame, or null.</param>
    /// <param name="to">Only clips starting before this frame, or null.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The text.</returns>
    public async Task<string> DescribeProjectAsync(long? from = null, long? to = null, CancellationToken ct = default)
    {
        var result = await CallAsync("project.describe", Args(new ProjectQueryRequest { From = from, To = to }, AgentJsonContext.Relaxed.ProjectQueryRequest), ct).ConfigureAwait(false);
        return Ensure(result).Text ?? string.Empty;
    }

    /// <summary><c>project.new</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The summary.</returns>
    public Task<ProjectSummaryDto> NewProjectAsync(NewProjectRequest request, CancellationToken ct = default) =>
        GetAsync("project.new", Args(request, AgentJsonContext.Relaxed.NewProjectRequest), AgentJsonContext.Relaxed.ProjectSummaryDto, ct);

    /// <summary><c>project.open</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The summary.</returns>
    public Task<ProjectSummaryDto> OpenProjectAsync(OpenProjectRequest request, CancellationToken ct = default) =>
        GetAsync("project.open", Args(request, AgentJsonContext.Relaxed.OpenProjectRequest), AgentJsonContext.Relaxed.ProjectSummaryDto, ct);

    /// <summary><c>project.save</c>.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The saved path.</returns>
    public Task<PathDto> SaveProjectAsync(CancellationToken ct = default) => GetAsync("project.save", null, AgentJsonContext.Relaxed.PathDto, ct);

    /// <summary><c>project.save-as</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The saved path.</returns>
    public Task<PathDto> SaveProjectAsAsync(SaveAsRequest request, CancellationToken ct = default) =>
        GetAsync("project.save-as", Args(request, AgentJsonContext.Relaxed.SaveAsRequest), AgentJsonContext.Relaxed.PathDto, ct);

    /// <summary><c>media.list</c>.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The media.</returns>
    public Task<List<MediaDto>> ListMediaAsync(CancellationToken ct = default) => GetAsync("media.list", null, AgentJsonContext.Relaxed.ListMediaDto, ct);

    /// <summary><c>media.get</c>.</summary>
    /// <param name="mediaId">The media id.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The media.</returns>
    public Task<MediaDto> GetMediaAsync(string mediaId, CancellationToken ct = default) =>
        GetAsync("media.get", Args(new MediaIdRequest { MediaId = mediaId }, AgentJsonContext.Relaxed.MediaIdRequest), AgentJsonContext.Relaxed.MediaDto, ct);

    /// <summary><c>media.import</c>.</summary>
    /// <param name="paths">The files.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>What was imported and what failed.</returns>
    public Task<ImportResultDto> ImportMediaAsync(IEnumerable<string> paths, CancellationToken ct = default) =>
        GetAsync("media.import", Args(new ImportRequest { Paths = [.. paths] }, AgentJsonContext.Relaxed.ImportRequest), AgentJsonContext.Relaxed.ImportResultDto, ct);

    /// <summary><c>media.remove</c>.</summary>
    /// <param name="mediaId">The media id.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> RemoveMediaAsync(string mediaId, CancellationToken ct = default) =>
        MutateAsync("media.remove", new MediaIdRequest { MediaId = mediaId }, AgentJsonContext.Relaxed.MediaIdRequest, ct);

    /// <summary><c>media.peaks</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The peaks.</returns>
    public Task<PeaksDto> PeaksAsync(PeaksRequest request, CancellationToken ct = default) =>
        GetAsync("media.peaks", Args(request, AgentJsonContext.Relaxed.PeaksRequest), AgentJsonContext.Relaxed.PeaksDto, ct);

    /// <summary><c>media.levels</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The levels.</returns>
    public Task<LevelsDto> MediaLevelsAsync(MediaLevelsRequest request, CancellationToken ct = default) =>
        GetAsync("media.levels", Args(request, AgentJsonContext.Relaxed.MediaLevelsRequest), AgentJsonContext.Relaxed.LevelsDto, ct);

    /// <summary><c>frame.render</c> as PNG bytes.</summary>
    /// <param name="frame">The timeline frame.</param>
    /// <param name="width">The width, or null for 640.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The PNG.</returns>
    public async Task<byte[]> RenderFrameAsync(long frame, int? width = null, CancellationToken ct = default)
    {
        var result = await CallAsync("frame.render", Args(new RenderFrameRequest { Frame = frame, Width = width }, AgentJsonContext.Relaxed.RenderFrameRequest), ct).ConfigureAwait(false);
        return Ensure(result).Png ?? throw new InvalidOperationException("The server returned no image.");
    }

    /// <summary><c>contact-sheet</c> as PNG bytes plus the cell layout.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The PNG and the layout.</returns>
    public async Task<(byte[] Png, List<ContactCellDto> Layout)> ContactSheetAsync(ContactSheetRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = Ensure(await CallAsync("contact-sheet", Args(request, AgentJsonContext.Relaxed.ContactSheetRequest), ct).ConfigureAwait(false));
        var layout = result.Result is { ValueKind: JsonValueKind.Array } array ? array.Deserialize(AgentJsonContext.Relaxed.ListContactCellDto) ?? [] : [];
        return (result.Png ?? throw new InvalidOperationException("The server returned no image."), layout);
    }

    /// <summary><c>audio.levels</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The levels.</returns>
    public Task<LevelsDto> AudioLevelsAsync(AudioLevelsRequest request, CancellationToken ct = default) =>
        GetAsync("audio.levels", Args(request, AgentJsonContext.Relaxed.AudioLevelsRequest), AgentJsonContext.Relaxed.LevelsDto, ct);

    /// <summary><c>audio.silences</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The silences.</returns>
    public Task<SilencesDto> FindSilencesAsync(SilencesRequest request, CancellationToken ct = default) =>
        GetAsync("audio.silences", Args(request, AgentJsonContext.Relaxed.SilencesRequest), AgentJsonContext.Relaxed.SilencesDto, ct);

    /// <summary><c>playhead.set</c>.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The playhead.</returns>
    public Task<PlayheadDto> SetPlayheadAsync(long frame, CancellationToken ct = default) =>
        GetAsync("playhead.set", Args(new PlayheadRequest { Frame = frame }, AgentJsonContext.Relaxed.PlayheadRequest), AgentJsonContext.Relaxed.PlayheadDto, ct);

    /// <summary><c>edit.place</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> PlaceAsync(PlaceRequest r, CancellationToken ct = default) => MutateAsync("edit.place", r, AgentJsonContext.Relaxed.PlaceRequest, ct);

    /// <summary><c>edit.move</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> MoveAsync(MoveRequest r, CancellationToken ct = default) => MutateAsync("edit.move", r, AgentJsonContext.Relaxed.MoveRequest, ct);

    /// <summary><c>edit.trim</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> TrimAsync(TrimRequest r, CancellationToken ct = default) => MutateAsync("edit.trim", r, AgentJsonContext.Relaxed.TrimRequest, ct);

    /// <summary><c>edit.roll</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> RollAsync(RollRequest r, CancellationToken ct = default) => MutateAsync("edit.roll", r, AgentJsonContext.Relaxed.RollRequest, ct);

    /// <summary><c>edit.slip</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> SlipAsync(SlipRequest r, CancellationToken ct = default) => MutateAsync("edit.slip", r, AgentJsonContext.Relaxed.SlipRequest, ct);

    /// <summary><c>edit.split</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> SplitAsync(SplitRequest r, CancellationToken ct = default) => MutateAsync("edit.split", r, AgentJsonContext.Relaxed.SplitRequest, ct);

    /// <summary><c>edit.delete</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> DeleteAsync(DeleteRequest r, CancellationToken ct = default) => MutateAsync("edit.delete", r, AgentJsonContext.Relaxed.DeleteRequest, ct);

    /// <summary><c>edit.remove-ranges</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> RemoveRangesAsync(RemoveRangesRequest r, CancellationToken ct = default) => MutateAsync("edit.remove-ranges", r, AgentJsonContext.Relaxed.RemoveRangesRequest, ct);

    /// <summary><c>edit.close-gap</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> CloseGapAsync(CloseGapRequest r, CancellationToken ct = default) => MutateAsync("edit.close-gap", r, AgentJsonContext.Relaxed.CloseGapRequest, ct);

    /// <summary><c>edit.clip-props</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> ClipPropsAsync(ClipPropsRequest r, CancellationToken ct = default) => MutateAsync("edit.clip-props", r, AgentJsonContext.Relaxed.ClipPropsRequest, ct);

    /// <summary><c>edit.speed</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> SpeedAsync(SpeedRequest r, CancellationToken ct = default) => MutateAsync("edit.speed", r, AgentJsonContext.Relaxed.SpeedRequest, ct);

    /// <summary><c>edit.link</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> LinkAsync(ClipIdsRequest r, CancellationToken ct = default) => MutateAsync("edit.link", r, AgentJsonContext.Relaxed.ClipIdsRequest, ct);

    /// <summary><c>edit.unlink</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> UnlinkAsync(ClipIdsRequest r, CancellationToken ct = default) => MutateAsync("edit.unlink", r, AgentJsonContext.Relaxed.ClipIdsRequest, ct);

    /// <summary><c>edit.volume-keys</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> VolumeKeysAsync(VolumeKeysRequest r, CancellationToken ct = default) => MutateAsync("edit.volume-keys", r, AgentJsonContext.Relaxed.VolumeKeysRequest, ct);

    /// <summary><c>edit.transition.add</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> AddTransitionAsync(TransitionAddRequest r, CancellationToken ct = default) => MutateAsync("edit.transition.add", r, AgentJsonContext.Relaxed.TransitionAddRequest, ct);

    /// <summary><c>edit.transition.update</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> UpdateTransitionAsync(TransitionUpdateRequest r, CancellationToken ct = default) => MutateAsync("edit.transition.update", r, AgentJsonContext.Relaxed.TransitionUpdateRequest, ct);

    /// <summary><c>edit.transition.remove</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> RemoveTransitionsAsync(TransitionIdsRequest r, CancellationToken ct = default) => MutateAsync("edit.transition.remove", r, AgentJsonContext.Relaxed.TransitionIdsRequest, ct);

    /// <summary><c>edit.title.add</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> AddTitleAsync(TitleAddRequest r, CancellationToken ct = default) => MutateAsync("edit.title.add", r, AgentJsonContext.Relaxed.TitleAddRequest, ct);

    /// <summary><c>edit.title.update</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> UpdateTitleAsync(TitleUpdateRequest r, CancellationToken ct = default) => MutateAsync("edit.title.update", r, AgentJsonContext.Relaxed.TitleUpdateRequest, ct);

    /// <summary><c>edit.marker.add</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> AddMarkerAsync(MarkerAddRequest r, CancellationToken ct = default) => MutateAsync("edit.marker.add", r, AgentJsonContext.Relaxed.MarkerAddRequest, ct);

    /// <summary><c>edit.marker.update</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> UpdateMarkerAsync(MarkerUpdateRequest r, CancellationToken ct = default) => MutateAsync("edit.marker.update", r, AgentJsonContext.Relaxed.MarkerUpdateRequest, ct);

    /// <summary><c>edit.marker.remove</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> RemoveMarkersAsync(MarkerIdsRequest r, CancellationToken ct = default) => MutateAsync("edit.marker.remove", r, AgentJsonContext.Relaxed.MarkerIdsRequest, ct);

    /// <summary><c>edit.in-out</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public async Task<ApiResult> SetInOutAsync(InOutRequest r, CancellationToken ct = default) =>
        Ensure(await CallAsync("edit.in-out", JsonSerializer.SerializeToElement(r, AgentJsonContext.Relaxed.InOutRequest), ct).ConfigureAwait(false));

    /// <summary><c>edit.track.add</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> AddTrackAsync(TrackAddRequest r, CancellationToken ct = default) => MutateAsync("edit.track.add", r, AgentJsonContext.Relaxed.TrackAddRequest, ct);

    /// <summary><c>edit.track.update</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> UpdateTrackAsync(TrackUpdateRequest r, CancellationToken ct = default) => MutateAsync("edit.track.update", r, AgentJsonContext.Relaxed.TrackUpdateRequest, ct);

    /// <summary><c>edit.track.remove</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> RemoveTrackAsync(TrackIdRequest r, CancellationToken ct = default) => MutateAsync("edit.track.remove", r, AgentJsonContext.Relaxed.TrackIdRequest, ct);

    /// <summary><c>edit.sequence</c>.</summary>
    /// <param name="r">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> SetSequenceAsync(SequenceRequest r, CancellationToken ct = default) => MutateAsync("edit.sequence", r, AgentJsonContext.Relaxed.SequenceRequest, ct);

    /// <summary><c>undo</c> (agent steps only).</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> UndoAsync(CancellationToken ct = default) => MutateAsync("undo", new BatchRequest(), AgentJsonContext.Relaxed.BatchRequest, ct);

    /// <summary><c>redo</c> (agent steps only).</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> RedoAsync(CancellationToken ct = default) => MutateAsync("redo", new BatchRequest(), AgentJsonContext.Relaxed.BatchRequest, ct);

    /// <summary><c>batch.begin</c>.</summary>
    /// <param name="label">The batch label.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> BeginBatchAsync(string label, CancellationToken ct = default) => MutateAsync("batch.begin", new BatchRequest { Label = label }, AgentJsonContext.Relaxed.BatchRequest, ct);

    /// <summary><c>batch.end</c>.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> EndBatchAsync(CancellationToken ct = default) => MutateAsync("batch.end", new BatchRequest(), AgentJsonContext.Relaxed.BatchRequest, ct);

    /// <summary><c>batch.cancel</c>.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The mutation result.</returns>
    public Task<ApiResult> CancelBatchAsync(CancellationToken ct = default) => MutateAsync("batch.cancel", new BatchRequest(), AgentJsonContext.Relaxed.BatchRequest, ct);

    /// <summary><c>exports.start</c>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The export.</returns>
    public Task<ExportDto> StartExportAsync(ExportStartRequest request, CancellationToken ct = default) =>
        GetAsync("exports.start", Args(request, AgentJsonContext.Relaxed.ExportStartRequest), AgentJsonContext.Relaxed.ExportDto, ct);

    /// <summary><c>exports.list</c>.</summary>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The exports.</returns>
    public Task<List<ExportDto>> ListExportsAsync(CancellationToken ct = default) => GetAsync("exports.list", null, AgentJsonContext.Relaxed.ListExportDto, ct);

    /// <summary><c>exports.get</c>, waiting up to <paramref name="waitMs"/> for a state change.</summary>
    /// <param name="exportId">The export id.</param>
    /// <param name="waitMs">The longest wait.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The export.</returns>
    public Task<ExportDto> ExportStatusAsync(string exportId, int waitMs = 0, CancellationToken ct = default) =>
        GetAsync("exports.get", Args(new ExportIdRequest { ExportId = exportId, WaitMs = waitMs }, AgentJsonContext.Relaxed.ExportIdRequest), AgentJsonContext.Relaxed.ExportDto, ct);

    /// <summary><c>exports.cancel</c>.</summary>
    /// <param name="exportId">The export id.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The export.</returns>
    public Task<ExportDto> CancelExportAsync(string exportId, CancellationToken ct = default) =>
        GetAsync("exports.cancel", Args(new ExportIdRequest { ExportId = exportId }, AgentJsonContext.Relaxed.ExportIdRequest), AgentJsonContext.Relaxed.ExportDto, ct);

    /// <summary>Waits for an export to finish by polling <c>exports.get</c> with 20 s waits.</summary>
    /// <param name="exportId">The export id.</param>
    /// <param name="timeout">The longest total wait.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The final export state.</returns>
    public async Task<ExportDto> WaitForExportAsync(string exportId, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var dto = await ExportStatusAsync(exportId, 20_000, ct).ConfigureAwait(false);
            if (dto.State is "completed" or "failed" or "canceled" || DateTime.UtcNow >= deadline)
            {
                return dto;
            }
        }
    }

    private static JsonElement Args<T>(T value, JsonTypeInfo<T> info)
    {
        var element = JsonSerializer.SerializeToElement(value, info);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return element;
        }

        // Drop nulls so absent fields stay absent (for example In/Out keep, and GET query strings stay short).
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, AgentJsonContext.WriterOptions))
        {
            w.WriteStartObject();
            foreach (var p in element.EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Null))
            {
                p.WriteTo(w);
            }

            w.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private static ApiResult Ensure(ApiResult result) => result.Error is { } error ? throw new ApiException(error) : result;

    private async Task<T> GetAsync<T>(string tool, JsonElement? args, JsonTypeInfo<T> info, CancellationToken ct) =>
        (await CallAsync(tool, args, ct).ConfigureAwait(false)).ResultAs(info);

    private async Task<ApiResult> MutateAsync<T>(string tool, T request, JsonTypeInfo<T> info, CancellationToken ct) =>
        Ensure(await CallAsync(tool, Args(request, info), ct).ConfigureAwait(false));
}
