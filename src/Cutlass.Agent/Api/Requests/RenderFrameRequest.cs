namespace Cutlass.Agent.Api.Requests;

/// <summary><c>GET /v1/frame/{n}</c>.</summary>
public sealed class RenderFrameRequest
{
    /// <summary>The timeline frame.</summary>
    public long? Frame { get; set; }

    /// <summary>Image width, 16 to 1920, even (640).</summary>
    public int? Width { get; set; }

    /// <summary>Render from proxies.</summary>
    public bool? UseProxies { get; set; }

    /// <summary><c>png</c> or <c>base64</c>.</summary>
    public string? Format { get; set; }
}
