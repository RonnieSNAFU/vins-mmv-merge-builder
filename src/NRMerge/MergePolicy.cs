namespace NRMerge;

public enum Side { EV, MMV }

/// <summary>
/// Spec §5: when both mods change the same field differently, combat/balance goes to Elden Vins and
/// world/content placement (map variations, spawns, rewards, drops) goes to More Map Variations.
/// </summary>
public static class MergePolicy
{
    static readonly HashSet<string> MmvParams = new(StringComparer.Ordinal)
    {
        "ActionButtonParam", "AssetEnvironmentGeometryParam",
        "ChaosMatchingMutationEnemyTableParam",
        "ItemLotParam_enemy", "ItemLotParam_map",
        "LotResultPlayAreaParam", "LotResultSmallBaseAndSpot", "MapPatternSet", "RandomAppearParam",
        "SmallBaseAndSpotAttachPoint", "SmallBaseAndSpotDefine", "SmallBaseMapVariationParam", "SmallbaseInvationNpcParam",
        "WorldMapPointIconParam", "WorldMapPointParam", "WwiseValueToStrParam_BgmBossChrIdConv",
        "ShopLineupParam",
    };

    static readonly Dictionary<string, HashSet<string>> MmvFields = new(StringComparer.Ordinal)
    {
        ["NpcParam"] = new(StringComparer.Ordinal)
        {
            "itemLotId_enemy", "itemLotId_map", "rewardItemLot_1", "rewardItemLot_2",
            "chaosMatchingRewardLotId", "chaosMatchingItemLotId",
            "sleepCollectorItemLotId_enemy", "sleepCollectorItemLotId_map",
            "getSoul", "WanderGhostPhantomId",
        },
    };

    public static Side Winner(string param, string field)
    {
        if (MmvParams.Contains(param)) return Side.MMV;
        if (MmvFields.TryGetValue(param, out var f) && f.Contains(field)) return Side.MMV;
        return Side.EV;
    }
}
