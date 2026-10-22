using System.Collections.Immutable;

namespace Cutlass.App.ViewModels;

/// <summary>A named sequence frame size for the inspector and the sequence settings dialog.</summary>
/// <param name="Name">The menu text.</param>
/// <param name="Width">Width in pixels (0 for "Custom").</param>
/// <param name="Height">Height in pixels (0 for "Custom").</param>
public sealed record SequenceSizePreset(string Name, int Width, int Height)
{
    /// <summary>The "Custom" entry shown for any other size.</summary>
    public static SequenceSizePreset Custom { get; } = new("Custom", 0, 0);

    /// <summary>The presets: 1080p, 4K UHD, vertical 1080x1920, 720p, square, then Custom.</summary>
    public static ImmutableArray<SequenceSizePreset> All { get; } =
    [
        new("1080p (1920 x 1080)", 1920, 1080),
        new("4K UHD (3840 x 2160)", 3840, 2160),
        new("Vertical (1080 x 1920)", 1080, 1920),
        new("720p (1280 x 720)", 1280, 720),
        new("Square (1080 x 1080)", 1080, 1080),
        Custom,
    ];

    /// <summary>The preset with this size, or <see cref="Custom"/>.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The preset.</returns>
    public static SequenceSizePreset Find(int width, int height) =>
        All.FirstOrDefault(p => p.Width == width && p.Height == height) ?? Custom;

    /// <inheritdoc />
    public override string ToString() => Name;
}
