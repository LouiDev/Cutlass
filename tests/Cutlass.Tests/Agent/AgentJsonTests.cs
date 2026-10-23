using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto;
using Cutlass.Agent.Api.Dto.Media;
using Cutlass.Agent.Api.Dto.Preview;
using Cutlass.Agent.Api.Dto.Project;
using Cutlass.Agent.Api.Dto.Status;
using Cutlass.Agent.Api.Requests;
using Cutlass.Agent.Api.Requests.Clips;
using Cutlass.Agent.Api.Requests.Media;
using Cutlass.Agent.Api.Requests.Preview;
using Cutlass.Agent.Api.Requests.Project;
using Cutlass.Agent.Api.Requests.Titles;
using Cutlass.Agent.Api.Requests.Tracks;
using Cutlass.Agent.Tools.Dispatch;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Agent;

/// <summary>JSON of the agent API (AGENT_INTEGRATION 4.2 and 7.1): round trips under de-DE, spellings, unknown fields.</summary>
[UseCulture("de-DE")]
public sealed class AgentJsonTests
{
    private static void RoundTrip<T>(T value, JsonTypeInfo<T> info)
    {
        var json = JsonSerializer.Serialize(value, info);
        var back = JsonSerializer.Deserialize(json, info);
        Assert.Equal(json, JsonSerializer.Serialize(back!, info));
        Assert.DoesNotContain("\u2014", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDtoRoundTripsWithInvariantNumbers()
    {
        var c = AgentJsonContext.Default;
        RoundTrip(new StatusDto { Version = "1.2.3", Project = new ProjectStatusDto { Path = "D:/a.cutlass", OfflineMediaIds = ["m_1"] }, Playhead = 12, Batch = "Cut" }, c.StatusDto);
        RoundTrip(new LockStateDto { Phase = "held", AgentName = "A", Since = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123), LeaseSeconds = 90 }, c.LockStateDto);
        RoundTrip(new LockGrantDto { LockId = "abc", LeaseSeconds = 90, LeaseExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000) }, c.LockGrantDto);
        RoundTrip(new CapabilitiesDto { Presets = [new PresetDto { Id = "h264", Name = "H.264", Available = true }], Qualities = ["high"] }, c.CapabilitiesDto);
        RoundTrip(new LevelsDto { StartFrame = 3, IntervalMs = 50, RmsDb = [-3.01, -120], PeakDb = [0.5, -120] }, c.LevelsDto);
        RoundTrip(new SilencesDto { Ranges = [new SilenceRangeDto { Start = 10, End = 20, Seconds = 0.4 }] }, c.SilencesDto);
        RoundTrip(new PeaksDto { PeaksPerSecond = 12.5, Min = [-0.25], Max = [0.75] }, c.PeaksDto);
        RoundTrip(new FrameDto { Width = 640, Height = 360, Frame = 60, Timecode = "00:00:02:10" }, c.FrameDto);
        RoundTrip(new ContactSheetDto { Width = 642, Height = 180, Layout = [new ContactCellDto { Frame = 1, X = 322 }] }, c.ContactSheetDto);
        RoundTrip(new ExportDto { Id = "x", State = "running", Fraction = 0.5, EtaSeconds = 1.5, Speed = 2.25, Warnings = ["w"] }, c.ExportDto);
        RoundTrip(new EventsDto { Events = [new EventDto { Seq = 1, Kind = "projectChanged", Revision = 3, By = "agent" }], Next = 1 }, c.EventsDto);
        RoundTrip(new ImportResultDto { Imported = [new MediaDto { Id = "m_1", DurationSeconds = 64.2 }], Failed = [new ImportFailureDto { Path = "x", Message = "y" }] }, c.ImportResultDto);
        RoundTrip(new EditOutcomeDto { Label = "Split", Batch = "Cut" }, c.EditOutcomeDto);
        RoundTrip(new PathDto { Path = "D:/x.cutlass" }, c.PathDto);
        RoundTrip(new PlayheadDto { Frame = 9 }, c.PlayheadDto);
        RoundTrip(new ProjectSummaryDto { Sequence = new SequenceSummaryDto { Fps = 29.97, FrameRate = "30000/1001" } }, c.ProjectSummaryDto);
        var json = JsonSerializer.Serialize(new LevelsDto { RmsDb = [-3.01] }, c.LevelsDto);
        Assert.Contains("-3.01", json, StringComparison.Ordinal);
        Assert.Contains("\"rmsDb\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRequestRoundTrips()
    {
        var c = AgentJsonContext.Default;
        RoundTrip(new AcquireLockRequest { AgentName = "A", Reason = "B", WaitMs = 1, LeaseSeconds = 60 }, c.AcquireLockRequest);
        RoundTrip(new PlaceRequest { MediaId = "m", At = 1, Mode = "insert", SourceInSeconds = 1.5, IfRevision = 3, ReturnProject = true }, c.PlaceRequest);
        RoundTrip(new MoveRequest { ClipIds = ["c"], DeltaFrames = -3 }, c.MoveRequest);
        RoundTrip(new TrimRequest { ClipId = "c", Edge = "end", ToFrame = 4 }, c.TrimRequest);
        RoundTrip(new RemoveRangesRequest { Ranges = [new FrameRange { Start = 1, End = 2 }] }, c.RemoveRangesRequest);
        RoundTrip(new ClipPropsRequest { ClipIds = ["c"], Transform = new TransformDto { Scale = 0.5 }, Color = new ColorDto { Gamma = 1.2 } }, c.ClipPropsRequest);
        RoundTrip(new VolumeKeysRequest { ClipId = "c", Keys = [new VolumeKeyDto { Frame = 1, Db = -6.5 }] }, c.VolumeKeysRequest);
        RoundTrip(new TitleAddRequest { TrackId = "t", At = 0, DurationFrames = 5, Title = new TitleDto { Text = "Hi", Anchor = "lowerThird" } }, c.TitleAddRequest);
        RoundTrip(new TrackUpdateRequest { TrackId = "t", GainDb = -3.5 }, c.TrackUpdateRequest);
        RoundTrip(new SequenceRequest { FrameRate = "30000/1001" }, c.SequenceRequest);
        RoundTrip(new ExportStartRequest { Preset = "h264", OutputPath = "D:/x.mp4" }, c.ExportStartRequest);
        RoundTrip(new ContactSheetRequest { Frames = [1, 2], CellWidth = 320 }, c.ContactSheetRequest);
        RoundTrip(new SilencesRequest { ThresholdDb = -40.5 }, c.SilencesRequest);
    }

    [Fact]
    public void EnumSpellingsAreCamelCase()
    {
        Assert.Equal("dipToBlack", EnumWire.Name(TransitionKind.DipToBlack));
        Assert.Equal("lowerThird", EnumWire.Name(TitleAnchor.LowerThird));
        Assert.Equal("audioCrossfade", EnumWire.Name(TransitionKind.AudioCrossfade));
        Assert.Equal(TitleAnchor.LowerThird, EnumWire.Parse("lowerThird", "anchor", TitleAnchor.Center));
        Assert.Equal(TitleAnchor.Center, EnumWire.Parse<TitleAnchor>(null, "anchor", TitleAnchor.Center));
        var ex = Assert.Throws<ApiException>(() => EnumWire.Parse("sideways", "anchor", TitleAnchor.Center));
        Assert.Equal(ErrorCode.InvalidArgument, ex.Code);
        Assert.Equal("anchor", ex.Error.Detail("field"));
    }

    [Fact]
    public void ErrorCodesUseSnakeCaseAndTheirStatus()
    {
        Assert.Equal("lock_required", ApiError.WireName(ErrorCode.LockRequired));
        Assert.Equal(423, ApiError.StatusOf(ErrorCode.LockLost));
        Assert.Equal(413, ApiError.StatusOf(ErrorCode.PayloadTooLarge));
        foreach (var code in Enum.GetValues<ErrorCode>())
        {
            Assert.True(ApiError.TryParseCode(ApiError.WireName(code), out var back));
            Assert.Equal(code, back);
        }
    }

    [Fact]
    public void TheErrorEnvelopeHasTheSpecShape()
    {
        var result = ApiResult.Fail(57, new ApiError(ErrorCode.Paused, "Paused.", null, 2000));
        using var doc = JsonDocument.Parse(result.ToJson());
        var root = doc.RootElement;
        Assert.Equal(57, root.GetProperty("revision").GetInt64());
        var error = root.GetProperty("error");
        Assert.Equal("paused", error.GetProperty("code").GetString());
        Assert.Equal(2000, error.GetProperty("retryAfterMs").GetInt32());
        Assert.False(root.TryGetProperty("result", out _));
        var back = ApiResult.FromJson(result.ToJson());
        Assert.Equal(ErrorCode.Paused, back.Error!.Code);
        Assert.Equal(2000, back.Error.RetryAfterMs);
    }

    [Fact]
    public void FlicksOrSecondsButNeverBoth()
    {
        Assert.Equal(705_600_000L, Args.Flicks(null, 1.0, "sourceInFlicks"));
        Assert.Equal(1L, Args.Flicks(1, null, "sourceInFlicks"));
        Assert.Null(Args.Flicks(null, null, "sourceInFlicks"));
        Assert.Equal(352_800_000L, Args.Flicks(null, 0.5, "sourceInFlicks"));
        var ex = Assert.Throws<ApiException>(() => Args.Flicks(1, 1.0, "sourceInFlicks"));
        Assert.Equal("sourceInFlicks", ex.Error.Detail("field"));
        Assert.Contains("sourceInSeconds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownFieldsAreIgnoredAndNumbersMayBeStrings()
    {
        var req = JsonSerializer.Deserialize("""{"frame":"60","width":320,"bogus":{"a":1},"format":"base64"}""", AgentJsonContext.Default.RenderFrameRequest)!;
        Assert.Equal(60, req.Frame);
        Assert.Equal(320, req.Width);
        Assert.Equal("base64", req.Format);
    }

    [Fact]
    public void InOutDistinguishesAbsentFromNull()
    {
        var req = JsonSerializer.Deserialize("""{"inFrame":null}""", AgentJsonContext.Default.InOutRequest)!;
        Assert.Equal(JsonValueKind.Null, req.InFrame.ValueKind);
        Assert.Equal(JsonValueKind.Undefined, req.OutFrame.ValueKind);
        var json = JsonSerializer.Serialize(req, AgentJsonContext.Default.InOutRequest);
        Assert.Contains("\"inFrame\":null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("outFrame", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsesUseTheRelaxedEncoderLikeTheProjectFile()
    {
        // Localhost JSON, never embedded in HTML: quotes, plus signs, angle brackets and umlauts stay readable.
        const string text = "Ana's <b>+1</b> \"Grüße\" & co";
        var ok = ApiResult.Ok(3, new PathDto { Path = text }, AgentJsonContext.Relaxed.PathDto, [text]);
        var json = ok.ToJson();
        Assert.Contains("Ana's <b>+1</b> \\\"Grüße\\\" & co", json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\u00", json, StringComparison.Ordinal);
        Assert.Contains("Ana's", ok.Result!.Value.GetRawText(), StringComparison.Ordinal);

        var fail = ApiResult.Fail(3, new ApiError(ErrorCode.Forbidden, text, new Dictionary<string, string> { ["path"] = "D:/a+b" }));
        Assert.Contains("D:/a+b", fail.ToJson(), StringComparison.Ordinal);
        Assert.Equal(text, ApiResult.FromJson(json).Warnings[0]);

        Assert.DoesNotContain(@"\u0027", new SchemaBuilder().Str("name", "The clip's name").Build(), StringComparison.Ordinal);
    }
}
