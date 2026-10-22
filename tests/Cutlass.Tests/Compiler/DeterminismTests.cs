using System.Globalization;
using Cutlass.Compiler.Emit;
using Cutlass.Compiler.Export;
using Cutlass.Core.Serialization;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Compiler;

/// <summary>
/// The compiler is pure and deterministic (TECH_BRIEF 7.1): the same input gives byte-identical output, also after
/// a round trip through the project serializer and under any culture.
/// </summary>
public sealed class DeterminismTests
{
    [Fact]
    public void FiftyCompilesGiveIdenticalBytes()
    {
        foreach (var c in GoldenCases.All)
        {
            var first = TimelineCompiler.Compile(c.Project, c.Catalog, c.Request);
            for (var i = 0; i < 50; i++)
            {
                var again = TimelineCompiler.Compile(c.Project, c.Catalog, c.Request);
                Assert.Equal(first.GraphText, again.GraphText);
                Assert.Equal(first.Inputs, again.Inputs);
                Assert.Equal(first.AuxFiles, again.AuxFiles);
                Assert.Equal(first.Warnings, again.Warnings);
                Assert.Equal(first.FrameCount, again.FrameCount);
                Assert.Equal(first.SampleCount, again.SampleCount);
            }
        }
    }

    [Fact]
    public void SerializerRoundTripCompilesIdentically()
    {
        foreach (var c in GoldenCases.All)
        {
            var before = TimelineCompiler.Compile(c.Project, c.Catalog, c.Request);
            var restored = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(c.Project));

            var after = TimelineCompiler.Compile(restored, c.Catalog, c.Request);

            Assert.Equal(before.GraphText, after.GraphText);
            Assert.Equal(Golden.InputsText(before), Golden.InputsText(after));
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    [InlineData("")]
    public void CultureNeverChangesTheOutput(string culture)
    {
        var c = GoldenCases.Get("worked_example_7_8");
        var invariant = WithCulture(CultureInfo.InvariantCulture, () => TimelineCompiler.Compile(c.Project, c.Catalog, c.Request));

        var localized = WithCulture(CultureInfo.GetCultureInfo(culture), () => TimelineCompiler.Compile(c.Project, c.Catalog, c.Request));

        Assert.Equal(invariant.GraphText, localized.GraphText);
        Assert.Equal(Golden.InputsText(invariant), Golden.InputsText(localized));
    }

    [Fact]
    [UseCulture("de-DE")]
    public void ExportArgumentsAreInvariantUnderGerman()
    {
        var c = GoldenCases.Get("speeds");
        var render = TimelineCompiler.Compile(c.Project, c.Catalog, c.Request);
        var encoding = ExportPresets.H264.Resolve(WorkedExampleTests.MasterCapabilities())!;

        var args = FfmpegArgsBuilder.Export(render, encoding, ExportQuality.Good, c.Project.Sequence.Settings.Rate, 1920, 1080, ["-/filter_complex", "g.ffg"], "D:/o.partial.mp4");

        Assert.Contains("2.101000", args);
        Assert.Contains("1.801000", args); // The audio seek, 0.2 s earlier for the atempo pre-roll.
        Assert.DoesNotContain(args, a => a.Length > 0 && char.IsDigit(a[^1]) && a.Contains(',', StringComparison.Ordinal));
        Assert.Contains("atempo=0.500000", render.GraphText, StringComparison.Ordinal);
        Assert.Contains("setpts=(PTS-705600)/0.250000", render.GraphText, StringComparison.Ordinal);
    }

    private static T WithCulture<T>(CultureInfo culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
