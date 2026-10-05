using NRMerge;
using SoulsFormats;
using System.Numerics;
using Xunit;

public class ObjMergeTests
{
    public class Inner { public int X { get; set; } public int Y { get; set; } }
    public class Thing
    {
        public string Model { get; set; }
        public float Pos { get; set; }
        public Inner In { get; set; } = new();
        public int[] Arr { get; set; } = new int[2];
    }

    static Thing T(string model, float pos, int x, int y, int a0 = 0) => new() { Model = model, Pos = pos, In = new Inner { X = x, Y = y }, Arr = new[] { a0, 0 } };

    [Fact]
    public void OneSidedChangesFromBothSidesAreCombinedIncludingNestedProperties()
    {
        var b = T("c1", 0, 0, 0);
        var e = T("c2", 0, 0, 0);          // EV swaps the model
        var m = T("c1", 5, 0, 7, 9);       // MMV moves it, changes a nested field and an array
        var conflicts = new List<string>();
        var r = (Thing)ObjMerge.Merge(b, e, m, _ => Side.EV, conflicts);
        Assert.Equal("c2", r.Model);
        Assert.Equal(5, r.Pos);
        Assert.Equal(0, r.In.X);
        Assert.Equal(7, r.In.Y);
        Assert.Equal(9, r.Arr[0]);
        Assert.Empty(conflicts);
    }

    [Fact]
    public void PropertyChangedDifferentlyByBothUsesTheWinnerAndIsReported()
    {
        var b = T("c1", 0, 0, 0);
        var e = T("c2", 1, 0, 0);
        var m = T("c3", 2, 0, 0);
        var conflicts = new List<string>();
        var r = (Thing)ObjMerge.Merge(b, e, m, p => p == "Model" ? Side.EV : Side.MMV, conflicts);
        Assert.Equal("c2", r.Model);
        Assert.Equal(2, r.Pos);
        Assert.Equal(2, conflicts.Count);
    }
}

public class MsbMergeTests
{
    static MSB_NR.Part.Enemy Enemy(string name, string model, int npc, float x = 0)
        => new() { Name = name, ModelName = model, NpcParamId = npc, Position = new Vector3(x, 0, 0) };

    static MSB_NR Map(params MSB_NR.Part.Enemy[] enemies)
    {
        var m = new MSB_NR();
        foreach (var model in enemies.Select(e => e.ModelName).Distinct())
            m.Models.Enemies.Add(new MSB_NR.Model.Enemy { Name = model });
        foreach (var e in enemies) m.Parts.Enemies.Add(e);
        return m;
    }

    [Fact]
    public void AddedPartsDeletionsAndModelListAreMerged()
    {
        var b = Map(Enemy("c1000_9000", "c1000", 100), Enemy("c1000_9001", "c1000", 100), Enemy("c1000_9002", "c1000", 100));
        // EV swaps 9000's model to c2000 and edits 9002; MMV adds 9003 (model c3000), deletes 9001 and 9002, moves 9000.
        var e = Map(Enemy("c1000_9000", "c2000", 200), Enemy("c1000_9001", "c1000", 100), Enemy("c1000_9002", "c1000", 101));
        var m = Map(Enemy("c1000_9000", "c1000", 100, 5), Enemy("c1000_9003", "c3000", 300));

        var r = MsbMerge.MergeMsb(b, e, m, "test");

        var names = r.Parts.Enemies.Select(p => p.Name).ToList();
        Assert.Equal(new[] { "c1000_9000", "c1000_9002", "c1000_9003" }, names);   // 9002 kept: EV modified it
        var p0 = r.Parts.Enemies[0];
        Assert.Equal("c2000", p0.ModelName);
        Assert.Equal(200, p0.NpcParamId);
        Assert.Equal(5, p0.Position.X);
        var models = r.Models.Enemies.Select(x => x.Name).ToHashSet();
        Assert.Contains("c2000", models);
        Assert.Contains("c3000", models);
        Assert.Contains("c1000", models);
        // Write/re-read is checked by the msbmerge stage on real maps (default-constructed parts lack sub-structs).
    }

    [Fact]
    public void PartRenamedByOneSideWithTheSameEntityIdIsMatchedNotDuplicated()
    {
        MSB_NR.Part.Enemy Boss(string name, string model, float x) { var p = Enemy(name, model, 1, x); p.EntityData = new MSB_NR.Part.EntityStruct { EntityID = 46570800 }; return p; }
        var b = Map(Boss("c4270_9000", "c4270", 0));
        var e = Map(Boss("c4270_9000", "c5820", 0));      // EV swaps the boss model
        var m = Map(Boss("c5011_9000", "c5011", 3));      // MMV renames the part, swaps model, moves it

        var r = MsbMerge.MergeMsb(b, e, m, "test");

        var p = Assert.Single(r.Parts.Enemies);
        Assert.Equal("c4270_9000", p.Name);
        Assert.Equal("c5820", p.ModelName);
        Assert.Equal(3, p.Position.X);
    }

    [Fact]
    public void SpawnIdentityFieldsStayTogetherWhenBothSidesChangeThem()
    {
        MSB_NR.Part.Enemy P(string model, int npc, int variation, float x)
        { var p = Enemy("c4270_9000", model, npc, x); p.EntityData = new MSB_NR.Part.EntityStruct { Variation = variation }; return p; }
        var b = Map(P("c4270", 42700040, -1, 0));
        var e = Map(P("c2010", 20109120, -1, 0));     // EV swaps the boss
        var m = Map(P("c4270", 42700041, 1, 4));      // MMV picks another NPC row, puts the boss in variation slot 1, moves it
        m.Parts.Enemies[0].SpEffectSetParamIds = new[] { 43500010, 0, 0, 0 };   // and a boss-specific SpEffect set

        var r = MsbMerge.MergeMsb(b, e, m, "test");

        var p = Assert.Single(r.Parts.Enemies);
        Assert.Equal("c2010", p.ModelName);
        Assert.Equal(20109120, p.NpcParamId);
        Assert.Equal(0, p.SpEffectSetParamIds[0]);
        Assert.Equal(1, p.EntityData.Variation);       // MMV's map-variation slot: EV's boss fills the slot the vanilla boss had
        Assert.Equal(4, p.Position.X);
    }
}
