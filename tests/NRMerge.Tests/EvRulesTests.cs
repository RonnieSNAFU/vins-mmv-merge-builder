using NRMerge;
using Xunit;

public class EvRulesTests
{
    static EvRules.Obs O(object b, object n, string g = "") => new(g, b, n);

    [Fact]
    public void DerivesMultiplierFromConsistentPairs()
    {
        var r = EvRules.DeriveMultiplier(new[] { O(100f, 250f), O(40f, 100f), O(0f, 0f), O(8f, 20f) });
        Assert.NotNull(r);
        Assert.Equal(2.5, r.K, 6);
    }

    [Fact]
    public void DerivesConstantFromConsistentPairs()
    {
        var r = EvRules.DeriveConstant(new[] { O(5, 0), O(7, 0), O(3, 0), O(0, 0) });
        Assert.NotNull(r);
        Assert.Equal("0", Regulation.Fmt(r.Const));
    }

    [Fact]
    public void RejectsInconsistentPairs()
    {
        var obs = new[] { O(100, 250), O(40, 7), O(30, 1), O(8, 99) };
        Assert.Null(EvRules.DeriveMultiplier(obs));
        Assert.Null(EvRules.DeriveConstant(obs));
        Assert.Null(EvRules.DeriveMap(obs));
    }

    [Fact]
    public void DerivesPerGroupMappingAndIgnoresUnchangedKeys()
    {
        var obs = new List<EvRules.Obs>();
        for (int i = 0; i < 10; i++) obs.Add(O(10, 0, "3"));
        obs.Add(O(10, 10, "3"));
        for (int i = 0; i < 8; i++) obs.Add(O(12, 2, "5"));
        for (int i = 0; i < 8; i++) obs.Add(O(7, 7, "9"));
        var r = EvRules.DeriveMap(obs);
        Assert.NotNull(r);
        Assert.Equal(2, r.Map.Count);
        Assert.Equal("0", Regulation.Fmt(r.Map[("3", "10")]));
        Assert.Equal("2", Regulation.Fmt(r.Map[("5", "12")]));
    }

    [Fact]
    public void AppliesMultiplierWithIntegerRounding()
    {
        var r = new EvRules.Rule { Kind = "mul", K = 0.81 };
        Assert.Equal(810, EvRules.Transform(r, "", 1000));
        Assert.Equal(1069, EvRules.Transform(r, "", 1320));
        Assert.Equal(250f, EvRules.Transform(r with { K = 2.5 }, "", 100f));
    }

    [Fact]
    public void MapRuleLeavesUnknownValuesAlone()
    {
        var r = new EvRules.Rule { Kind = "map", Map = new() { [("3", "10")] = 0 } };
        Assert.Equal(0, EvRules.Transform(r, "3", 10));
        Assert.Equal(58, EvRules.Transform(r, "94", 58));
    }
}

public class EvRulesRefinementTests
{
    static EvRules.Obs O(object b, object n, string g = "") => new(g, b, n);

    [Fact]
    public void MultiplierPicksBestFittingFactorDespiteInconsistentRounding()
    {
        // EV's HP conversion: x0.81 with mixed floor/round
        var obs = new[] { O(1000, 810), O(1320, 1069), O(1001, 811), O(2500, 2025), O(777, 629), O(4321, 3500), O(999, 809) };
        var r = EvRules.DeriveMultiplier(obs);
        Assert.NotNull(r);
        Assert.Equal(0.81, r.K, 3);
    }

    [Fact]
    public void LargeIdsUnchangedDoNotLookLikeAMultiplier()
    {
        var obs = new List<EvRules.Obs>();
        for (int i = 0; i < 40; i++) obs.Add(O(100000 + i * 10, 100000 + i * 10));
        for (int i = 0; i < 4; i++) obs.Add(O(200000 + i, 200020 + i));
        Assert.Null(EvRules.DeriveMultiplier(obs));
    }

    [Fact]
    public void GroupConstantAppliesToAnyBaseValueOfThatGroup()
    {
        var obs = new List<EvRules.Obs>();
        for (int i = 0; i < 10; i++) obs.Add(O(40 + i, 100, "67"));
        obs.Add(O(55, 55, "67"));
        for (int i = 0; i < 10; i++) obs.Add(O(20 + i, 20 + i, "3")); // unchanged type
        var r = EvRules.DeriveGroupConstant(obs);
        Assert.NotNull(r);
        Assert.Equal(100, EvRules.Transform(r, "67", 33));
        Assert.Equal(33, EvRules.Transform(r, "3", 33));
    }
}

public class EvRulesTypeRuleTests
{
    static EvRules.Obs O(object b, object n, string g = "") => new(g, b, n);

    [Fact]
    public void GroupConstantRejectedWhenASizeableSubgroupKeepsItsValue()
    {
        var obs = new List<EvRules.Obs>();
        for (int i = 0; i < 40; i++) obs.Add(O(20000010, 999990000, "3"));
        for (int i = 0; i < 10; i++) obs.Add(O(-1, -1, "3"));       // unique weapons keep their fixed skill
        var r = EvRules.DeriveTypeRule(obs);
        Assert.NotNull(r);
        Assert.Equal(999990000, EvRules.Transform(r, "3", 20000010));
        Assert.Equal(-1, EvRules.Transform(r, "3", -1));
        Assert.Equal(21500810, EvRules.Transform(r, "3", 21500810));
    }

    [Fact]
    public void TypeRuleUsesGroupConstantForUnseenValuesWhenEveryValueMoved()
    {
        var obs = new List<EvRules.Obs>();
        for (int i = 0; i < 10; i++) obs.Add(O(60f, 180f, "9"));
        for (int i = 0; i < 5; i++) obs.Add(O(45f, 180f, "9"));
        var r = EvRules.DeriveTypeRule(obs);
        Assert.Equal(180f, EvRules.Transform(r, "9", 30f));
    }

    [Theory]
    [InlineData(new object[] { new[] { 0, 1, 2, 1, 2, 2, 0 }, true })]
    public void FewDistinctValuesAreCategorical(int[] values, bool expected)
    {
        Assert.Equal(expected, EvRules.IsCategorical("guardmotionCategory", values.Select(v => new EvRules.Obs("", v, v))));
    }

    [Fact]
    public void ManyDistinctValuesAreNotCategorical()
    {
        Assert.False(EvRules.IsCategorical("hp", Enumerable.Range(1, 500).Select(v => new EvRules.Obs("", v * 7, v * 7))));
    }

    [Theory]
    [InlineData("90", "35")]
    [InlineData("92", "15")]
    [InlineData("94", "9")]
    [InlineData("95", "5")]
    [InlineData("96", "13")]
    [InlineData("3", null)]
    public void UniqueMmvWeaponTypesBorrowAnAnalogType(string type, string analog)
    {
        Assert.Equal(analog, EvRules.AnalogType(type));
    }
}

public class EvRulesCloneTests
{
    [Fact]
    public void PicksTheMostSimilarVanillaRowIgnoringScaledFields()
    {
        var row = new Dictionary<string, object> { ["hp"] = 5000, ["think"] = 7, ["lot"] = 3, ["sp"] = 20 };
        var a = new Dictionary<string, object> { ["hp"] = 4000, ["think"] = 7, ["lot"] = 9, ["sp"] = 20 };
        var b = new Dictionary<string, object> { ["hp"] = 5000, ["think"] = 1, ["lot"] = 9, ["sp"] = 99 };
        var id = EvRules.PickSource(row, new[] { (50700000, a), (50700010, b) }, new HashSet<string> { "hp" });
        Assert.Equal(50700000, id);
    }
}
