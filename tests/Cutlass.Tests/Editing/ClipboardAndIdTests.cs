using System.Text.RegularExpressions;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

public sealed partial class ClipboardAndIdTests
{
    private readonly SequentialIdGenerator _ids = new(1000);

    [Fact]
    public void CopyKeepsRelativeOffsetsAndTracks()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 100, 20, link: "l1"), V("c2", 140, 10, sourceFrame: 300)).With(A1, A("a1", 110, 20, link: "l1")).Build();
        var clipboard = ClipClipboard.Copy(p, ["a1", "c2", "c1"]);
        Assert.Equal([(V1, 0L), (V1, 40L), (A1, 10L)], clipboard.Items.Select(i => (i.TrackId, i.Clip.Start)));
        Assert.True(ClipClipboard.Copy(p, ["nope"]).IsEmpty);
    }

    [Fact]
    public void PasteOverwritesAtThePlayheadWithNewIdsAndLinks()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 20, link: "l1"), V("c9", 500, 100, sourceFrame: 1000)).With(A1, A("a1", 0, 20, link: "l1")).Build();
        var clipboard = ClipClipboard.Copy(p, ["c1", "a1"]);
        var r = clipboard.Paste(_ids, p, 510, EditMode.Overwrite);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal("Paste", r.Label);
        Assert.Equal(2, r.Select.Length);
        var pasted = r.Select.Select(id => ModelAssert.Clip(q, id)).ToList();
        Assert.All(pasted, c => Assert.Equal(510, c.Start));
        Assert.Equal(pasted[0].LinkId, pasted[1].LinkId);
        Assert.NotEqual("l1", pasted[0].LinkId);
        Assert.Equal([(500L, 10L), (510L, 20L), (530L, 70L)], ModelAssert.Spans(q, V1)[1..]);
    }

    [Fact]
    public void PasteInsertShiftsLaterClips()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 20), V("c2", 20, 20, sourceFrame: 300)).Build();
        var q = ModelAssert.Changed(p, ClipClipboard.Copy(p, ["c1"]).Paste(_ids, p, 20, EditMode.Insert));
        Assert.Equal([(0L, 20L), (20L, 20L), (40L, 20L)], ModelAssert.Spans(q, V1));
        Assert.Equal(40, ModelAssert.Clip(q, "c2").Start);
    }

    [Fact]
    public void PasteFallsBackToAnUnlockedTrackOfTheSameKind()
    {
        var p = new ProjectBuilder().With(A2, A("a1", 0, 20)).Build();
        var clipboard = ClipClipboard.Copy(p, ["a1"]);
        var removed = ModelAssert.Changed(p, TimelineEdits.RemoveTrack(p, A2));
        var q = ModelAssert.Changed(removed, clipboard.Paste(_ids, removed, 0, EditMode.Overwrite));
        Assert.Single(ModelAssert.Track(q, A1).Clips);
        Assert.Same(p, ClipClipboard.Empty.Paste(_ids, p, 0, EditMode.Overwrite).Project);
    }

    [Fact]
    public void RandomIdsHaveThePrefixAndEightBase32Characters()
    {
        var generator = new RandomIdGenerator();
        var ids = Enumerable.Range(0, 10_000).Select(_ => generator.NewId(IIdGenerator.ClipPrefix)).ToList();
        Assert.All(ids, id => Assert.Matches(IdPattern(), id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.StartsWith("tr_", RandomIdGenerator.Shared.NewId(IIdGenerator.TrackPrefix));
    }

    [Fact]
    public void SequentialIdsAreDeterministic()
    {
        var generator = new SequentialIdGenerator();
        Assert.Equal("c_00000001", generator.NewId(IIdGenerator.ClipPrefix));
        Assert.Equal("l_00000002", generator.NewId(IIdGenerator.LinkPrefix));
    }

    [GeneratedRegex("^c_[0-9abcdefghjkmnpqrstvwxyz]{8}$")]
    private static partial Regex IdPattern();
}
