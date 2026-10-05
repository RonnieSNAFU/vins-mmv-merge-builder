using NRMerge;
using SoulsFormats;
using Xunit;

public class FmgMergeTests
{
    static FMG F(params (int, string)[] e)
    {
        var f = new FMG { Version = FMG.FMGVersion.DarkSouls3 };
        foreach (var (id, t) in e) f.Entries.Add(new FMG.Entry(f, id, t));
        return f;
    }

    [Fact]
    public void EntriesAddedByEachSideArePresent()
    {
        var r = FmgMerge.Merge(F((1, "a")), F((1, "a"), (2, "ev")), F((1, "a"), (3, "mmv")), Side.EV, "t", out var conflicts);
        Assert.Equal(new[] { 1, 2, 3 }, r.Entries.Select(e => e.ID).OrderBy(x => x));
        Assert.Equal(0, conflicts);
    }

    [Fact]
    public void BothChangedUsesWinnerAndCountsConflict()
    {
        var r = FmgMerge.Merge(F((1, "a")), F((1, "ev")), F((1, "mmv")), Side.EV, "t", out var conflicts);
        Assert.Equal("ev", r.Entries.Single(e => e.ID == 1).Text);
        Assert.Equal(1, conflicts);
    }

    [Fact]
    public void OneSidedChangeWins()
    {
        var r = FmgMerge.Merge(F((1, "a"), (2, "b")), F((1, "a"), (2, "b")), F((1, "A"), (2, "b")), Side.EV, "t", out _);
        Assert.Equal("A", r.Entries.Single(e => e.ID == 1).Text);
    }

    [Fact]
    public void RemapMovesMmvTextToTheNewId()
    {
        var mmv = F((9100000, "Sword of Night"));
        FmgMerge.ApplyRemap(mmv, new Dictionary<long, long> { [9100000] = 9140000 });
        Assert.Single(mmv.Entries);
        Assert.Equal(9140000, mmv.Entries[0].ID);
    }
}
