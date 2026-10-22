using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cutlass.Agent.Api.Dto;
using Cutlass.Agent.Api.Requests;
using Cutlass.Agent.Http;

namespace Cutlass.Agent.Api;

/// <summary>
/// Source-generated JSON metadata of the agent API (AGENT_INTEGRATION 4.2): camelCase, unknown fields ignored,
/// numbers also read from strings (query parameters), invariant numbers. Enum-like fields are strings in the DTOs, so
/// their spelling is explicit. Write through <see cref="Relaxed"/> (or <see cref="Indented"/>): they use
/// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> like <c>ProjectSerializer</c>, so quotes, <c>+</c>,
/// <c>&lt;</c> and non-ASCII text stay readable. Responses are localhost JSON over HTTP or MCP, never embedded in HTML.
/// <see cref="JsonSerializerContext"/>'s generated <c>Default</c> keeps the default encoder; use it only to read, or
/// where the text must stay ASCII (an HTTP header).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ApiEnvelopeDto))]
[JsonSerializable(typeof(ApiErrorDto))]
[JsonSerializable(typeof(StatusDto))]
[JsonSerializable(typeof(LockStateDto))]
[JsonSerializable(typeof(LockGrantDto))]
[JsonSerializable(typeof(EmptyDto))]
[JsonSerializable(typeof(CapabilitiesDto))]
[JsonSerializable(typeof(ProjectSummaryDto))]
[JsonSerializable(typeof(List<TrackSummaryDto>))]
[JsonSerializable(typeof(MediaDto))]
[JsonSerializable(typeof(List<MediaDto>))]
[JsonSerializable(typeof(ImportResultDto))]
[JsonSerializable(typeof(PeaksDto))]
[JsonSerializable(typeof(LevelsDto))]
[JsonSerializable(typeof(SilencesDto))]
[JsonSerializable(typeof(FrameDto))]
[JsonSerializable(typeof(ContactSheetDto))]
[JsonSerializable(typeof(List<ContactCellDto>))]
[JsonSerializable(typeof(PlayheadDto))]
[JsonSerializable(typeof(PathDto))]
[JsonSerializable(typeof(ExportDto))]
[JsonSerializable(typeof(List<ExportDto>))]
[JsonSerializable(typeof(EventsDto))]
[JsonSerializable(typeof(EditOutcomeDto))]
[JsonSerializable(typeof(EndpointInfo))]
[JsonSerializable(typeof(AcquireLockRequest))]
[JsonSerializable(typeof(ShutdownRequest))]
[JsonSerializable(typeof(ProjectQueryRequest))]
[JsonSerializable(typeof(NewProjectRequest))]
[JsonSerializable(typeof(OpenProjectRequest))]
[JsonSerializable(typeof(SaveAsRequest))]
[JsonSerializable(typeof(MediaIdRequest))]
[JsonSerializable(typeof(ImportRequest))]
[JsonSerializable(typeof(PeaksRequest))]
[JsonSerializable(typeof(MediaLevelsRequest))]
[JsonSerializable(typeof(RenderFrameRequest))]
[JsonSerializable(typeof(ContactSheetRequest))]
[JsonSerializable(typeof(AudioLevelsRequest))]
[JsonSerializable(typeof(SilencesRequest))]
[JsonSerializable(typeof(PlayheadRequest))]
[JsonSerializable(typeof(EventsRequest))]
[JsonSerializable(typeof(CallRequest))]
[JsonSerializable(typeof(PlaceRequest))]
[JsonSerializable(typeof(MoveRequest))]
[JsonSerializable(typeof(TrimRequest))]
[JsonSerializable(typeof(RollRequest))]
[JsonSerializable(typeof(SlipRequest))]
[JsonSerializable(typeof(SplitRequest))]
[JsonSerializable(typeof(DeleteRequest))]
[JsonSerializable(typeof(RemoveRangesRequest))]
[JsonSerializable(typeof(CloseGapRequest))]
[JsonSerializable(typeof(ClipPropsRequest))]
[JsonSerializable(typeof(SpeedRequest))]
[JsonSerializable(typeof(ClipIdsRequest))]
[JsonSerializable(typeof(VolumeKeysRequest))]
[JsonSerializable(typeof(TransitionAddRequest))]
[JsonSerializable(typeof(TransitionUpdateRequest))]
[JsonSerializable(typeof(TransitionIdsRequest))]
[JsonSerializable(typeof(TitleAddRequest))]
[JsonSerializable(typeof(TitleUpdateRequest))]
[JsonSerializable(typeof(MarkerAddRequest))]
[JsonSerializable(typeof(MarkerUpdateRequest))]
[JsonSerializable(typeof(MarkerIdsRequest))]
[JsonSerializable(typeof(InOutRequest))]
[JsonSerializable(typeof(TrackAddRequest))]
[JsonSerializable(typeof(TrackUpdateRequest))]
[JsonSerializable(typeof(TrackIdRequest))]
[JsonSerializable(typeof(SequenceRequest))]
[JsonSerializable(typeof(BatchRequest))]
[JsonSerializable(typeof(ExportStartRequest))]
[JsonSerializable(typeof(ExportIdRequest))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(JsonElement))]
public sealed partial class AgentJsonContext : JsonSerializerContext
{
    private static AgentJsonContext? _relaxed;
    private static AgentJsonContext? _indented;

    /// <summary>The encoder of every agent API write: relaxed, because the JSON is never embedded in HTML.</summary>
    public static JavaScriptEncoder Encoder => JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>Writer options with <see cref="Encoder"/>, for hand-written JSON.</summary>
    public static JsonWriterOptions WriterOptions => new() { Encoder = Encoder };

    /// <summary>The metadata with the relaxed <see cref="Encoder"/>; every response and request is written through it.</summary>
    public static AgentJsonContext Relaxed =>
        _relaxed ??= new AgentJsonContext(new JsonSerializerOptions(Default.Options) { Encoder = Encoder });

    /// <summary>The same metadata, writing indented text with the relaxed <see cref="Encoder"/>.</summary>
    public static AgentJsonContext Indented =>
        _indented ??= new AgentJsonContext(new JsonSerializerOptions(Default.Options) { WriteIndented = true, Encoder = Encoder });
}
