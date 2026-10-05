using NRMerge;
using Xunit;

public class RegMergeTests
{
    [Fact]
    public void OneSidedEvChangeWins()
    {
        var v = RegMerge.MergeField(10, 25, 10, Side.MMV, out var conflict);
        Assert.Equal(25, v);
        Assert.False(conflict);
    }

    [Fact]
    public void OneSidedMmvChangeWins()
    {
        var v = RegMerge.MergeField(10, 10, 7, Side.EV, out var conflict);
        Assert.Equal(7, v);
        Assert.False(conflict);
    }

    [Fact]
    public void SameChangeOnBothSidesIsNotAConflict()
    {
        var v = RegMerge.MergeField(10, 3, 3, Side.EV, out var conflict);
        Assert.Equal(3, v);
        Assert.False(conflict);
    }

    [Fact]
    public void TwoSidedChangeUsesWinner()
    {
        Assert.Equal(5, RegMerge.MergeField(10, 5, 8, Side.EV, out var c1));
        Assert.True(c1);
        Assert.Equal(8, RegMerge.MergeField(10, 5, 8, Side.MMV, out var c2));
        Assert.True(c2);
    }

    [Fact]
    public void MissingBaseWithDifferentValuesUsesWinner()
    {
        Assert.Equal(8, RegMerge.MergeField(null, 5, 8, Side.MMV, out var c));
        Assert.True(c);
    }

    [Fact]
    public void CrossTypeNumericEqualityAgainstErBase()
    {
        // ER defines the field as int, NR as uint: same number means unchanged
        var v = RegMerge.MergeField(7, (uint)7, (uint)9, Side.EV, out var conflict);
        Assert.Equal((uint)9, v);
        Assert.False(conflict);
    }

    [Fact]
    public void VanillaPatchValueSurvivesWhenNeitherModChangedTheField()
    {
        var vanilla = new Dictionary<string, object> { ["hp"] = 100, ["patched"] = 2 };
        var evChanges = new Dictionary<string, object> { ["hp"] = 120 };      // EV delta vs 1.03.4 does not mention "patched"
        var mmvChanges = new Dictionary<string, object>();
        var merged = RegMerge.MergeRowFields(vanilla, evChanges, mmvChanges, f => Side.EV, out var conflicts);
        Assert.Equal(120, merged["hp"]);
        Assert.Equal(2, merged["patched"]);
        Assert.Empty(conflicts);
    }

    [Fact]
    public void PolicySendsRewardFieldsToMmvAndStatsToEv()
    {
        Assert.Equal(Side.EV, MergePolicy.Winner("NpcParam", "hp"));
        Assert.Equal(Side.MMV, MergePolicy.Winner("NpcParam", "itemLotId_enemy"));
        Assert.Equal(Side.MMV, MergePolicy.Winner("ItemLotParam_enemy", "lotItemId01"));
        Assert.Equal(Side.EV, MergePolicy.Winner("AtkParam_Npc", "atkPhys"));
        Assert.Equal(Side.MMV, MergePolicy.Winner("SmallBaseMapVariationParam", "anything"));
    }
}

public class RegMergeUnsetReferenceTests
{
    [Fact]
    public void UnsetReferenceDoesNotBeatARealReference()
    {
        // MMV wins the policy for rewards, but MMV's value is "none" while EV has a real reward lot
        var v = RegMerge.MergeField(null, 6110000, -1, Side.MMV, out var conflict, isRef: true);
        Assert.Equal(6110000, v);
        Assert.True(conflict);
    }

    [Fact]
    public void BothReferencesSetStillUsesWinner()
    {
        Assert.Equal(7, RegMerge.MergeField(null, 5, 7, Side.MMV, out _, isRef: true));
    }

    [Fact]
    public void NonReferenceFieldsIgnoreTheUnsetRule()
    {
        Assert.Equal(-1, RegMerge.MergeField(null, -1, 3, Side.EV, out _, isRef: false));
    }

    [Theory]
    [InlineData("itemLotId_enemy", true)]
    [InlineData("rewardItemLot_2", true)]
    [InlineData("spEffectID3", true)]
    [InlineData("chaosMatchingCorrectParamId", true)]
    [InlineData("hp", false)]
    [InlineData("atkSa_JustGuard", false)]
    public void ReferenceFieldsAreRecognisedByName(string field, bool expected)
    {
        Assert.Equal(expected, RegMerge.IsRefField(field));
    }
}

public class CollisionRuleTests
{
    static Dictionary<string, object> D(params (string, object)[] kv) => kv.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public void DifferentRowsWithoutACommonOriginalAreACollision()
    {
        var e = D(("hp", 100), ("a", 0), ("b", 0), ("c", 0));
        var m = D(("hp", 200), ("a", 0), ("b", 0), ("c", 0));
        Assert.True(RegMerge.IsCollisionCore("SpEffectParam", e, m, hasErBase: false, out _));
    }

    [Fact]
    public void SamePortWithDifferentTuningIsNotACollision()
    {
        var e = D(("hp", 81), ("a", 1), ("b", 2), ("c", 3));
        var m = D(("hp", 100), ("a", 1), ("b", 2), ("c", 3));
        Assert.False(RegMerge.IsCollisionCore("NpcParam", e, m, hasErBase: true, out _));
    }

    [Fact]
    public void DifferentModelOnAWeaponIsACollisionEvenWithAnErOriginal()
    {
        var e = D(("equipModelId", 1699), ("weight", 3));
        var m = D(("equipModelId", 559), ("weight", 3));
        Assert.True(RegMerge.IsCollisionCore("EquipParamWeapon", e, m, hasErBase: true, out var why));
        Assert.Contains("equipModelId", why);
    }
}
