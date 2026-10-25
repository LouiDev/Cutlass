namespace Cutlass.Agent.Api;

/// <summary>The agent API version (AGENT_INTEGRATION 4.2).</summary>
public static class ApiVersion
{
    /// <summary>The major version; paths start with <c>/v1</c>. Breaking changes go to <c>/v2</c>.</summary>
    public const int Current = 1;

    /// <summary>
    /// The additive revision reported by <c>/v1/capabilities</c>; raised by every additive change. 2 added the discovery
    /// routes and MCP names in <c>/v1/call</c> (AGENT_INTEGRATION 9.2). 3 added the extra xfade transition kinds
    /// (<c>wipeUp</c> to <c>zoomIn</c>) to <c>capabilities.transitionKinds</c> and the <c>kind</c> enums. 4 added the
    /// <c>effects</c> object (flips, rotation, hue, temperature, tint, blur, sharpen, vignette, grayscale, chroma key)
    /// to <c>edit.clip-props</c> and to the <c>video</c> block of every video clip in the project summary. 5 added the
    /// <c>audioEffects</c> object (high-pass, low-pass, bass, treble, compressor, gate, pan, mono) to <c>edit.clip-props</c>
    /// and <c>effects</c> to the <c>audio</c> block of every audio clip in the project summary. 6 added <c>edit.freeze</c>
    /// (<c>cutlass_freeze_frame</c>), the <c>freeze</c> flag of <c>edit.clip-props</c> and of frozen media clips in the
    /// project summary, the <c>template</c> of <c>edit.title.add</c> and <c>capabilities.titleTemplates</c>.
    /// </summary>
    public const int Revision = 6;

    /// <summary>The route prefix of this version.</summary>
    public const string Prefix = "/v1";

    /// <summary>The header that carries the lock id.</summary>
    public const string LockHeader = "X-Cutlass-Lock";

    /// <summary>The optional header that names the calling agent.</summary>
    public const string AgentHeader = "X-Cutlass-Agent";

    /// <summary>The header that carries a contact sheet layout (JSON) next to its PNG.</summary>
    public const string LayoutHeader = "X-Cutlass-Layout";
}
