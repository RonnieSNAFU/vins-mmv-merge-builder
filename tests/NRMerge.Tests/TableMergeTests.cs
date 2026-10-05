using NRMerge;
using Xunit;

public class TableMergeTests
{
    static List<TableMerge.Entry> T(string spec) =>
        spec.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => new TableMerge.Entry(s[0].ToString(), int.Parse(s[1..]))).ToList();

    static string S(List<TableMerge.Entry> l) => string.Join(" ", l.Select(e => $"{e.Key}{e.Weight}"));

    [Fact]
    public void InsertionsFromBothModsAreKeptWithTheirWeights()
    {
        var r = TableMerge.MergeEntries(T("a100 b100 c100"), T("a100 X100 b100 c100"), T("a100 b100 Y150 c100"), out _);
        Assert.Equal("a100 X100 b100 Y150 c100", S(r));
    }

    [Fact]
    public void BaseEntryReplacedByBothKeepsBothWithHalvedWeights()
    {
        var r = TableMerge.MergeEntries(T("a100 b100"), T("a100 X100"), T("a100 Y100"), out var conflicts);
        Assert.Equal("a100 X50 Y50", S(r));
        Assert.Equal(1, conflicts);
    }

    [Fact]
    public void OddWeightsRoundAndNeverDropBelowOne()
    {
        var r = TableMerge.MergeEntries(T("b101"), T("X101"), T("Y1"), out _);
        Assert.Equal("X51 Y1", S(r));
    }

    [Fact]
    public void OneSidedDeletionIsApplied()
    {
        var r = TableMerge.MergeEntries(T("a100 b100"), T("a100 b100"), T("a100"), out _);
        Assert.Equal("a100", S(r));
    }

    [Fact]
    public void UntouchedTableIsUnchanged()
    {
        var r = TableMerge.MergeEntries(T("a5 b6 c7"), T("a5 b6 c7"), T("a5 b6 c7"), out var conflicts);
        Assert.Equal("a5 b6 c7", S(r));
        Assert.Equal(0, conflicts);
    }

    [Fact]
    public void SameSpotInsertionsAreAdditionsNotReplacements()
    {
        var r = TableMerge.MergeEntries(T("a100 c100"), T("a100 X100 c100"), T("a100 Y100 c100"), out _);
        Assert.Equal("a100 X100 Y100 c100", S(r));
    }
}

public class IdRemapTests
{
    [Fact]
    public void FindsSmallestOffsetThatFreesEveryId()
    {
        var used = new HashSet<int> { 5080000, 5080100, 5090000 };
        var k = IdRemap.FindFreeOffset(new[] { 5080000, 5080100 }, id => used.Contains(id), 10000);
        Assert.Equal(20000, k); // +10000 hits 5090000
    }

    [Fact]
    public void ParsesConditionalRefsFromMeta()
    {
        var specs = ParamRefs.ParseRefs("itemId", "EquipParamCustomWeapon(itemCategory=6),EquipParamWeapon(itemCategory=2),ItemTableParam(itemCategory=7)");
        Assert.Equal(3, specs.Count);
        Assert.Equal("EquipParamCustomWeapon", specs[0].Target);
        Assert.Equal("itemCategory", specs[0].CondField);
        Assert.Equal("6", specs[0].CondValue);
        var plain = ParamRefs.ParseRefs("phantomShaderId", "PhantomParam");
        Assert.Single(plain);
        Assert.Null(plain[0].CondField);
    }

    [Fact]
    public void RewritesOnlyMatchingConditionalReferences()
    {
        var specs = ParamRefs.ParseRefs("itemId", "EquipParamCustomWeapon(itemCategory=6),EquipParamWeapon(itemCategory=2)");
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["itemCategory"] = 6, ["itemId"] = 64500000 },
            new() { ["itemCategory"] = 2, ["itemId"] = 64500000 },
            new() { ["itemCategory"] = 6, ["itemId"] = 1 },
        };
        int n = IdRemap.RewriteRefs(rows, specs, "EquipParamCustomWeapon", 64500000, 64510000);
        Assert.Equal(1, n);
        Assert.Equal(64510000, rows[0]["itemId"]);
        Assert.Equal(64500000, rows[1]["itemId"]);
    }
}
