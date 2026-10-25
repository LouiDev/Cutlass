using Cutlass.Core.Model;

namespace Cutlass.Tests.Model;

/// <summary>The built-in title templates (effects batch 4) shared by the Effects tab and the agent.</summary>
public sealed class TitleTemplatesTests
{
    [Fact]
    public void ListsTheSixTemplatesInOrder() =>
        Assert.Equal(["Centered", "LowerThird", "TopCaption", "Subtitle", "Headline", "Quote"], TitleTemplates.Names.ToArray());

    [Fact]
    public void LooksTemplatesUpIgnoringCase()
    {
        Assert.True(TitleTemplates.TryGet("lowerthird", out var lower));
        Assert.Equal(("Name", "builtin:Inter-SemiBold", 64.0, true, TitleAnchor.LowerThird), (lower.Text, lower.Font, lower.Size, lower.Box, lower.Anchor));
        Assert.True(TitleTemplates.TryGet("Subtitle", out var subtitle));
        Assert.Equal(("Subtitle", "builtin:Inter-Regular", 44.0, true, "#000000A0", 14.0, TitleAnchor.LowerThird), (subtitle.Text, subtitle.Font, subtitle.Size, subtitle.Box, subtitle.BoxColor, subtitle.BoxPadding, subtitle.Anchor));
        Assert.True(TitleTemplates.TryGet("Headline", out var headline));
        Assert.Equal(("Headline", "builtin:Inter-Bold", 120.0, 4.0, false, TitleAnchor.Top), (headline.Text, headline.Font, headline.Size, headline.OutlineWidth, headline.Box, headline.Anchor));
        Assert.True(TitleTemplates.TryGet("QUOTE", out var quote));
        Assert.Equal(("Quote", "builtin:Inter-SemiBold", 72.0, true, "#00000080", 28.0, TitleAnchor.Center), (quote.Text, quote.Font, quote.Size, quote.Box, quote.BoxColor, quote.BoxPadding, quote.Anchor));
        Assert.False(TitleTemplates.TryGet(null, out var fallback));
        Assert.Equal(TitleSpec.Default, fallback);
        Assert.False(TitleTemplates.TryGet("Banner", out _));
        Assert.All(TitleTemplates.Names, n => Assert.Equal("#FFFFFFFF", Spec(n).Color));
    }

    [Fact]
    public void DisplayNamesAndDescriptionsExistForEveryTemplate()
    {
        Assert.Equal("Lower Third", TitleTemplates.DisplayName("LowerThird"));
        Assert.Equal("Top Caption", TitleTemplates.DisplayName("TopCaption"));
        Assert.Equal("Banner", TitleTemplates.DisplayName("Banner"));
        Assert.Equal(string.Empty, TitleTemplates.Description("Banner"));
        Assert.All(TitleTemplates.Names, n => Assert.NotEmpty(TitleTemplates.Description(n)));
    }

    [Fact]
    public void ScaleFollowsTheSequenceHeight()
    {
        var spec = Spec("LowerThird");
        Assert.Same(spec, TitleTemplates.Scale(spec, 1080));
        var half = TitleTemplates.Scale(spec, 540);
        Assert.Equal((32.0, 1.5, 8.0), (half.Size, half.OutlineWidth, half.BoxPadding));
        var uhd = TitleTemplates.Scale(Spec("Headline"), 2160);
        Assert.Equal((240.0, 8.0), (uhd.Size, uhd.OutlineWidth));
        Assert.Equal(1, TitleTemplates.Scale(spec, 8).Size);
    }

    private static TitleSpec Spec(string name)
    {
        Assert.True(TitleTemplates.TryGet(name, out var spec));
        return spec;
    }
}
