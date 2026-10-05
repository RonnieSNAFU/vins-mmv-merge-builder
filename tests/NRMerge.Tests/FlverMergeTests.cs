using NRMerge;
using SoulsFormats;
using Xunit;

public class FlverMergeTests
{
    static FLVER2 F(string mtd, float bone)
    {
        var f = new FLVER2();
        f.Materials.Add(new FLVER2.Material { Name = "m", MTD = mtd });
        f.Nodes.Add(new FLVER.Node { Name = "b", Translation = new System.Numerics.Vector3(bone, 0, 0) });
        return f;
    }

    [Fact]
    public void SectionsChangedByOneSideEachAreCombined()
    {
        var r = FlverMerge.MergeSections(F("a.matxml", 0), F("a.matxml", 1), F("b.matxml", 0), out var notes);
        Assert.NotNull(r);
        Assert.Equal("b.matxml", r.Materials[0].MTD);
        Assert.Equal(1, r.Nodes[0].Translation.X);
    }

    [Fact]
    public void SectionChangedByBothGivesNoMerge()
    {
        Assert.Null(FlverMerge.MergeSections(F("a.matxml", 0), F("c.matxml", 0), F("b.matxml", 0), out _));
    }

    [Fact]
    public void DummyPolysAddedByEachSideAreUnited()
    {
        FLVER2 D(params short[] refs) { var f = F("a.matxml", 0); foreach (var r in refs) f.Dummies.Add(new FLVER.Dummy { ReferenceID = r }); return f; }
        var r = FlverMerge.MergeSections(D(1, 2), D(1, 2, 900), D(1, 2, 950, 951), out _);
        Assert.NotNull(r);
        Assert.Equal(new short[] { 1, 2, 900, 950, 951 }, r.Dummies.Select(d => d.ReferenceID));
    }
}
