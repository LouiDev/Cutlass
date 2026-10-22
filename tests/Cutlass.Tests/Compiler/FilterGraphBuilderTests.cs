using Cutlass.Compiler.Graph;

namespace Cutlass.Tests.Compiler;

/// <summary>FilterGraphBuilder: chain order, open chains, label bookkeeping (TECH_BRIEF 7.7).</summary>
public sealed class FilterGraphBuilderTests
{
    [Fact]
    public void ChainsAreWrittenInStartOrderOnePerLine()
    {
        var graph = new FilterGraphBuilder();
        var base0 = graph.StartSource("color=c=black:s=16x16:r=30").Then("format=yuv420p");
        var seg = graph.StartInput(0, 0).Then("setsar=1");
        seg.Close("v0r0");
        base0.Close("base");
        graph.StartJoin(["base", "v0r0"], "overlay=x=0:y=0").Close("vout");

        Assert.Equal(
            "color=c=black:s=16x16:r=30,format=yuv420p[base];\n[0:0]setsar=1[v0r0];\n[base][v0r0]overlay=x=0:y=0[vout]",
            graph.Build(["vout"]));
        Assert.Equal(3, graph.ChainCount);
    }

    [Fact]
    public void MidChainInputPutsTheLabelFirst()
    {
        var graph = new FilterGraphBuilder();
        graph.StartInput(1, 0).Then("scale=w=8:h=8").ThenWithMainInput("cv", "overlay=x=4:y=4").Close("vout");
        graph.StartSource("color=c=black@0:s=16x16").Close("cv");

        Assert.Equal("[1:0]scale=w=8:h=8,[cv]overlay=x=4:y=4[vout];\ncolor=c=black@0:s=16x16[cv]", graph.Build(["vout"]));
    }

    [Fact]
    public void SplitHasSeveralOutputs()
    {
        var graph = new FilterGraphBuilder();
        graph.StartSource("nullsrc").Then("split").Close(["a", "b"]);
        graph.StartJoin(["a", "b"], "hstack").Close("vout");

        Assert.Equal("nullsrc,split[a][b];\n[a][b]hstack[vout]", graph.Build(["vout"]));
    }

    [Fact]
    public void OpenChainsAndDanglingLabelsAreBugs()
    {
        var open = new FilterGraphBuilder();
        open.StartSource("nullsrc");
        Assert.Throws<InvalidOperationException>(() => open.Build([]));

        var unused = new FilterGraphBuilder();
        unused.StartSource("nullsrc").Close("x");
        unused.StartSource("nullsrc").Close("vout");
        Assert.Throws<InvalidOperationException>(() => unused.Build(["vout"]));

        var undefined = new FilterGraphBuilder();
        undefined.StartJoin(["ghost"], "null").Close("vout");
        Assert.Throws<InvalidOperationException>(() => undefined.Build(["vout"]));

        var twice = new FilterGraphBuilder();
        twice.StartSource("nullsrc").Close("vout");
        twice.StartSource("nullsrc").Close("vout");
        Assert.Throws<InvalidOperationException>(() => twice.Build(["vout"]));

        Assert.Throws<InvalidOperationException>(() => new FilterGraphBuilder().Build([]));
    }

    [Fact]
    public void ClosedChainsRejectMoreFilters()
    {
        var graph = new FilterGraphBuilder();
        var chain = graph.StartSource("nullsrc");
        chain.Close("vout");

        Assert.True(chain.IsClosed);
        Assert.Throws<InvalidOperationException>(() => chain.Then("null"));
        Assert.Throws<InvalidOperationException>(() => graph.StartInput(0, 0).ThenWithMainInput("x", "overlay"));
        Assert.Throws<ArgumentException>(() => graph.StartSource("nullsrc").Close("bad label"));
    }
}
