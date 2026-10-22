using Cutlass.Core.Model;

namespace Cutlass.Compiler;

/// <summary>Everything the compiler needs about one media asset (TECH_BRIEF 7.1).</summary>
/// <param name="Asset">The asset with its probe results.</param>
/// <param name="Original">The original file.</param>
/// <param name="Proxy">The video proxy, or null when there is none (or it was rejected).</param>
public sealed record MediaRenderInfo(MediaAsset Asset, RenderSource Original, RenderSource? Proxy);
